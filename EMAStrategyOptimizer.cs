using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ecng.Collections;
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

        public KeyValuePair<OptVarItem, EMAEventModelStrategy> BestResult { get; protected set; }
        public Dictionary<OptVarItem, EMAEventModelStrategy> Results { get; protected set; }

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
            _storage = storage;
            _portfolio = portfolio;

            this.Results = new Dictionary<OptVarItem, EMAEventModelStrategy>();

            this.Volume = 1;
            this.UseQuoting = false;
        }

        public void Optimize()
        {
            // clean up
            this.BestResult = new KeyValuePair<OptVarItem, EMAEventModelStrategy>();
            this.Results.Clear();

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
                    var done = new ManualResetEvent(false);
                    var context = GetOptContext(optVars[i], done);

                    ThreadPool.QueueUserWorkItem(state =>
                    {
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

                        // Try to clean up
                        if (Results.Any(kv => kv.Value.PnLManager.PnL > context.Value.PnLManager.PnL))
                        {
                            Results.Remove(context.Key);
                        }

                        countdownEvent.Signal();
                    }, i);
                }

                countdownEvent.Wait();
            }

            this.BestResult = Results.OrderByDescending(kv => kv.Value.PnLManager.PnL).FirstOrDefault();
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

        public KeyValuePair<OptVarItem, EMAEventModelStrategy> GetOptContext(OptVarItem optVarItem, ManualResetEvent doneEvent)
        {
            // clone doesn't work for some reason
            var security = new Security
            {
                Id = _security.Id,
                Code = _security.Code,
                Name = _security.Name,
                MinStepSize = _security.MinStepSize,
                MinStepPrice = _security.MinStepPrice,
                Exchange = _security.Exchange,
                MaxPrice = Decimal.MaxValue,
                MinPrice = Decimal.MinValue
            };

            // тестовый портфель
            var portfolio = new Portfolio { BeginValue = _portfolio.BeginValue };

            EmulationTrader trader = new EmulationTrader(
                new[] { security },
                new[] { portfolio })
            {
                MarketTimeChangedInterval = optVarItem.TimeFrame,
                StorageRegistry = _storage,
                UseMarketDepth = this.UseQuoting,
            };

            if (this.UseQuoting)
            {
                trader.MarketEmulator.Settings.DepthExpirationTime = TimeSpan.FromHours(1); // Default: TimeSpan.FromDays(1);
                var marketDepthGenerator = new TrendMarketDepthGenerator(security)
                {
                    // стакан для инструмента в истории обновляется раз в секунду
                    Interval = TimeSpan.FromSeconds(10),
                    //MaxPriceStepCount = 10,
                    //MaxAsksDepth = 10,
                    //MaxBidsDepth = 10
                };

                trader.RegisterMarketDepth(marketDepthGenerator);
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

            var result = new KeyValuePair<OptVarItem, EMAEventModelStrategy>(optVarItem, strategy);
            this.Results.Add(result.Key, result.Value);

            trader.StateChanged += (oldState, newState) =>
            {
                if (trader.State == EmulationStates.Started)
                {
                    strategy.Start();
                }
                else if (trader.State == EmulationStates.Stopped)
                {
                    strategy.Stop();
                }        
            };

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

            foreach (int t in new[] { 1, 5, 10 })
            {
                for (int a = 90; a <= 90; a += 10)
                {
                    for (int b = 12; b <= 15; b++)
                    {
                        for (int c = 9; c <= 11; c++)
                        {
                            for (int tp = 40; tp < 70; tp += 10)
                            {
                                for (int sl = 30; sl < 50; sl += 10)
                                {
                                    result.Add(new OptVarItem(TimeSpan.FromMinutes(t), a, b, c, tp, sl));
                                }
                            }
                        }
                    }
                }
            }

            return result;
        }
    }
}
