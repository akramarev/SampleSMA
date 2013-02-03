using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Ecng.Collections;
using Ecng.Common;
using StockSharp.Algo.Candles;
using StockSharp.Algo.Candles.Compression;
using StockSharp.Algo.Indicators.Trend;
using StockSharp.Algo.Storages;
using StockSharp.Algo.Testing;
using StockSharp.BusinessEntities;
using StockSharp.Algo;
using SampleSMA.Logging;
using StockSharp.Logging;

namespace SampleSMA
{
    public enum OptimizationState
    {
        None,
        Began,
        Finished
    }

    public class EMAStrategyOptimizer
    {
        public OptimizationState State
        {
            get;
            protected set;
        }

        private Object _bestResultLock = new object();
        public KeyValuePair<OptVarItem, EMAEventModelStrategy> BestResult { get; private set; }

        private DateTime _startTime;
        private DateTime _stopTime;

        private Security _security;
        private StorageRegistry _storage;
        private Portfolio _portfolio;

        public decimal Volume { get; set; }
        public bool UseQuoting { get; set; }

        public delegate void StateChangedHandler();
        public event StateChangedHandler StateChanged;

        public SimpleLogSource Log { get; set; }

        public EMAStrategyOptimizer(Security security, StorageRegistry storage, Portfolio portfolio, DateTime startTime, DateTime stopTime)
        {
            _startTime = startTime;
            _stopTime = stopTime;

            _security = security;
            _portfolio = portfolio;

            _storage = storage;

            this.Volume = 1;
            this.UseQuoting = false;
        }

        public void Optimize()
        {
            // clean up
            this.BestResult = new KeyValuePair<OptVarItem, EMAEventModelStrategy>();

            this.OnStateChanged(OptimizationState.Began);
            ThreadPool.QueueUserWorkItem(AsyncOptimize);
        }

        private void AsyncOptimize(Object threadContext)
        {
            var optVars = this.GetOptVarField().ToArray();

            Log.AddLog(new LogMessage(Log, DateTime.Now, LogLevels.Info, 
                optVars.Length > 1 
                    ? String.Format("{0} optVarItems are going to be optimized", optVars.Length)
                    : "1 optVarItem is going to be optimized"));

            using (var countdownEvent = new CountdownEvent(optVars.Length))
            {
                for (int i = 0; i < optVars.Length; i++)
                {
                    var a = i;

                    ThreadPool.QueueUserWorkItem(state =>
                    {
                        var done = new ManualResetEvent(false);
                        var context = GetOptContext(optVars[a]);

                        var trader = context.Value.Trader as EmulationTrader;
                        trader.StateChanged += (oldState, newState) =>
                        {
                            if (trader.State == EmulationStates.Stopped)
                            {
                                done.Set();
                            }
                        };

                        Stopwatch sw = new Stopwatch();
                        sw.Start();

                        trader.Start(_startTime, _stopTime);
                        done.WaitOne();

                        sw.Stop();

                        Log.AddLog(new LogMessage(Log, DateTime.Now, LogLevels.Info,
                        String.Format("OptVarItem #{0} done ({1}). Result: PnL: {2}, {3}",
                                a,
                                sw.Elapsed,
                                context.Value.PnLManager.PnL,
                                context.Key
                            )));

                        lock (_bestResultLock)
                        {
                            if (this.BestResult.Value == null ||
                                this.BestResult.Value.PnLManager.PnL <= context.Value.PnLManager.PnL)
                            {
                                this.BestResult = context;
                            }
                            else
                            {
                                // try to cleanup memory, the last private field in EmulationTrader
                                // #=qUTBJ0c9uFmGWYx4a3_oZjOoV9pJDtArCh9oL5k$U8DQ= {Ecng.Collections.CachedSynchronizedDictionary<StockSharp.BusinessEntities.Security,StockSharp.Algo.Testing.MarketDepthGenerator>}  Ecng.Collections.CachedSynchronizedDictionary<StockSharp.BusinessEntities.Security,StockSharp.Algo.Testing.MarketDepthGenerator>
                                //var value = context.Value.Trader.GetType().GetField("#=qHvivsYU2tNspR3_h$VF0nqA$yDC50HFX_RHAxeUi6UE=", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(context.Value.Trader);
                                //value.GetType().GetMethod("Clear").Invoke(value, null);

                                context.Value.Trader.Dispose();
                                context.Value.Trader = null;
                                context.Value.Dispose();
                            }
                        }

                        countdownEvent.Signal();
                    }, i);
                }

                countdownEvent.Wait();
            }

            GC.Collect();

            this.OnStateChanged(OptimizationState.Finished);
        }

        protected void OnStateChanged(OptimizationState state)
        {
            this.State = state;

            if (StateChanged != null)
            {
                StateChanged();
            }
        }

        public KeyValuePair<OptVarItem, EMAEventModelStrategy> GetOptContext(OptVarItem optVarItem)
        {
            // clone doesn't work for some reason
            var security = new Security
            {
                Id = _security.Id,
                Code = _security.Code,
                Name = _security.Name,
                MinStepSize = _security.MinStepSize,
                MinStepPrice = _security.MinStepPrice,
                ExchangeBoard = _security.ExchangeBoard,
                MaxPrice = 99999,
                MinPrice = 1
            };

            // Create local Storage to make it disposable after optimization
            var storage = new StorageRegistry();
            ((LocalMarketDataDrive) storage.DefaultDrive).Path = ((LocalMarketDataDrive) _storage.DefaultDrive).Path;
            ((LocalMarketDataDrive) storage.DefaultDrive).UseAlphabeticPath = true;

            var portfolio = new Portfolio { BeginValue = _portfolio.BeginValue };

            EmulationTrader trader = new EmulationTrader(
                new[] { security },
                new[] { portfolio })
            {
                MarketTimeChangedInterval = optVarItem.TimeFrame,
                StorageRegistry = storage,
                UseMarketDepth = true,
                //UseCandlesTimeFrame = optVarItem.TimeFrame
            };

            if (trader.UseMarketDepth)
            {
                trader.MarketEmulator.Settings.DepthExpirationTime = TimeSpan.FromHours(1); // Default: TimeSpan.FromDays(1);
                var marketDepthGenerator = new TrendMarketDepthGenerator(security)
                {
                    // стакан для инструмента в истории обновляется раз в 10 секунд
                    Interval = TimeSpan.FromSeconds(10),
                    //MaxAsksDepth = 5,
                    //MaxBidsDepth = 5
                };

                trader.RegisterMarketDepth(marketDepthGenerator);

                trader.StateChanged += (oldState, newState) =>
                {
                    if (trader.State == EmulationStates.Stopped)
                    {
                        trader.UnRegisterMarketDepth(marketDepthGenerator);
                        marketDepthGenerator = null;
                    }
                };
            }

            // соединяемся с трейдером и запускаем экспорт,
            // чтобы инициализировать переданными инструментами и портфелями необходимые свойства EmulationTrader
            trader.Connect();
            trader.StartExport();

            var series = new CandleSeries(typeof(TimeFrameCandle), trader.Securities.First(), optVarItem.TimeFrame);
            var candleManager = new CandleManager(trader);
            candleManager.Start(series);

            var strategy = new EMAEventModelStrategy(series,
                new ExponentialMovingAverage { Length = optVarItem.FilterOptPeriod },
                new ExponentialMovingAverage { Length = optVarItem.LongOptPeriods },
                new ExponentialMovingAverage { Length = optVarItem.ShortOptPeriods },
                optVarItem.TakeProfitUnit, optVarItem.StopLossUnit)
            {
                Volume = this.Volume,
                Portfolio = portfolio,
                Security = security,
                Trader = trader,
                UseQuoting = this.UseQuoting
            };

            trader.StateChanged += (oldState, newState) =>
            {
                if (trader.State == EmulationStates.Started)
                {
                    strategy.Start();
                }
                else if (trader.State == EmulationStates.Stopped)
                {
                    strategy.Stop();
                    candleManager = null;
                    storage = null;
                }        
            };

            var result = new KeyValuePair<OptVarItem, EMAEventModelStrategy>(optVarItem, strategy);
            return result;
        }

        public struct OptVarItem
        {
            public TimeSpan TimeFrame;

            public int FilterOptPeriod;
            public int LongOptPeriods;
            public int ShortOptPeriods;

            public Unit TakeProfitUnit;
            public Unit StopLossUnit;

            public OptVarItem(TimeSpan timeFrame, 
                int filterOptPeriod, int longOptPeriods, int shortOptPeriods,
                Unit takeProfitUnit, Unit stopLossUnit)
            {
                TimeFrame = timeFrame;

                FilterOptPeriod = filterOptPeriod;
                LongOptPeriods = longOptPeriods;
                ShortOptPeriods = shortOptPeriods;

                TakeProfitUnit = takeProfitUnit;
                StopLossUnit = stopLossUnit;
            }

            public override string ToString()
            {
                return String.Format("{0}m, {1}, {2}, {3}, {4}tp, {5}sl",
                                     this.TimeFrame.Minutes,
                                     this.FilterOptPeriod, this.LongOptPeriods, this.ShortOptPeriods,
                                     this.TakeProfitUnit, this.StopLossUnit);
            }
        }

        private List<OptVarItem> GetOptVarField()
        {
            List<OptVarItem> result = new List<OptVarItem>();

            // --216--
            foreach (int t in new[] { 1 })
            {
                for (int a = 90; a <= 90; a += 10)
                {
                    for (int b = 12; b <= 15; b++)
                    {
                        for (int c = 9; c <= 11; c++)
                        {
                            for (int tp = 20; tp <= 50; tp += 10)
                            {
                                for (int sl = 30; sl <= 50; sl += 10)
                                {
                                    result.Add(new OptVarItem(TimeSpan.FromMinutes(t), a, b, c, tp, sl));
                                }
                            }
                        }
                    }
                }
            }

            // --36--
            //foreach (int t in new[] { 1, 10 })
            //{
            //    for (int a = 84; a <= 90; a += 10)
            //    {
            //        for (int b = 12; b <= 15; b++)
            //        {
            //            for (int c = 9; c <= 11; c++)
            //            {
            //                for (int tp = 20; tp < 40; tp += 10)
            //                {
            //                    for (int sl = 40; sl < 50; sl += 10)
            //                    {
            //                        result.Add(new OptVarItem(TimeSpan.FromMinutes(t), a, b, c, tp, sl));
            //                    }
            //                }
            //            }
            //        }
            //    }
            //}

            return result;
        }
    }
}
