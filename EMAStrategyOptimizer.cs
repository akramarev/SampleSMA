using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

        public EMAEventModelStrategy BestStrategy { get; protected set; }
        public List<EMAEventModelStrategy> Strategies { get; protected set; }

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

            this.Strategies = new List<EMAEventModelStrategy>();

            this.Volume = 1;
        }

        //public void Optimize()
        //{
        //    // clean up
        //    this.BestStrategy = null;
        //    this.Strategies.Clear();

        //    this.OnStateChanged(OptimizationState.Began);

        //    Thread t = new Thread(new ThreadStart(AsyncOptimize));
        //    t.Start();
        //}

        //private void AsyncOptimize()
        //{
        //    var basketTrader = new BasketTrader { SupportTradesUnique = false };

        //    OptVarItem[] optVars = this.GetOptVarField().ToArray();
        //    ManualResetEvent[] doneEvents = new ManualResetEvent[optVars.Length];

        //    for (int i = 0; i < optVars.Length; i++)
        //    {
        //        doneEvents[i] = new ManualResetEvent(false);

        //        EmulationTrader trader = GetOptTraderContext(optVars[i].FilterOptPeriod, optVars[i].LongOptPeriods, optVars[i].ShortOptPeriods, doneEvents[i]);
        //        basketTrader.InnerTraders.Add(trader);
        //    }

        //    EmulationTrader[] traders = basketTrader.InnerTraders.Cast<EmulationTrader>().ToArray();

        //    for (int i = 0; i < traders.Length; i++)
        //    {
        //        traders[i].Connect();
        //        traders[i].StartExport();

        //        traders[i].Start(_startTime, _stopTime);
        //        //doneEvents[i].WaitOne();
        //    }

        //    WaitHandle.WaitAll(doneEvents);

        //    this.Strategies.ForEach(s => Log.AddLog(new LogMessage(Log, DateTime.Now, LogLevels.Debug, 
        //        String.Format("Opt: {0}, {1}, {2}. PnL: {3} ", s.FilterMA.Length, s.LongMA.Length, s.ShortMA.Length, s.PnLManager.PnL))));

        //    this.BestStrategy = Strategies.OrderByDescending(s => s.PnLManager.PnL).FirstOrDefault();
        //    this.OnStateChanged(OptimizationState.Finished);
        //}

        protected void OnStateChanged(OptimizationState state)
        {
            this.State = state;

            if (StateChanged != null)
            {
                StateChanged();
            }
        }

        public EmulationTrader GetOptTraderContext(TimeSpan timeFrame, int filterOptPeriod, int longOptPeriod, int shortOptPeriod, ManualResetEvent doneEvent)
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
                MarketTimeChangedInterval = timeFrame,
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

            var series = new CandleSeries(typeof(TimeFrameCandle), trader.Securities.First(), timeFrame);
            var candleManager = new CandleManager(trader);
            candleManager.Start(series);

            var strategy = new EMAEventModelStrategy(series,
                new ExponentialMovingAverage { Length = filterOptPeriod },
                new ExponentialMovingAverage { Length = longOptPeriod }, 
                new ExponentialMovingAverage { Length = shortOptPeriod })
            {
                Volume = this.Volume,
                Portfolio = portfolio,
                Security = security,
                Trader = trader
            };

            this.Strategies.Add(strategy);

            trader.StateChanged += (oldState, newState) =>
            {
                if (trader.State == EmulationStates.Started)
                {
                    strategy.Start();
                }
                else if (trader.State == EmulationStates.Stopped)
                {
                    doneEvent.Set();
                }        
            };

            return trader;
        }

        public struct OptVarItem
        {
            public int FilterOptPeriod;
            public int LongOptPeriods;
            public int ShortOptPeriods;

            public OptVarItem(int filterOptPeriod, int longOptPeriods, int shortOptPeriods)
            {
                FilterOptPeriod = filterOptPeriod;
                LongOptPeriods = longOptPeriods;
                ShortOptPeriods = shortOptPeriods;
            }
        }

        private List<OptVarItem> GetOptVarField()
        {
            List<OptVarItem> result = new List<OptVarItem>();

            for (int a = 90; a <= 90; a += 10)
            {
                for (int b = 12; b <= 15; b += 1)
                {
                    for (int c = 9; c <= 11; c += 1)
                    {
                        result.Add(new OptVarItem(a, b, c));
                    }
                }
            }

            return result;
        }
    }
}
