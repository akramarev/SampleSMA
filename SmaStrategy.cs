namespace SampleSMA
{
	using System;

	using StockSharp.Algo;
	using StockSharp.Algo.Candles;
	using StockSharp.Algo.Indicators;
	using StockSharp.Algo.Indicators.Trend;
	using StockSharp.Algo.Strategies;
	using StockSharp.BusinessEntities;

	class SmaStrategy : TimeFrameStrategy
	{
		private readonly CandleManager _candleManager;
		private bool _isShortLessThenLong;

		private DateTime _nextTime;

		public SmaStrategy(CandleManager candleManager, SimpleMovingAverage longSma, SimpleMovingAverage shortSma, TimeSpan timeFrame)
			: base(timeFrame)
		{
			_candleManager = candleManager;

			this.LongSma = longSma;
			this.ShortSma = shortSma;
		}

		public SimpleMovingAverage LongSma { get; private set; }
		public SimpleMovingAverage ShortSma { get; private set; }

		protected override void OnStarting()
		{
			// запоминаем текущее положение относительно друг друга
			_isShortLessThenLong = this.ShortSma.LastValue < this.LongSma.LastValue;

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
			var candle = _candleManager.GetTimeFrameCandle(base.Security, base.TimeFrame, _nextTime - base.TimeFrame);

			// если свечки не существует (не было ни одной сделке в тайм-фрейме), то ждем окончания следующей свечки.
			if (candle == null)
			{
				// если прошло больше 10 секунд с момента окончания свечки, а она так и не появилась,
				// значит сделок в прошедшей 5-минутке не было, и переходим на следующую свечку
				if ((base.Trader.MarketTime - _nextTime) > TimeSpan.FromSeconds(10))
					_nextTime = base.TimeFrame.GetCandleBounds(base.Trader.MarketTime).Max;

				return ProcessResults.Continue;
			}

			_nextTime += base.TimeFrame;

			// добавляем новую свечку
			this.LongSma.Process((DecimalIndicatorValue)candle.ClosePrice);
			this.ShortSma.Process((DecimalIndicatorValue)candle.ClosePrice);

			// вычисляем новое положение относительно друг друга
			var isShortLessThenLong = this.ShortSma.LastValue < this.LongSma.LastValue;

			// если произошло пересечение
			if (_isShortLessThenLong != isShortLessThenLong)
			{
				// если короткая меньше чем длинная, то продажа, иначе, покупка.
				var direction = isShortLessThenLong ? OrderDirections.Sell : OrderDirections.Buy;

				// создаем заявку
				var order = this.CreateOrder(direction, base.Security.GetMarketPrice(direction), base.Volume);

				// регистрируем заявку (обычным способом - лимитированной заявкой)
				 base.RegisterOrder(order);

				// регистрируем заявку (через квотирование)
                //var strategy = new MarketQuotingStrategy(order, new Unit(), new Unit());
                //base.ChildStrategies.Add(strategy);

				// запоминаем текущее положение относительно друг друга
				_isShortLessThenLong = isShortLessThenLong;
			}

			return ProcessResults.Continue;
		}
	}
}