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
    public class EMAEventModelStrategy : Strategy
    {
        public ExponentialMovingAverage FilterMA { get; private set; }
        public ExponentialMovingAverage LongMA { get; private set; }
        public ExponentialMovingAverage ShortMA { get; private set; }

        public Unit TakeProfitUnit { get; set; }
        public Unit StopLossUnit { get; set; }
        public Unit StopTradingUnit { get; set; }

        public bool UseQuoting { get; set; }

        public CandleSeries CandleSeries { get; private set; }

        private DateTime _strategyStartTime;

        public delegate void CandleProcessedHandler(Candle candle);
        public event CandleProcessedHandler CandleProcessed;

        public EMAEventModelStrategy(CandleSeries series, 
            ExponentialMovingAverage filterMA, ExponentialMovingAverage longMA, ExponentialMovingAverage shortMA,
            Unit takeProfitUnit, Unit stopLossUnit)
		{
            this.FilterMA = filterMA;
			this.LongMA = longMA;
			this.ShortMA = shortMA;

            this.CandleSeries = series;

            this.TakeProfitUnit = takeProfitUnit;
            this.StopLossUnit = stopLossUnit;

            this.StopTradingUnit = this.StopLossUnit * 3;

            this.UseQuoting = true;
		}

        protected override void OnStarted()
        {
            this.AddInfoLog("Core strategy {0} has been started.", this.Name);

            this._strategyStartTime = this.GetMarketTime();

            CandleSeries
                .WhenCandlesFinished()
                .Do(ProcessCandle)
                .Apply(this);

            this.WhenPnLLess(this.StopTradingUnit)
                .Do(StopTradingOnNotBeckhamsDay)
                .Once()
                .Apply(this);

            base.OnStarted();
        }

        protected override void OnStopped()
        {
            this.AddInfoLog("Core strategy {0} has been stopped.", this.Name);

            base.OnStopped();
        }

        protected void ProcessCandle(Candle candle)
        {
            if (candle == null || candle.State != CandleStates.Finished)
            {
                return;
            }

            LongMA.Process(candle);
            ShortMA.Process(candle);
            FilterMA.Process(candle);

            if (candle.CloseTime > this._strategyStartTime
                && LongMA.IsFormed
                && ShortMA.IsFormed
                && FilterMA.IsFormed)
            {
                this.AnalyseAndTrade();
            }

            this.OnCandleProcessed(candle);
        }

        private void AnalyseAndTrade()
        {
            // calculate MA X-ing cases 
            bool xUp = this.ShortMA.GetCurrentValue() > this.LongMA.GetCurrentValue() && this.ShortMA.GetValue(1) <= this.LongMA.GetValue(1);
            bool xDown = this.ShortMA.GetCurrentValue() < this.LongMA.GetCurrentValue() && this.ShortMA.GetValue(1) >= this.LongMA.GetValue(1);

            // calculate Filters
            bool upFilter = this.FilterMA.GetCurrentValue() > this.FilterMA.GetValue(1);
            bool downFilter = !upFilter;

            OrderDirections direction;
            decimal price;
            Order order = null;

            // calculate order direction
            if (xUp)
            {
                if (upFilter)
                {
                    direction = OrderDirections.Buy;
                    price = (this.UseQuoting) ? base.Security.GetMarketPrice(direction) : base.Security.GetCurrentPrice(direction).Value;
                    order = this.CreateOrder(direction, price, base.Volume);

                    this.AddInfoLog("Xing Up appeared (MarketTime: {0}), and filter allowed the deal.", base.Security.GetMarketTime());
                }
                else
                {
                    this.AddInfoLog("Xing Up appeared (MarketTime: {0}), but filter blocked the deal.", base.Security.GetMarketTime());
                }
            }

            if (xDown)
            {
                if (downFilter)
                {
                    direction = OrderDirections.Sell;
                    price = (this.UseQuoting) ? base.Security.GetMarketPrice(direction) : base.Security.GetCurrentPrice(direction).Value;
                    order = this.CreateOrder(direction, price, base.Volume);

                    this.AddInfoLog("Xing Down appeared (MarketTime: {0}), and filter allowed the deal.", base.Security.GetMarketTime());
                }
                else
                {
                    this.AddInfoLog("Xing Down appeared (MarketTime: {0}), but filter blocked the deal.", base.Security.GetMarketTime());
                }
            }

            // make order
            if (order != null)
            {
                if (this.PositionManager.Position == 0)
                {
                    if (this.UseQuoting)
                    {
                        LastTradeQuotingStrategy quotingStrategy = new LastTradeQuotingStrategy(order, new Unit());
                        base.ChildStrategies.Add(quotingStrategy);

                        quotingStrategy
                            .WhenNewMyTrades()
                            .Do(ProtectMyNewTrades)
                            .Apply(this);

                        //quotingStrategy
                        //    .WhenOrderRegistered()
                        //    .Do(qOrder =>
                        //    {
                        //        this
                        //            .WhenNewMyTrades()
                        //            .Do(ProtectMyNewTrades)
                        //            .Until(qOrder.IsMatched)
                        //            .Apply(this);
                        //    })
                        //    .Apply(this);
                    }
                    else
                    {
                        order
                            .WhenNewTrades()
                            .Do(ProtectMyNewTrades)
                            .Until(order.IsMatched)
                            .Apply(this);

                        RegisterOrder(order);
                    }
                }
                else
                {
                    this.AddInfoLog("PositionManager blocked the deal (MarketTime: {0}), we're already in position.", base.Security.GetMarketTime());
                }
            }
        }

        protected void OnCandleProcessed(Candle candle)
        {
            if (CandleProcessed != null)
            {
                CandleProcessed(candle);
            }
        }

        private void ProtectMyNewTrades(IEnumerable<MyTrade> trades)
        {
            foreach (MyTrade trade in trades)
            {
                var takeProfit = new TakeProfitStrategy(trade, this.TakeProfitUnit) {UseQuoting = this.UseQuoting};
                var stopLoss = new StopLossStrategy(trade, this.StopLossUnit) { UseQuoting = this.UseQuoting };

                ChildStrategies.Add(new TakeProfitStopLossStrategy(takeProfit, stopLoss));

                this.AddInfoLog("Hurrah, we have new trade (#{0}) and I've protected it.", trade.Trade.Id);
            };
        }

        private void StopTradingOnNotBeckhamsDay()
        {
            this.AddInfoLog("It's not Beckham's day. PnL reduction is detected. ({0}).", PnLManager.PnL);

            var position = PositionManager.Position;

            // Is it possible to stop the strategy right now?
            if (position == 0)
            {
                this.Stop();
                return;
            }

            this.AddInfoLog("I can't stop strategy right now. There {0} {1} open position{2}. I'll try again when position change.",
                            (position > 1) ? "are" : "is",
                            position,
                            (position > 1) ? "s" : "");

            // Prepare regular tries to stop the strategy
            this
                .WhenPositionChanged()
                .Do(() =>
                {
                    if (PositionManager.Position == 0)
                    {
                        this.Stop();
                    }
                })
                .Until(() => this.ProcessState == ProcessStates.Stopping || this.ProcessState == ProcessStates.Stopped)
                .Apply(this);
        }
    }
}
