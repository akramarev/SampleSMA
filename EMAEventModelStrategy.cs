﻿using System;
using System.Collections.Generic;
using System.Linq;
using StockSharp.Algo;
using StockSharp.Algo.Logging;
using StockSharp.Algo.Candles;
using StockSharp.Algo.Indicators;
using StockSharp.Algo.Indicators.Trend;
using StockSharp.Algo.Strategies;
using StockSharp.BusinessEntities;

namespace SampleSMA
{
    public class EMAEventModelStrategy : Strategy
    {
        public ExponentialMovingAverage FilterMA { get; private set; }
        public ExponentialMovingAverage LongMA { get; private set; }
        public ExponentialMovingAverage ShortMA { get; private set; }

        public Unit TakeProfitUnit { get; set; }
        public Unit StopLossUnit { get; set; }

        private bool useQuoting;
        public bool UseQuoting
        {
            get { return useQuoting; }
            set 
            {
                if (!value)
                {
                    throw new NotImplementedException("Strategy doesn't work without quoting for now.");
                }

                useQuoting = value;
            }
        }

        public CandleManager CandleManager { get; private set; }
        public Candle LastCandle { get; private set; }

        private decimal _prevFilterMAValue;
        private decimal _prevLongMAValue;
        private decimal _prevShortMAValue;

        private DateTime _strategyStartTime;

        public delegate void CandleProcessedHandler(Candle candle);
        public event CandleProcessedHandler CandleProcessed;

        public EMAEventModelStrategy(CandleManager candleManager, ExponentialMovingAverage filterMA, ExponentialMovingAverage longMA, ExponentialMovingAverage shortMA)
		{
            this.Name = "EmaEventModelStrategy";

            this.FilterMA = filterMA;
			this.LongMA = longMA;
			this.ShortMA = shortMA;

            this.CandleManager = candleManager;

            this.TakeProfitUnit = 50;
            this.StopLossUnit = 35;

            this.UseQuoting = true;
		}

        protected override void OnStarting()
        {
            this._strategyStartTime = Trader.MarketTime;

            if (!UseQuoting)
            {
                this.NewMyTrades += ProtectMyNewTrades;
            }
            
            this.
                When(this.CandleManager.Tokens.ElementAt(0).CandlesFinished())
                .Do<IEnumerable<Candle>>(ProcessCandles);

            base.OnStarting();
        }

        protected override void OnStopped()
        {
            if (!UseQuoting)
            {
                this.NewMyTrades -= ProtectMyNewTrades;
            }

            base.OnStopped();
        }

        protected IEnumerable<Candle> ProcessCandles(IEnumerable<Candle> candles)
        {
            foreach (Candle candle in candles)
            {
                ProcessCandle(candle);
            }

            return candles;
        }

        protected void ProcessCandle(Candle candle)
        {
            this.LastCandle = candle;

            if (this.LastCandle == null)
            {
                return;
            }

            // processing Filer, Short and Long MA (also take care about "prev-" variables)
            this.FilterMA.Process((DecimalIndicatorValue)this.LastCandle.ClosePrice);
            if (this._prevFilterMAValue == 0)
            {
                this._prevFilterMAValue = this.FilterMA.LastValue;
            }

            this.LongMA.Process((DecimalIndicatorValue)this.LastCandle.ClosePrice);
            if (this._prevLongMAValue == 0)
            {
                this._prevLongMAValue = this.LongMA.LastValue;
            }

            this.ShortMA.Process((DecimalIndicatorValue)this.LastCandle.ClosePrice);
            if (this._prevShortMAValue == 0)
            {
                this._prevShortMAValue = this.ShortMA.LastValue;
            }

            if (candle.Time > this._strategyStartTime)
            {
                this.AnalyseAndTrade();
            }

            // update "prev-" variables
            this._prevFilterMAValue = this.FilterMA.LastValue;
            this._prevShortMAValue = this.ShortMA.LastValue;
            this._prevLongMAValue = this.LongMA.LastValue;

            this.OnCandleProcessed(candle);
        }

        private void AnalyseAndTrade()
        {
            // calculate MA X-ing cases 
            bool xUp = this.ShortMA.LastValue > this.LongMA.LastValue && this._prevShortMAValue <= this._prevLongMAValue;
            bool xDown = this.ShortMA.LastValue < this.LongMA.LastValue && this._prevShortMAValue >= this._prevLongMAValue;

            // calculate Filters
            bool upFilter = this.FilterMA.LastValue > this._prevFilterMAValue;
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
                    price = (UseQuoting) ? base.Security.GetMarketPrice(direction) : this.LastCandle.ClosePrice;
                    order = this.CreateOrder(direction, price, base.Volume);

                    this.AddLog(new LogMessage(this, base.Trader.MarketTime, ErrorTypes.None, "Xing Up appeared (CandleTime: {0}), and filter allowed the deal.", this.LastCandle.Time));
                }
                else
                {
                    this.AddLog(new LogMessage(this, base.Trader.MarketTime, ErrorTypes.None, "Xing Up appeared (CandleTime: {0}), but filter blocked the deal.", this.LastCandle.Time));
                }
            }

            if (xDown)
            {
                if (downFilter)
                {
                    direction = OrderDirections.Sell;
                    price = (UseQuoting) ? base.Security.GetMarketPrice(direction) : this.LastCandle.ClosePrice;
                    order = this.CreateOrder(direction, price, base.Volume);

                    this.AddLog(new LogMessage(this, base.Trader.MarketTime, ErrorTypes.None, "Xing Down appeared (CandleTime: {0}), and filter allowed the deal.", this.LastCandle.Time));
                }
                else
                {
                    this.AddLog(new LogMessage(this, base.Trader.MarketTime, ErrorTypes.None, "Xing Down appeared (CandleTime: {0}), but filter blocked the deal.", this.LastCandle.Time));
                }
            }

            // make order
            if (order != null)
            {
                if (this.PositionManager.Position == 0)
                {
                    if (UseQuoting)
                    {
                        MarketQuotingStrategy marketQuotingStrategy = new MarketQuotingStrategy(order, new Unit(), new Unit());
                        marketQuotingStrategy.NewMyTrades += ProtectMyNewTrades;
                        base.ChildStrategies.Add(marketQuotingStrategy);
                    }
                    else
                    {
                        base.RegisterOrder(order);
                    }
                }
                else
                {
                    this.AddLog(new LogMessage(this, base.Trader.MarketTime, ErrorTypes.None, "PositionManager blocked the deal (CandleTime: {0}), we're already in position.", this.LastCandle.Time));
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
            var basket = new BasketStrategy(BasketStrategyFinishModes.All);

            foreach (MyTrade trade in trades)
            {
                var s = new BasketStrategy(BasketStrategyFinishModes.First) { Name = "ProtectStrategy" };

                var takeProfit = new TakeProfitStrategy(trade, this.TakeProfitUnit)
                {
                    Name = "TakeProfitStrategy",
                    BestPriceOffset = 15,
                    PriceOffset = 3,
                    UseQuoting = this.UseQuoting
                };

                var stopLoss = new StopLossStrategy(trade, this.StopLossUnit)
                {
                    Name = "StopLossStrategy",
                    PriceOffset = 3
                };

                s.ChildStrategies.Add(takeProfit);
                s.ChildStrategies.Add(stopLoss);

                basket.ChildStrategies.Add(s);
            }

            base.ChildStrategies.Add(basket);
        }
    }
}
