using System;
using System.Collections.Generic;
using System.Linq;
using StockSharp.Algo;
using StockSharp.Algo.Logging;
using StockSharp.Algo.Candles;
using StockSharp.Algo.Indicators;
using StockSharp.Algo.Indicators.Trend;
using StockSharp.Algo.Strategies;
using StockSharp.BusinessEntities;
using SampleSMA.Logging;

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

        private bool useQuoting;
        public bool UseQuoting
        {
            get { return useQuoting; }
            set 
            {
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

            this.StopTradingUnit = this.StopLossUnit * 3;

            this.UseQuoting = true;
		}

        protected override void OnStarting()
        {
            this.AddLog(new ExtendedLogMessage(this, base.Trader.MarketTime, ErrorTypes.None, ExtendedLogMessage.ImportanceLevel.High,
                    "Core strategy {0} has been started.", this.Name));

            this._strategyStartTime = Trader.MarketTime;
            
            this
                .When(CandleManager.Tokens.ElementAt(0).CandlesFinished())
                .Do<IEnumerable<Candle>>(ProcessCandles);

            this
                .When(PnLManager.Less(this.StopTradingUnit))
                .Do(StopTradingOnNotBeckhamsDay)
                .Once();

            base.OnStarting();
        }

        protected override void OnStopped()
        {
            this.AddLog(new ExtendedLogMessage(this, base.Trader.MarketTime, ErrorTypes.None, ExtendedLogMessage.ImportanceLevel.High,
                    "Core strategy {0} has been stopped.", this.Name));

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
            if (candle == null)
            {
                return;
            }

            if (this.LastCandle == null
                && !this.FilterMA.IsFormed
                && !this.ShortMA.IsFormed
                && !this.LongMA.IsFormed)
            {
                // it's a very first candle and indicators are not formed yet
                this.RemoveStartFootprint(candle);
            }

            this.LastCandle = candle;

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

        public void RemoveStartFootprint(Candle candle)
        {
            this.FilterMA.RemoveStartFootprint((DecimalIndicatorValue)candle.OpenPrice);
            this.LongMA.RemoveStartFootprint((DecimalIndicatorValue)candle.OpenPrice);
            this.ShortMA.RemoveStartFootprint((DecimalIndicatorValue)candle.OpenPrice);
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
                        base.ChildStrategies.Add(marketQuotingStrategy);

                        this
                            .When(marketQuotingStrategy.StrategyNewOrder())
                            .Do((qOrder) =>
                            {
                                this
                                    .When(qOrder.NewTrades())
                                    .Do(ProtectMyNewTrades)
                                    .Periodical(() => qOrder.IsMatched());
                            });
                    }
                    else
                    {
                        RegisterOrder(order);

                        this
                            .When(order.NewTrades())
                            .Do(ProtectMyNewTrades)
                            .Periodical(() => order.IsMatched());
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
            foreach (MyTrade trade in trades)
            {
                var takeProfit = new TakeProfitStrategy(trade, this.TakeProfitUnit);
                var stopLoss = new StopLossStrategy(trade, StopLossUnit) { PriceOffset = 3 };

                ChildStrategies.Add(new TakeProfitStopLossStrategy(takeProfit, stopLoss));

                this.AddLog(new ExtendedLogMessage(this, base.Trader.MarketTime, ErrorTypes.None, ExtendedLogMessage.ImportanceLevel.High,
                    "Hurrah, we have new trade (#{0}) and I've protected it.", trade.Trade.Id));
            };
        }

        private void StopTradingOnNotBeckhamsDay()
        {
            this.AddLog(new ExtendedLogMessage(this, base.Trader.MarketTime, ErrorTypes.Warning, ExtendedLogMessage.ImportanceLevel.High,
                "It's not Beckham's day. PnL reduction is detected. ({0}).", PnLManager.PnL));

            var position = PositionManager.Position;

            // Is it possible to stop the strategy right now?
            if (position == 0)
            {
                this.Stop();
                return;
            }

            this.AddLog(new ExtendedLogMessage(this, base.Trader.MarketTime, ErrorTypes.Warning, ExtendedLogMessage.ImportanceLevel.High,
                            "I can't stop strategy right now. There {0} {1} open position{2}. I'll try again when position change.",
                            (position > 1) ? "are" : "is",
                            position,
                            (position > 1) ? "s" : ""));

            // Prepare regular tries to stop the strategy
            this
                .When(PositionManager.Changed())
                .Do(() =>
                {
                    if (PositionManager.Position == 0)
                    {
                        this.Stop();
                    }
                })
                .Periodical(() =>
                    {
                        return
                            this.ProcessState == ProcessStates.Stopping
                            || this.ProcessState == ProcessStates.Stopped;
                    });
        }
    }
}
