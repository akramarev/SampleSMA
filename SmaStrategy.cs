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
			// ���������� ������� ��������� ������������ ���� �����
			_isShortLessThenLong = this.ShortSma.LastValue < this.LongSma.LastValue;

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

			// �������� �������������� ������
			var candle = _candleManager.GetTimeFrameCandle(base.Security, base.TimeFrame, _nextTime - base.TimeFrame);

			// ���� ������ �� ���������� (�� ���� �� ����� ������ � ����-������), �� ���� ��������� ��������� ������.
			if (candle == null)
			{
				// ���� ������ ������ 10 ������ � ������� ��������� ������, � ��� ��� � �� ���������,
				// ������ ������ � ��������� 5-������� �� ����, � ��������� �� ��������� ������
				if ((base.Trader.MarketTime - _nextTime) > TimeSpan.FromSeconds(10))
					_nextTime = base.TimeFrame.GetCandleBounds(base.Trader.MarketTime).Max;

				return ProcessResults.Continue;
			}

			_nextTime += base.TimeFrame;

			// ��������� ����� ������
			this.LongSma.Process((DecimalIndicatorValue)candle.ClosePrice);
			this.ShortSma.Process((DecimalIndicatorValue)candle.ClosePrice);

			// ��������� ����� ��������� ������������ ���� �����
			var isShortLessThenLong = this.ShortSma.LastValue < this.LongSma.LastValue;

			// ���� ��������� �����������
			if (_isShortLessThenLong != isShortLessThenLong)
			{
				// ���� �������� ������ ��� �������, �� �������, �����, �������.
				var direction = isShortLessThenLong ? OrderDirections.Sell : OrderDirections.Buy;

				// ������� ������
				var order = this.CreateOrder(direction, base.Security.GetMarketPrice(direction), base.Volume);

				// ������������ ������ (������� �������� - �������������� �������)
				 base.RegisterOrder(order);

				// ������������ ������ (����� ������������)
                //var strategy = new MarketQuotingStrategy(order, new Unit(), new Unit());
                //base.ChildStrategies.Add(strategy);

				// ���������� ������� ��������� ������������ ���� �����
				_isShortLessThenLong = isShortLessThenLong;
			}

			return ProcessResults.Continue;
		}
	}
}