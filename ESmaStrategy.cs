namespace SampleSMA
{
	using System;

	using StockSharp.Algo;
	using StockSharp.Algo.Candles;
	using StockSharp.Algo.Indicators;
	using StockSharp.Algo.Indicators.Trend;
	using StockSharp.Algo.Strategies;
	using StockSharp.BusinessEntities;

	class ESmaStrategy : TimeFrameStrategy
	{
        public ExponentialMovingAverage FilterMA { get; private set; }
        public ExponentialMovingAverage LongMA { get; private set; }
        public ExponentialMovingAverage ShortMA { get; private set; }

        private decimal _prevFilterMAValue;
        private decimal _prevLongMAValue;
        private decimal _prevShortMAValue;

        public TimeFrameCandle LastCandle { get; private set; }

		private readonly CandleManager _candleManager;
		private DateTime _nextTime;

        public ESmaStrategy(CandleManager candleManager, ExponentialMovingAverage filterMA, ExponentialMovingAverage longMA, ExponentialMovingAverage shortMA, TimeSpan timeFrame)
			: base(timeFrame)
		{
			_candleManager = candleManager;

            this.FilterMA = filterMA;
			this.LongMA = longMA;
			this.ShortMA = shortMA;
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

			// получаем сформированную свечку (заполняем предыдущую свечку если она null)
            this.LastCandle = _candleManager.GetTimeFrameCandle(base.Security, base.TimeFrame, _nextTime - base.TimeFrame);
            
            if (this.LastCandle == null)
            {
                return ProcessResults.Continue;
            }

            // move internal time tracker to the next candle
			_nextTime += base.TimeFrame;

            // processing Filer, Short and Long MA (also take care about "prev-" variables)
            this.FilterMA.Process((DecimalIndicatorValue)this.LastCandle.ClosePrice);
            if (this._prevFilterMAValue == 0) { this._prevFilterMAValue = this.FilterMA.LastValue; }

            this.ShortMA.Process((DecimalIndicatorValue)this.LastCandle.ClosePrice);
            if (this._prevShortMAValue == 0) { this._prevShortMAValue = this.ShortMA.LastValue; }

            this.LongMA.Process((DecimalIndicatorValue)this.LastCandle.ClosePrice);
            if (this._prevLongMAValue == 0) { this._prevLongMAValue = this.LongMA.LastValue; }

			// calculate MA X-ing cases 
            bool xUp = this.ShortMA.LastValue > this.LongMA.LastValue && this._prevShortMAValue <= this._prevLongMAValue;
            bool xDown = this.ShortMA.LastValue < this.LongMA.LastValue && this._prevShortMAValue >= this._prevLongMAValue;

            // calculate Filters
            bool upFilter = this.FilterMA.LastValue > this._prevFilterMAValue;
            bool downFilter = !upFilter;

            OrderDirections direction;
            Order order = null;

            // calculate order direction
            if (xUp && upFilter)
            {
                direction = OrderDirections.Buy;
                order = this.CreateOrder(direction, base.Security.GetMarketPrice(direction), base.Volume);
            }

			if (xDown && downFilter)
			{
                direction = OrderDirections.Sell;
                order = this.CreateOrder(direction, base.Security.GetMarketPrice(direction), base.Volume);
            }

            // make order
            if (order != null)
            {
                MarketQuotingStrategy marketQuotingStrategy = new MarketQuotingStrategy(order, new Unit(), new Unit());
                base.ChildStrategies.Add(marketQuotingStrategy);
            }

            // update "prev-" variables
            this._prevFilterMAValue = this.FilterMA.LastValue;
            this._prevShortMAValue = this.ShortMA.LastValue;
            this._prevLongMAValue = this.LongMA.LastValue;

			return ProcessResults.Continue;
		}
	}
}