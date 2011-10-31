using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using StockSharp.BusinessEntities;
using StockSharp.Algo.Storages;
using StockSharp.Algo.Testing;
using StockSharp.Algo.Candles;
using System.Threading;
using StockSharp.Algo.Indicators.Trend;
using Ecng.Serialization;

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

        public EmaStrategy BestStrategy { get; protected set; }

        private readonly TimeSpan _timeFrame = TimeSpan.FromMinutes(5);

        private DateTime _startTime;
        private DateTime _stopTime;

        private Security _security;
        private TradingStorage _storage;
        private Portfolio _portfolio;



        public EMAStrategyOptimizer(Security security, TradingStorage storage, Portfolio portfolio, DateTime startTime, DateTime stopTime)
        {
            _startTime = startTime;
            _stopTime = stopTime;

            _security = security;
            _storage = storage;
            _portfolio = portfolio;
        }

        public void Optimize()
        {
            this.OnStateChanged(OptimizationState.Began);

            Thread t = new Thread(new ThreadStart(AsyncOptimize));
            t.Start();
        }

        private void AsyncOptimize()
        {
            EmulationTrader trader = new EmulationTrader(
                new[] { _security },
                new[] { _portfolio })
            {
                MarketTimeChangedInterval = _timeFrame,
                Storage = _storage,
                WorkingTime = Exchange.Rts.WorkingTime,

                // параметр влияет на занимаемую память.
                // в случае достаточно количества памяти на компьютере рекомендуется его увеличить
                DaysInMemory = 100,
            };

            trader.DepthGenerators[_security] = new TrendMarketDepthGenerator(_security)
            {
                // стакан для инструмента в истории обновляется раз в 60 (1) секунд
                Interval = TimeSpan.FromSeconds(10),
            };

            CandleManager candleManager = new CandleManager();

            var builder = new CandleBuilder(new SyncTraderTradeSource(trader));
            candleManager.Sources.Add(builder);

            candleManager.RegisterTimeFrameCandles(_security, _timeFrame);

            // соединяемся с трейдером и запускаем экспорт,
            // чтобы инициализировать переданными инструментами и портфелями необходимые свойства EmulationTrader
            trader.Connect();
            trader.StartExport();

            List<EmaStrategy> performedStrategies = new List<EmaStrategy>();

            // sync object
            EventWaitHandle waitHandle = new EventWaitHandle(true, EventResetMode.ManualReset); ;

            // prepare optimization sets
            List<int> filterOptPeriods = new List<int>() { 90, 100, 110 };
            List<int> longOptPeriods = new List<int>() { 12, 13, 14 };
            List<int> shortOptPeriods = new List<int>() { 9, 10, 11 };

            foreach (int filterOptPeriod in filterOptPeriods)
            {
                foreach (int longOptPeriod in longOptPeriods)
                {
                    foreach (int shortOptPeriod in shortOptPeriods)
                    {
                        var strategy = new EmaStrategy(candleManager,
                            new ExponentialMovingAverage { Length = filterOptPeriod },
                            new ExponentialMovingAverage { Length = longOptPeriod }, new ExponentialMovingAverage { Length = shortOptPeriod },
                            _timeFrame)
                        {
                            Volume = 1,
                            Portfolio = _portfolio,
                            Security = _security,
                            Trader = trader
                        };

                        performedStrategies.Add(strategy);

                        trader.StateChanged += () =>
                        {
                            if (trader.State == EmulationStates.Started)
                            {
                                waitHandle.Reset();
                                strategy.Start();
                            }
                            else if (trader.State == EmulationStates.Stopped)
                            {
                                waitHandle.Set();
                            }
                        };

                        waitHandle.WaitOne();
                        trader.Start(_startTime, _stopTime);
                    }
                }
            }

            this.BestStrategy = performedStrategies.OrderByDescending(s => s.PnLManager.PnL).FirstOrDefault();
            this.OnStateChanged(OptimizationState.Finished);
        }

        public delegate void StateChangedHandler();
        public event StateChangedHandler StateChanged;

        protected void OnStateChanged(OptimizationState state)
        {
            this.State = state;

            if (StateChanged != null)
            {
                StateChanged();
            }
        }
    }
}
