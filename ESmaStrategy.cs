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

        // all order made by strategy, but not by child strategies
        private List<Order> _primaryStrategyOrders = new List<Order>();

        public ESmaStrategy(CandleManager candleManager, ExponentialMovingAverage filterMA, ExponentialMovingAverage longMA, ExponentialMovingAverage shortMA, TimeSpan timeFrame)
			: base(timeFrame)
		{
			_candleManager = candleManager;

            this.FilterMA = filterMA;
			this.LongMA = longMA;
			this.ShortMA = shortMA;

            // subscribe to new trades (required for child strategies)
            base.NewMyTrades += ProtectMyNewTrades;
		}

		protected override void OnStarting()
		{
			// ��������� ����� ��������� ������� �����������
			_nextTime = base.TimeFrame.GetCandleBounds(base.Trader).Max;

			base.OnStarting();
		}

		protected override ProcessResults OnProcess()
		{
			// ���� ���� ��������� � �������� ���������
			if (base.ProcessState == ProcessStates.Stopping)
			{
				// �������� �������� ������
				base.CancelActiveOrders();

				// ��� ��� ��� �������� ������ �������������� ���� ��������, �� ���������� ProcessResults.Stop
				return ProcessResults.Stop;
			}

			// ������� ��������� �������� ��������� ��������� ������� ���, ��� ������, ��� ��������� ������� 5-�������.
			if (base.Trader.MarketTime < _nextTime)
			{
				// ���������� ProcessResults.Continue, ��� ��� ��� �������� ��� �� �������� ���� ������, � ������ ������� ���������� ������.
				return ProcessResults.Continue;
			}

			// �������� �������������� ������ (��������� ���������� ������ ���� ��� null)
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
            if (xUp)
            {
                if (upFilter)
                {
                    direction = OrderDirections.Buy;
                    order = this.CreateOrder(direction, base.Security.GetMarketPrice(direction), base.Volume);
                }
                else
                {
                    this.AddLog(new LogMessage(this, DateTime.Now, ErrorTypes.None, "Xing Up appeared (MarketTime: {0}, CandleTime: {1}), but filter blocked the deal.", base.Trader.MarketTime, this.LastCandle.Time));
                }
            }

			if (xDown)
			{
                if (downFilter)
                {
                    direction = OrderDirections.Sell;
                    order = this.CreateOrder(direction, base.Security.GetMarketPrice(direction), base.Volume);
                }
                else
                {
                    this.AddLog(new LogMessage(this, DateTime.Now, ErrorTypes.None, "Xing Down appeared (MarketTime: {0}, CandleTime: {1}), but filter blocked the deal.", base.Trader.MarketTime, this.LastCandle.Time));
                }
            }

            // make order
            if (order != null)
            {
                MarketQuotingStrategy marketQuotingStrategy = new MarketQuotingStrategy(order, new Unit(), new Unit());
                base.ChildStrategies.Add(marketQuotingStrategy);

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

            // ���� �� ������� �� ���� ������ ��� ������ TargetOrder
            if (trades.Count() == 0)
                return;

            var basket = new BasketStrategy(BasketStrategyFinishModes.All);

            foreach (MyTrade trade in trades)
            {
                var s = new BasketStrategy(BasketStrategyFinishModes.First);

                var takeProfit = new TakeProfitStrategy(trade, 500);
                var stopLoss = new StopLossStrategy(trade, 2.Percents());

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