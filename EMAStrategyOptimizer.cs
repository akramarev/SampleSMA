using System;
using System.Collections.Generic;
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

        public KeyValuePair<OptVarItem, EMAEventModelStrategy> BestStrategy { get; protected set; }
        public Dictionary<OptVarItem, EMAEventModelStrategy> Strategies { get; protected set; }

        private DateTime _startTime;
        private DateTime _stopTime;

        private Security _security;
        private StorageRegistry _storage;
        private Portfolio _portfolio;

        public decimal Volume { get; set; }

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

            this.Strategies = new Dictionary<OptVarItem, EMAEventModelStrategy>();

            this.Volume = 1;
        }

        public void Optimize()
        {
            // clean up
            this.BestStrategy = new KeyValuePair<OptVarItem, EMAEventModelStrategy>();
            this.Strategies.Clear();

            this.OnStateChanged(OptimizationState.Began);

            Thread t = new Thread(AsyncOptimize);
            t.Start();
        }

        private void AsyncOptimize()
        {
            var basketTrader = new BasketTrader { SupportTradesUnique = false };

            OptVarItem[] optVars = this.GetOptVarField().ToArray();
            ManualResetEvent[] doneEvents = new ManualResetEvent[optVars.Length];

            Log.AddLog(new LogMessage(Log, DateTime.Now, LogLevels.Info, 
                optVars.Length > 1 
                    ? String.Format("{0} optVarItems are going to be optimized", optVars.Length)
                    : "1 optVarItem is going to be optimized"));

            for (int i = 0; i < optVars.Length; i++)
            {
                doneEvents[i] = new ManualResetEvent(false);

                EmulationTrader trader = GetOptTraderContext(optVars[i], doneEvents[i]);
                basketTrader.InnerTraders.Add(trader);
            }

            EmulationTrader[] traders = basketTrader.InnerTraders.Cast<EmulationTrader>().ToArray();

            for (int i = 0; i < traders.Length; i++)
            {
                traders[i].Connect();
                traders[i].StartExport();

                traders[i].Start(_startTime, _stopTime);
                //doneEvents[i].WaitOne();
            }

            WaitHandle.WaitAll(doneEvents);

            //this.Strategies.ForEach(s => Log.AddLog(new LogMessage(Log, DateTime.Now, LogLevels.Debug,
            //    String.Format("Opt: {0}m, {1}, {2}, {3} PnL: {4} ", 
            //    s.Key.TimeFrame.Minutes, 
            //    s.Key.FilterOptPeriod, s.Key.LongOptPeriods, s.Key.ShortOptPeriods, 
            //    s.Value.PnLManager.PnL))));

            this.BestStrategy = Strategies.OrderByDescending(kv => kv.Value.PnLManager.PnL).FirstOrDefault();
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

        public EmulationTrader GetOptTraderContext(OptVarItem optVarItem, ManualResetEvent doneEvent)
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
                UseMarketDepth = true
            };

            trader.RegisterMarketDepth(new TrendMarketDepthGenerator(security)
            {
                // стакан для инструмента в истории обновляется раз в секунду
                Interval = TimeSpan.FromSeconds(1),
            });

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
                Trader = trader
            };

            this.Strategies.Add(optVarItem, strategy);

            trader.StateChanged += (oldState, newState) =>
            {
                if (trader.State == EmulationStates.Started)
                {
                    strategy.Start();
                }
                else if (trader.State == EmulationStates.Stopped)
                {
                    trader = null;
                    GC.Collect();

                    doneEvent.Set();
                }        
            };

            return trader;
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

            //foreach (int t in new[] { 1, 5, 10 })
            //{
            //    for (int a = 90; a <= 90; a += 10)
            //    {
            //        for (int b = 12; b <= 15; b++)
            //        {
            //            for (int c = 9; c <= 11; c++)
            //            {
            //                for (int tp = 40; tp < 70; tp += 10)
            //                {
            //                    for (int sl = 30; sl < 50; sl += 10)
            //                    {
            //                        result.Add(new OptVarItem(TimeSpan.FromMinutes(t), a, b, c, tp, sl));
            //                    }
            //                }
            //            }
            //        }
            //    }
            //}

            foreach (int t in new[] { 1, 5, 10 })
            {
                for (int a = 90; a <= 90; a += 10)
                {
                    for (int b = 12; b <= 15; b++)
                    {
                        for (int c = 9; c <= 11; c++)
                        {
                            for (int tp = 40; tp < 50; tp += 10)
                            {
                                for (int sl = 30; sl < 40; sl += 10)
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
