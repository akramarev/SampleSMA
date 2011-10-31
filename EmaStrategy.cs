namespace SampleSMA
{
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

    public class EmaStrategy : TimeFrameStrategy
	{
        public ExponentialMovingAverage FilterMA { get; private set; }
        public ExponentialMovingAverage LongMA { get; private set; }
        public ExponentialMovingAverage ShortMA { get; private set; }

        public Unit TakeProfitUnit { get; set; }
        public Unit StopLossUnit { get; set; }

        private decimal _prevFilterMAValue;
        private decimal _prevLongMAValue;
        private decimal _prevShortMAValue;

        public TimeFrameCandle LastCandle { get; private set; }

		private readonly CandleManager _candleManager;
		private DateTime _nextTime;

        // all order made by strategy, but not by child strategies
        private List<Order> _primaryStrategyOrders = new List<Order>();

        public EmaStrategy(CandleManager candleManager, ExponentialMovingAverage filterMA, ExponentialMovingAverage longMA, ExponentialMovingAverage shortMA, TimeSpan timeFrame)
			: base(timeFrame)
		{
            this.Name = "EmaStrategy";

			this._candleManager = candleManager;

            this.FilterMA = filterMA;
			this.LongMA = longMA;
			this.ShortMA = shortMA;

            this.TakeProfitUnit = 200;
            this.StopLossUnit = 300;

            // subscribe to new trades (required for child strategies)
            base.NewMyTrades += ProtectMyNewTrades;
		}

		protected override void OnStarting()
		{
			// вычисляем время окончания текущей пятиминутки
			_nextTime = base.TimeFrame.GetCandleBounds(base.Trader).Max;

			base.OnStarting();
		}

		protected override ProcessResults OnProcess()
		{
			// если наша стратегия в процессе остановки
			if (base.ProcessState == ProcessStates.Stopping)
			{
				// отменяем активные заявки
				base.CancelActiveOrders();

				// так как все активные заявки гарантированно были отменены, то возвращаем ProcessResults.Stop
				return ProcessResults.Stop;
			}

			// событие обработки торговой стратегии вызвалось впервый раз, что раньше, чем окончания текущей 5-минутки.
			if (base.Trader.MarketTime < _nextTime)
			{
				// возвращаем ProcessResults.Continue, так как наш алгоритм еще не закончил свою работу, а просто ожидает следующего вызова.
				return ProcessResults.Continue;
			}

			// получаем сформированную свечку
            this.LastCandle = _candleManager.GetTimeFrameCandle(base.Security, base.TimeFrame, _nextTime - base.TimeFrame);

            // move internal time tracker to the next candle
            _nextTime += base.TimeFrame;

            if (this.LastCandle == null)
            {
                return ProcessResults.Continue;
            }

            // processing Filer, Short and Long MA (also take care about "prev-" variables)
            this.FilterMA.Process((DecimalIndicatorValue)this.LastCandle.ClosePrice);
            if (this._prevFilterMAValue == 0) 
            { 
                this._prevFilterMAValue = this.FilterMA.LastValue;
                this.FilterMA.RemoveStartFootprint((DecimalIndicatorValue)this.LastCandle.ClosePrice);
            }

            this.ShortMA.Process((DecimalIndicatorValue)this.LastCandle.ClosePrice);
            if (this._prevShortMAValue == 0) 
            { 
                this._prevShortMAValue = this.ShortMA.LastValue;
                this.ShortMA.RemoveStartFootprint((DecimalIndicatorValue)this.LastCandle.ClosePrice);
            }

            this.LongMA.Process((DecimalIndicatorValue)this.LastCandle.ClosePrice);
            if (this._prevLongMAValue == 0)
            {
                this._prevLongMAValue = this.LongMA.LastValue;
                this.LongMA.RemoveStartFootprint((DecimalIndicatorValue)this.LastCandle.ClosePrice);
            }

            //if (this.LastCandle.Time > DateTime.Parse("24.10.2011 20:29:00") && this.LastCandle.Time < DateTime.Parse("24.10.2011 20:36:00"))
            //{
            //    this.AddLog(new LogMessage(this, base.Trader.MarketTime, ErrorTypes.None, "Test (CandleTime: {0}). SMA: {1}, LMA: {2}",
            //        this.LastCandle.Time,
            //        this.ShortMA.LastValue, this.LongMA.LastValue));
            //}

			// calculate MA X-ing cases 
            bool xUp = this.ShortMA.LastValue > this.LongMA.LastValue && this._prevShortMAValue <= this._prevLongMAValue;
            bool xDown = this.ShortMA.LastValue < this.LongMA.LastValue && this._prevShortMAValue >= this._prevLongMAValue;

            // calculate Filters
            bool upFilter = this.FilterMA.LastValue > this._prevFilterMAValue;
            bool downFilter = !upFilter;

            OrderDirections direction;
            Order order = null;

            // calculate order direction
            if (xUp)
            {
                if (upFilter)
                {
                    direction = OrderDirections.Buy;
                    order = this.CreateOrder(direction, base.Security.GetMarketPrice(direction), base.Volume);

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
                    order = this.CreateOrder(direction, base.Security.GetMarketPrice(direction), base.Volume);

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
                //MarketQuotingStrategy marketQuotingStrategy = new MarketQuotingStrategy(order, new Unit(), new Unit());
                //base.ChildStrategies.Add(marketQuotingStrategy);
                base.RegisterOrder(order);

                _primaryStrategyOrders.Add(order);
            }

            // update "prev-" variables
            this._prevFilterMAValue = this.FilterMA.LastValue;
            this._prevShortMAValue = this.ShortMA.LastValue;
            this._prevLongMAValue = this.LongMA.LastValue;

			return ProcessResults.Continue;
		}

        private void ProtectMyNewTrades(IEnumerable<MyTrade> trades)
        {
            // take care about trades that were made by main strategy
            trades = trades.Where(t => _primaryStrategyOrders.Any(o => t.Order == o));

            // если не найдена ни одна сделка для заявки TargetOrder
            if (trades.Count() == 0)
                return;

            var basket = new BasketStrategy(BasketStrategyFinishModes.All);

            foreach (MyTrade trade in trades)
            {
                var s = new BasketStrategy(BasketStrategyFinishModes.First) { Name = "ProtectStrategy" };

                var takeProfit = new TakeProfitStrategy(trade, this.TakeProfitUnit) { Name = "TakeProfitStrategy" };
                var stopLoss = new StopLossStrategy(trade, this.StopLossUnit) { Name = "StopLossStrategy" };

                s.ChildStrategies.Add(takeProfit);
                s.ChildStrategies.Add(stopLoss);

                basket.ChildStrategies.Add(s);
            }

            base.ChildStrategies.Add(basket);
        }

        protected override void DisposeManaged()
        {
            base.NewMyTrades -= ProtectMyNewTrades;
            base.DisposeManaged();
        }
	}
}