namespace SampleSMA
{
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.Linq;
	using System.ComponentModel;
	using System.Globalization;
	using System.IO;
	using System.Threading;
	using System.Windows;
	using System.Windows.Forms;
	using MessageBox = System.Windows.MessageBox;

	using AmCharts.Windows.Stock;

	using Ecng.Collections;
	using Ecng.Common;
	using Ecng.Xaml;
	using Ecng.ComponentModel;

	using StockSharp.Algo.Logging;
	using StockSharp.Algo.Candles;
	using StockSharp.Algo.Reporting;
	using StockSharp.Algo.Strategies;
	using StockSharp.Algo.Indicators;
	using StockSharp.Algo.Indicators.Trend;
	using StockSharp.BusinessEntities;
	using StockSharp.Quik;
	using StockSharp.Xaml;
    using StockSharp.Algo.Testing;
    using StockSharp.Algo.Storages;
    using Ecng.Serialization;

	public partial class MainWindow
	{
		private readonly TimeSpan _timeFrame = TimeSpan.FromMinutes(5);
		private ITrader _trader;
		private ESmaStrategy _strategy;
		private bool _isDdeStarted;
		private DateTime _lastCandleTime;
		private bool _isTodaySmaDrawn;
		private CandleManager _candleManager;
		private readonly ICollection<CustomChartIndicator> _longSmaGraph;
		private readonly ICollection<CustomChartIndicator> _shortSmaGraph;
		private Security _security;

        private DateTime _lastUpdateDate;
        private DateTime _startEmulationTime;

		public MainWindow()
		{
			InitializeComponent();

			// изменяет текущий формат, чтобы нецелое числа интерпритировалось как разделенное точкой.
			var cci = new CultureInfo(Thread.CurrentThread.CurrentCulture.Name) { NumberFormat = { NumberDecimalSeparator = "." } };
			Thread.CurrentThread.CurrentCulture = cci;

			_longSmaGraph = _chart.CreateTrend("Длинная", GraphType.Line);
			_shortSmaGraph = _chart.CreateTrend("Короткая", GraphType.Line);

            txtHistoryRangeBegin.Text = DateTime.Now.AddDays(-3).ToShortDateString();
            txtHistoryRangeEnd.Text = DateTime.Now.ToShortDateString();
		}

		private void _orders_OrderSelected(object sender, EventArgs e)
		{
			this.CancelOrders.IsEnabled = _orders.SelectedOrders.Count() > 0;
		}

		protected override void OnClosing(CancelEventArgs e)
		{
			if (_trader != null)
			{
				if (_isDdeStarted)
					StopDde();

				_trader.Dispose();
			}

			base.OnClosing(e);
		}

		private void FindPath_Click(object sender, RoutedEventArgs e)
		{
			var dlg = new FolderBrowserDialog();

			if (!this.Path.Text.IsEmpty())
				dlg.SelectedPath = this.Path.Text;

			if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
			{
				this.Path.Text = dlg.SelectedPath;
			}
		}

		private void Connect_Click(object sender, RoutedEventArgs e)
		{
			if (_trader == null || !_trader.IsConnected)
			{
				if (_trader == null)
				{
					if (this.Path.Text.IsEmpty())
					{
						MessageBox.Show(this, "Путь к Quik не выбран.");
						return;
					}

					// создаем шлюз
					_trader = new RealTimeEmulationTrader<QuikTrader>(new QuikTrader(this.Path.Text));

					this.Portfolios.Trader = _trader;

					_trader.Connected += () =>
					{
						_candleManager = new CandleManager(_trader);

						_trader.NewSecurities += securities => this.GuiAsync(() =>
						{
							// находим нужную бумагу
                            var sec = securities.FirstOrDefault(s => s.Code == this.txtSecurityCode.Text);

							if (sec != null)
							{
								_security = sec;

								this.GuiAsync(() =>
								{
									this.Start.IsEnabled = true;
								});
							}
						});

						_trader.NewMyTrades += trades => this.GuiAsync(() =>
						{
							if (_strategy != null)
							{
								// найти те сделки, которые совершила стратегия скользящей средней
								trades = trades.Where(t => _strategy.Orders.Any(o => o == t.Order));

								_trades.Trades.AddRange(trades);
							}
						});

						_candleManager.NewCandles += (token, candles) =>
						{
							DrawCandles(candles);

							// если скользящие за сегодняшний день отрисованы, то рисуем в реальном времени текущие скользящие
							if (_isTodaySmaDrawn)
								DrawSma();
						};
						_candleManager.CandlesChanged += (token, candles) => DrawCandles(candles);
						//_trader.ProcessDataError += ex => this.Sync(() => MessageBox.Show(this, ex.ToString()));
						_trader.ConnectionError += ex =>
						{
							if (ex != null)
								this.GuiAsync(() => MessageBox.Show(this, ex.ToString()));
						};

						this.GuiAsync(() =>
						{
							this.ConnectBtn.IsEnabled = false;
							this.ExportDde.IsEnabled = true;
							this.Report.IsEnabled = true;
						});
					};
				}

				_trader.Connect();
			}
			else
				_trader.Disconnect();
		}

		private void OnNewOrder(Order order)
		{
			_orders.Orders.Add(order);
			this.GuiAsync(() => _chart.Orders.Add(order));
		}

		private void OnLog(LogMessage message)
		{
			// если стратегия вывела не просто сообщение, то вывести на экран.
			if (message.Type != ErrorTypes.None)
				this.GuiAsync(() => MessageBox.Show(this, message.Message));
		}

		private void DrawCandles(IEnumerable<Candle> candles)
		{
			this.GuiAsync(() => _chart.Candles.AddRange(candles));
		}

		private void DrawSma()
		{
			// нас не интересует текущая свечка, так как она еще не сформировалась
			// и из нее нельзя брать цену закрытия

			// вычисляем временные отрезки текущей свечки
			var bounds = _timeFrame.GetCandleBounds(_trader);

			// если появились новые полностью сформированные свечки
			if ((_lastCandleTime + _timeFrame) < bounds.Min)
			{
				// отступ с конца интервала, чтобы не захватить текущую свечку.
				var endOffset = TimeSpan.FromSeconds(1);

				bounds = new Range<DateTime>(_lastCandleTime + _timeFrame, bounds.Min - endOffset);

				// получаем эти свечки
				var candles = _candleManager.GetTimeFrameCandles(_strategy.Security, _timeFrame, bounds);

				if (candles.Count() > 0)
				{
					// получаем время самой последней свечки и запоминаем его как новое начало
					_lastCandleTime = candles.Max(c => c.Time);

					DrawSmaLines(bounds.Min);
				}
			}
		}

		private void DrawSmaLines(DateTime time)
		{
			this.GuiSync(() =>
			{
				_longSmaGraph.Add(new CustomChartIndicator
				{
					Time = time,
					Value = (double)_strategy.LongSma.LastValue
				});
				_shortSmaGraph.Add(new CustomChartIndicator
				{
					Time = time,
					Value = (double)_strategy.ShortSma.LastValue
				});
			});
		}

		private void OnStrategyPropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			this.GuiAsync(() =>
			{
				this.Status.Content = _strategy.ProcessState;
				this.PnL.Content = _strategy.PnLManager.PnL;
				this.Slippage.Content = _strategy.SlippageManager.Slippage;
				this.Position.Content = _strategy.PositionManager.Position;
				this.Latency.Content = _strategy.LatencyManager.Latency;
			});
		}

		private void StartDde()
		{
			_trader.StartExport();
			_isDdeStarted = true;
		}

		private void StopDde()
		{
			_trader.StopExport();
			_isDdeStarted = false;
		}

		private void ExportDde_Click(object sender, RoutedEventArgs e)
		{
			if (_isDdeStarted)
				StopDde();
			else
				StartDde();
		}

		private void CancelOrders_Click(object sender, RoutedEventArgs e)
		{
			_orders.SelectedOrders.ForEach(_trader.CancelOrder);
		}

		private void Start_Click(object sender, RoutedEventArgs e)
		{
			if (_strategy == null)
			{
				if (this.Portfolios.SelectedPortfolio == null)
				{
					MessageBox.Show(this, "Портфель не выбран.");
					return;
				}

				var candles = File.ReadAllLines("LKOH_history.txt").Select(line =>
				{
					var parts = line.Split(',');
					var time = DateTime.ParseExact(parts[0] + parts[1], "yyyyMMddHHmmss", CultureInfo.InvariantCulture);
					return new TimeFrameCandle
					{
						OpenPrice = parts[2].To<decimal>(),
						HighPrice = parts[3].To<decimal>(),
						LowPrice = parts[4].To<decimal>(),
						ClosePrice = parts[5].To<decimal>(),
						TimeFrame = _timeFrame,
						Time = time,
						TotalVolume = parts[6].To<int>(),
						Security = _security,
					};
				});

				DrawCandles(candles.Cast<Candle>());

				// создаем торговую стратегию, скользящие средние на 80 5-минуток и 10 5-минуток
                _strategy = new ESmaStrategy(_candleManager, new ExponentialMovingAverage { Length = 80 }, new ExponentialMovingAverage { Length = 10 }, _timeFrame)
				{
					Volume = 1,
					Security = _security,
					Portfolio = this.Portfolios.SelectedPortfolio,
					Trader = _trader,
				};
				_strategy.Log += OnLog;
				_strategy.NewOrder += OnNewOrder;
				_strategy.PropertyChanged += OnStrategyPropertyChanged;

				var index = 0;

				// начинаем вычислять скользящие средние
				foreach (var candle in candles)
				{
					_strategy.LongSma.Process((DecimalIndicatorValue)candle.ClosePrice);
					_strategy.ShortSma.Process((DecimalIndicatorValue)candle.ClosePrice);

					// если все скользящие сформировались, то начинаем их отрисовывать
					if (index >= _strategy.LongSma.Length)
						DrawSmaLines(candle.Time);

					index++;

					_lastCandleTime = candle.Time;
				}

				// регистрируем наш тайм-фрейм
				_candleManager.RegisterTimeFrameCandles(_security, _timeFrame);

				// вычисляем временные отрезки текущей свечки
				var bounds = _timeFrame.GetCandleBounds(_trader);

				candles = _candleManager.GetTimeFrameCandles(_strategy.Security, _timeFrame, new Range<DateTime>(_lastCandleTime + _timeFrame, bounds.Min));

				foreach (var candle in candles)
				{
					_strategy.LongSma.Process((DecimalIndicatorValue)candle.ClosePrice);
					_strategy.ShortSma.Process((DecimalIndicatorValue)candle.ClosePrice);

					DrawSmaLines(candle.Time);

					_lastCandleTime = candle.Time;
				}

				_isTodaySmaDrawn = true;

				this.Report.IsEnabled = true;
			}

			if (_strategy.ProcessState == ProcessStates.Stopped)
			{
				// запускаем процесс получения стакана, необходимый для работы алгоритма котирования
				_trader.RegisterQuotes(_strategy.Security);
				_strategy.Start();
				this.Start.Content = "Стоп";
			}
			else
			{
				_trader.UnRegisterQuotes(_strategy.Security);
				_strategy.Stop();
				this.Start.Content = "Старт";
			}
		}

		private void Report_Click(object sender, RoutedEventArgs e)
		{
			// сгерерировать отчет по прошедшему тестированию
			new ExcelStrategyReport(_strategy, "sma.xlsx").Generate();

			// открыть отчет
			Process.Start("sma.xlsx");
		}

        private void btnFindHistoryPath_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new FolderBrowserDialog();

            if (!this.txtHistoryPath.Text.IsEmpty())
                dlg.SelectedPath = this.txtHistoryPath.Text;

            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                this.txtHistoryPath.Text = dlg.SelectedPath;
            }
        }

        private void btnHistoryStart_Click(object sender, RoutedEventArgs e)
        {
            if (_trader != null && !(_trader is EmulationTrader))
                return;

            // если процесс был запущен, то его останавливаем
            if (_trader != null && ((EmulationTrader)_trader).State != EmulationStates.Stopped)
            {
                btnHistoryStart.IsEnabled = false;

                _strategy.Stop();
                ((EmulationTrader)_trader).Stop();
                //_logManager.Sources.Clear();

                return;
            }

            if (this.txtHistoryPath.Text.IsEmpty() || !Directory.Exists(this.txtHistoryPath.Text))
            {
                MessageBox.Show(this, "Неправильный путь.");
                return;
            }

            // создаем тестовый инструмент, на котором будет производится тестирование
            var security = new Security
            {
                Id = this.txtSecurityId.Text, // по идентификатору инструмента будет искаться папка с историческими маркет данными
                Code = this.txtSecurityCode.Text,
                Name = this.txtSecurityCode.Text,
                MinStepSize = 5,
                MinStepPrice = 2,
                Exchange = Exchange.Test,
            };

            // тестовый портфель
            var portfolio = new Portfolio { Name = "test account", BeginAmount = 30000m };

            // хранилище, через которое будет производиться доступ к тиковой и котировочной базе
            var storage = new TradingStorage(new InMemoryStorage())
            {
                BasePath = this.txtHistoryPath.Text
            };
            
            DateTime startTime;
            DateTime stopTime;

            if (!DateTime.TryParse(txtHistoryRangeEnd.Text, out stopTime))
            {
                stopTime = DateTime.Now;
                txtHistoryRangeEnd.Text = stopTime.ToString();
            }

            if (!DateTime.TryParse(txtHistoryRangeBegin.Text, out startTime))
            {
                startTime = stopTime.AddDays(-3);
                txtHistoryRangeBegin.Text = startTime.ToString();
            }
 
            EmulationTrader emuTrader = new EmulationTrader(
                new[] { security },
                new[] { portfolio })
            {
                MarketTimeChangedInterval = _timeFrame,
                Storage = storage,
                WorkingTime = Exchange.Rts.WorkingTime,

                // параметр влияет на занимаемую память.
                // в случае достаточно количества памяти на компьютере рекомендуется его увеличить
                DaysInMemory = 100,
            };

            _trader = emuTrader;

            emuTrader.DepthGenerators[security] = new TrendMarketDepthGenerator(security)
            {
                // стакан для инструмента в истории обновляется раз в 60 (1) секунд
                Interval = TimeSpan.FromSeconds(1),
            };

            //_trader.NewMyTrades += trades => this.GuiAsync(() =>
            //{
            //    if (_strategy != null)
            //    {
            //        _trades.Trades.AddRange(trades);
            //    }
            //});

            _candleManager = new CandleManager();

            var builder = new CandleBuilder(new SyncTraderTradeSource(_trader));
            _candleManager.Sources.Add(builder);

            // в целях оптимизации расходования памяти храним не более 100 последних свечек и 100000 последних сделок
            //((CandleContainer)_candleManager.Container).MaxCandleCount = 100;
            //((TradeContainer)builder.Container).MaxCandleCount = 100;
            //((TradeContainer)builder.Container).MaxTradeCount = 100;

            _candleManager.RegisterTimeFrameCandles(security, _timeFrame);

            // отрисовка свечей real time
            //_candleManager.NewCandles += (token, candles) =>
            //{
            //    DrawCandles(candles);
            //    DrawSma();
            //};
            //_candleManager.CandlesChanged += (token, candles) => DrawCandles(candles);

            // создаем торговую стратегию, скользящие средние на 12 5-минуток и 10 5-минуток
            _strategy = new ESmaStrategy(_candleManager, new ExponentialMovingAverage { Length = 50 }, new ExponentialMovingAverage { Length = 10 }, _timeFrame)
            {
                Volume = 1,
                Portfolio = portfolio,
                Security = security,
                Trader = _trader
            };

            // вывод сделок real time
            //_strategy.NewOrder += OnNewOrder;
            //_strategy.PropertyChanged += OnStrategyPropertyChanged;

            // и подписываемся на событие изменения времени, чтобы обновить ProgressBar
            _trader.MarketTimeChanged += () =>
            {
                // в целях оптимизации обновляем ProgressBar только при начале нового дня
                if (_trader.MarketTime.Date != _lastUpdateDate || _trader.MarketTime >= stopTime)
                {
                    _lastUpdateDate = _trader.MarketTime.Date;
                    this.GuiAsync(() => this.pbHistoryTestProgress.Value = (_trader.MarketTime - startTime).TotalMinutes);
                }
            };

            emuTrader.StateChanged += () =>
            {
                if (emuTrader.State == EmulationStates.Stopped)
                {
                    this.GuiAsync(() =>
                    {
                        btnHistoryStart.IsEnabled = true;

                        if (this.pbHistoryTestProgress.Value != this.pbHistoryTestProgress.Maximum)
                        {
                            MessageBox.Show("Отменено");
                        }

                        // рисуем график
                        var candles = _candleManager.GetTimeFrameCandles(security, _timeFrame).Cast<Candle>();
                        DrawCandles(candles);

                        // рисуем сколзяшки
                        foreach (var candle in candles)
                        {
                            _strategy.LongSma.Process((DecimalIndicatorValue)candle.ClosePrice);
                            _strategy.ShortSma.Process((DecimalIndicatorValue)candle.ClosePrice);

                            DrawSmaLines(candle.Time);
                        }

                        // заполняем order'ы
                        _orders.Orders.Clear();
                        _orders.Orders.AddRange(_strategy.Orders);
                        this.GuiAsync(() => _chart.Orders.AddRange(_strategy.Orders));

                        // заполняем трейды
                        _trades.Trades.Clear();
                        _trades.Trades.AddRange(_trader.MyTrades);

                        // обновляем стату по стратегии
                        OnStrategyPropertyChanged(null, null);

                        this.btnHistoryStart.Content = "Старт";
                    });
                }
                else if (emuTrader.State == EmulationStates.Started)
                {
                    this.btnHistoryStart.Content = "Стоп";

                    // запускаем стратегию когда эмулятор запустился
                    _strategy.Start();
                }
            };

            // устанавливаем в визуальный элемент ProgressBar максимальное количество итераций)
            this.pbHistoryTestProgress.Maximum = (stopTime - startTime).TotalMinutes;
            this.pbHistoryTestProgress.Value = 0;
            this.Report.IsEnabled = true;

            _startEmulationTime = DateTime.Now;

            // соединяемся с трейдером и запускаем экспорт,
            // чтобы инициализировать переданными инструментами и портфелями необходимые свойства EmulationTrader
            emuTrader.Connect();
            emuTrader.StartExport();

            // запускаем эмуляцию, задавая период тестирования (startTime, stopTime).
            emuTrader.Start(startTime, stopTime);
        }
	}
}