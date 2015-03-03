using System;
using System.Collections.Generic;
using System.Linq;
using StockSharp.Algo;
using StockSharp.Algo.Candles;
using StockSharp.Algo.Indicators;
using StockSharp.Algo.Indicators.Trend;
using StockSharp.Algo.Strategies;
using StockSharp.BusinessEntities;
using StockSharp.Logging;

namespace SampleSMA
{
    public class StopLossCandleStrategy : Strategy
    {
        public CandleSeries CandleSeries { get; private set; }
        public MyTrade Trade { get; set; }
        public Unit StopLossUnit { get; set; }

        public StopLossCandleStrategy(CandleSeries series, MyTrade trade, Unit stopLossUnit)
        {
            this.CandleSeries = series;
            this.Trade = trade;
            this.StopLossUnit = stopLossUnit;
		}

        protected override void OnStarted()
        {
            this.AddInfoLog("StopLossCandleStrategy strategy {0} has been started.", this.Name);

            this
                .CandleSeries
                .WhenCandlesFinished()
                .Do(ProcessCandle)
                .Apply(this);

            base.OnStarted();
        }

        protected override void OnStopped()
        {
            this.AddInfoLog("StopLossCandleStrategy strategy {0} has been stopped.", this.Name);

            base.OnStopped();
        }

        protected void ProcessCandle(Candle candle)
        {
            if (Trade.GetPnL() <= -StopLossUnit)
            {
                OrderDirections direction = this.Trade.Order.Direction.Invert();
                decimal price = Security.LastTrade.Price;
                Order order = this.CreateOrder(direction, price, this.Trade.Order.Volume);

                RegisterOrder(order);
            }
        }
    }
}
