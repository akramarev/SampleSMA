namespace SampleSMA
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Windows;
    using System.Windows.Forms;
    using AmCharts.Windows.Stock;
    using Ecng.Collections;
    using Ecng.Common;
    using Ecng.ComponentModel;
    using Ecng.Serialization;
    using Ecng.Xaml;
    using StockSharp.Algo.Candles;
    using StockSharp.Algo.Indicators;
    using StockSharp.Algo.Indicators.Trend;
    using StockSharp.Algo.Logging;
    using StockSharp.Algo.Reporting;
    using StockSharp.Algo.Storages;
    using StockSharp.Algo.Strategies;
    using StockSharp.Algo.Testing;
    using StockSharp.BusinessEntities;
    using StockSharp.Quik;
    using StockSharp.Xaml;
    using MessageBox = System.Windows.MessageBox;
    using System.Configuration;
    using SampleSMA.Logging;

	public partial class MainWindow
	{
		private readonly TimeSpan _timeFrame = TimeSpan.FromMinutes(5);
		private ITrader _trader;
        private EMAEventModelStrategy _strategy;
		private bool _isDdeStarted;
		private CandleManager _candleManager;
		private Security _security;

        private int _filterMAPeriod = 90;
        private int _longMAPeriod = 13;
        private int _shortMAPeriod = 9;

        private LogManager _logManager = new LogManager();
        private SimpleLogSource _log = new SimpleLogSource() { Name="Main App" };

		public MainWindow()
		{
			InitializeComponent();

			// изменяет текущий формат, чтобы нецелое числа интерпритировалось как разделенное точкой.
			var cci = new CultureInfo(Thread.CurrentThread.CurrentCulture.Name) { NumberFormat = { NumberDecimalSeparator = "." } };
			Thread.CurrentThread.CurrentCulture = cci;

            SetDefaultHistoryRange();

            // попробовать сразу найти месторасположение Quik по запущенному процессу
            this.Path.Text = QuikTerminal.GetDefaultPath();

            _logManager.Sources.Add(_log);
            _logManager.Listeners.Add(new GuiLogListener(_monitor));
		}

        #region Main Event Handlers

        private void Start_Click(object sender, RoutedEventArgs e)
        {
            if (_strategy == null)
            {
                if (this.Portfolios.SelectedPortfolio == null)
                {
                    MessageBox.Show(this, "Портфель не выбран.");
                    return;
                }

                _strategy = new EMAEventModelStrategy(_candleManager,
                    new ExponentialMovingAverage { Length = this._filterMAPeriod },
                    new ExponentialMovingAverage { Length = this._longMAPeriod }, 
                    new ExponentialMovingAverage { Length = this._shortMAPeriod })
                {
                    Volume = this.Volume,
                    Security = _security,
                    Portfolio = this.Portfolios.SelectedPortfolio,
                    Trader = _trader,
                };

                // draw MAs based on latest strategy data
                _strategy.CandleProcessed += (candle) => DrawSmaLines(_strategy, candle.Time);

                _strategy.NewOrder += OnNewOrder;
                _strategy.PropertyChanged += OnStrategyPropertyChanged;

                _logManager.Sources.Add(_strategy);
                _logManager.Listeners.Add(new EmailUnitedLogListener());

                this.ClearChart();

                // load & draw history candles
                TimeFrameCandle[] historyCandles = GetHistoryCandlesFromFile(_security, _timeFrame);
                if (historyCandles != null)
                {
                    _strategy.RemoveStartFootprint(historyCandles[0]);

                    DrawCandles(historyCandles);
                    CalculateAndDrawMAs(historyCandles);
                }

                // регистрируем наш тайм-фрейм
                _candleManager.RegisterTimeFrameCandles(_security, _timeFrame);

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

        private void btnHistoryStart_Click(object sender, RoutedEventArgs e)
        {
            this.ClearChart();
            this.ClearGrids();

            // создаем тестовый инструмент, на котором будет производится тестирование
            var security = new Security
            {
                Id = this.txtSecurityId.Text, // по идентификатору инструмента будет искаться папка с историческими маркет данными
                Code = this.txtSecurityCode.Text,
                Name = this.txtSecurityCode.Text,
                MinStepSize = 1,
                MinStepPrice = 1,
                Exchange = Exchange.Rts,
            };

            var storage = new TradingStorage(new InMemoryStorage())
            {
                BasePath = this.txtHistoryPath.Text
            };

            var portfolio = new Portfolio { Name = "test account", BeginAmount = 30000m };

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

            EMAStrategyOptimizer optimizer = new EMAStrategyOptimizer(security, storage, portfolio, startTime, stopTime)
            {
                Volume = this.Volume,
                Log = _log
            };

            EmulationTrader trader = optimizer.GetOptTraderContext(this._filterMAPeriod, this._longMAPeriod, this._shortMAPeriod, new ManualResetEvent(false));
            EMAEventModelStrategy strategy = optimizer.Strategies[0];

            strategy.CandleProcessed += (candle) => DrawSmaLines(strategy, candle.Time);
            strategy.NewOrder += OnNewOrder;
            strategy.CandleManager.CandlesFinished += (token, candles) => DrawCandles(candles.Cast<TimeFrameCandle>());
            //strategy.PropertyChanged += OnStrategyPropertyChanged;
            trader.NewMyTrades += OnNewTrades;

            _logManager.Sources.Add(strategy);

            int lastUpdateHour = 0;

            trader.MarketTimeChanged += () =>
            {
                // в целях оптимизации обновляем ProgressBar и Stat только при начале нового часа
                if (trader.MarketTime.Hour != lastUpdateHour || trader.MarketTime >= stopTime)
                {
                    lastUpdateHour = trader.MarketTime.Hour;

                    UpdateStrategyStat(strategy);
                    this.GuiAsync(() => this.pbHistoryTestProgress.Value = (trader.MarketTime - startTime).TotalMinutes);
                }
            };

            Stopwatch sw = new Stopwatch();

            trader.StateChanged += () =>
            {
                if (trader.State == EmulationStates.Stopped)
                {
                    sw.Stop();

                    this.GuiAsync(() =>
                    {
                        btnHistoryStart.IsEnabled = true;

                        // Update strategy stat panel
                        this.UpdateStrategyStat(strategy);

                        _log.AddLog(new ExtendedLogMessage(_log, DateTime.Now, ErrorTypes.Warning, ExtendedLogMessage.ImportanceLevel.High,
                            String.Format("History testing done ({0}). Result: {1}, {2}, {3}. PnL: {4} ",
                                sw.Elapsed,
                                strategy.FilterMA.Length, 
                                strategy.LongMA.Length, 
                                strategy.ShortMA.Length, 
                                strategy.PnLManager.PnL)));
                    });
                }
                else if (trader.State == EmulationStates.Started)
                {
                    sw.Start();

                    this.GuiAsync(() =>
                    {
                        btnHistoryStart.IsEnabled = false;
                    });
                }
            };

            // устанавливаем в визуальный элемент ProgressBar максимальное количество итераций)
            this.pbHistoryTestProgress.Maximum = (stopTime - startTime).TotalMinutes;
            this.pbHistoryTestProgress.Value = 0;
            this.Report.IsEnabled = true;

            // соединяемся с трейдером и запускаем экспорт,
            // чтобы инициализировать переданными инструментами и портфелями необходимые свойства EmulationTrader
            trader.Connect();
            trader.StartExport();

            // запускаем эмуляцию, задавая период тестирования (startTime, stopTime).
            trader.Start(startTime, stopTime);
        }

        private void btnOptimize_Click(object sender, RoutedEventArgs e)
        {
            this.GuiAsync(() =>
            {
                btnOptimize.IsEnabled = false;
            });

            // создаем тестовый инструмент, на котором будет производится тестирование
            var security = new Security
            {
                Id = this.txtSecurityId.Text, // по идентификатору инструмента будет искаться папка с историческими маркет данными
                Code = this.txtSecurityCode.Text,
                Name = this.txtSecurityCode.Text,
                MinStepSize = 1,
                MinStepPrice = 1,
                Exchange = Exchange.Rts,
            };

            var storage = new TradingStorage(new InMemoryStorage())
            {
                BasePath = this.txtHistoryPath.Text
            };

            var portfolio = new Portfolio { Name = "test account", BeginAmount = 30000m };

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

            Stopwatch sw = new Stopwatch();

            EMAStrategyOptimizer optimizer = new EMAStrategyOptimizer(security, storage, portfolio, startTime, stopTime)
                {
                    Volume = this.Volume,
                    Log = _log
                };

            optimizer.StateChanged += () =>
            {
                if (optimizer.State == OptimizationState.Finished)
                {
                    sw.Stop();

                    _log.AddLog(new LogMessage(_log, DateTime.Now, ErrorTypes.Warning, String.Format("Opt done ({0}). The best startegy: {1}, {2}, {3} PnL: {4}",
                        sw.Elapsed,
                        optimizer.BestStrategy.FilterMA.Length,
                        optimizer.BestStrategy.LongMA.Length, optimizer.BestStrategy.ShortMA.Length,
                        optimizer.BestStrategy.PnLManager.PnL)));

                    this.GuiAsync(() =>
                    {
                        btnOptimize.IsEnabled = true;
                    });
                }
            };

            sw.Start();
            optimizer.Optimize();
        }

        #endregion

        #region Service Event Handlers

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
                    if (rbFightMode.IsChecked.Value)
                    {
                        _trader = new QuikTrader(this.Path.Text);
                    }
                    else
                    {
                        _trader = new RealTimeEmulationTrader<QuikTrader>(new QuikTrader(this.Path.Text));
                    }

                    // Connect trader with portoflio
                    this.Portfolios.Trader = _trader;

                    _trader.Connected += () =>
                    {
                        // Create main candle manager
                        _candleManager = new CandleManager(_trader);

                        _trader.NewSecurities += securities => this.GuiAsync(() =>
                        {
                            // находим нужную бумагу
                            var security = securities.FirstOrDefault(s => s.Code == this.txtSecurityCode.Text);

                            if (security != null)
                            {
                                _security = security;

                                this.Start.IsEnabled = true;
                            }
                        });

                        _trader.NewMyTrades += OnNewTrades;

                        _candleManager.CandlesStarted += (token, candles) => DrawCandles(candles.Cast<TimeFrameCandle>());
                        _candleManager.CandlesChanged += (token, candles) => DrawCandles(candles.Cast<TimeFrameCandle>());
                        _candleManager.CandlesFinished += (token, candles) => DrawCandles(candles.Cast<TimeFrameCandle>());

                        _trader.ConnectionError += ex =>
                        {
                            if (ex != null)
                            {
                                this._log.AddLog(new LogMessage(this._log, DateTime.Now, ErrorTypes.Error, ex.Message));
                            }
                        };

                        this.GuiAsync(() =>
                        {
                            rbFightMode.IsEnabled = false;
                            rbTrainingMode.IsEnabled = false;

                            this.ConnectBtn.IsEnabled = false;
                            this.Report.IsEnabled = true;
                        });
                    };
                }

                _trader.Connect();

                this.StartDde();
            }
            else
                _trader.Disconnect();
        }

        private void Report_Click(object sender, RoutedEventArgs e)
        {
            new ExcelStrategyReport(_strategy, "sma.xlsx").Generate();
        }

        #endregion

        #region Draw candles and indicators

        private void DrawCandles(IEnumerable<TimeFrameCandle> candles)
        {
            this.GuiAsync(() => _candleChart.AddCandles(candles));
        }
        private void DrawSmaLines(EMAEventModelStrategy strategy, DateTime time)
        {
            this.GuiSync(() =>
            {
                _candleChart.AddFilterMA(time, (double)strategy.FilterMA.LastValue);
                _candleChart.AddLongMA(time, (double)strategy.LongMA.LastValue);
                _candleChart.AddShortMA(time, (double)strategy.ShortMA.LastValue);
            });
        }

        private void CalculateAndDrawMAs(IEnumerable<TimeFrameCandle> candles)
        {
            foreach (var candle in candles)
            {
                _strategy.FilterMA.Process((DecimalIndicatorValue)candle.ClosePrice);
                _strategy.LongMA.Process((DecimalIndicatorValue)candle.ClosePrice);
                _strategy.ShortMA.Process((DecimalIndicatorValue)candle.ClosePrice);

                DrawSmaLines(_strategy, candle.Time);
            }
        }

        #endregion

        #region Interface Event Handlers

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

        #endregion

        #region Helpers Event Handlers

        private void OnStrategyPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var strategy = sender as EMAEventModelStrategy;
            if (strategy != null)
            {
                this.UpdateStrategyStat(strategy);
            }
        }

        private void UpdateStrategyStat(EMAEventModelStrategy strategy)
        {
            this.GuiAsync(() =>
            {
                this.Status.Content = strategy.ProcessState;
                this.PnL.Content = strategy.PnLManager.PnL;
                this.Slippage.Content = strategy.SlippageManager.Slippage;
                this.Position.Content = strategy.PositionManager.Position;
                this.Latency.Content = strategy.LatencyManager.Latency;
            });
        }

        private void OnNewOrder(Order order)
        {
            _orders.Orders.Add(order);
        }

        private void OnNewTrades(IEnumerable<MyTrade> trades)
        {
            _trades.Trades.AddRange(trades);

            var newTradeLogMessage = "I've {0} {1} future contracts at {2}";
            trades.ForEach(t => this._log.AddLog(
                new ExtendedLogMessage(this._log, DateTime.Now, ErrorTypes.Warning, ExtendedLogMessage.ImportanceLevel.High,
                    newTradeLogMessage,
                    (t.Trade.OrderDirection.Value == OrderDirections.Buy) ? "bought" : "sold",
                    t.Trade.Volume,
                    t.Trade.Price)));
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

        #endregion

        #region Helpers

        private void ClearChart()
        {
            _candleChart.Clear();
        }

        private void ClearGrids()
        {
            _orders.Orders.Clear();
            _trades.Trades.Clear();
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

        private void SetDefaultHistoryRange()
        {
            DateTime d = DateTime.Now.AddDays(-1);

            txtHistoryRangeBegin.Text = (d.Date + Exchange.Rts.WorkingTime.Times[0].Min).ToString("g");
            txtHistoryRangeEnd.Text = (d.Date + Exchange.Rts.WorkingTime.Times[2].Max).ToString("g");

            txtHistoryRangeBegin.Text = "05.01.2012 10:00";
            txtHistoryRangeEnd.Text = "05.01.2012 23:45";
        }

        private static TimeFrameCandle[] GetHistoryCandlesFromFile(Security security, TimeSpan timeFrame)
        {
            var historyFilePath = String.Format("{0}.txt", security.Code);

            if (File.Exists(historyFilePath))
            {
                var candles = File.ReadAllLines(historyFilePath).Select(line =>
                {
                    var parts = line.Split(',');
                    var time = DateTime.ParseExact(parts[0] + parts[1], "yyyyMMddHHmmss", CultureInfo.InvariantCulture);
                    return new TimeFrameCandle
                    {
                        OpenPrice = parts[2].To<decimal>(),
                        HighPrice = parts[3].To<decimal>(),
                        LowPrice = parts[4].To<decimal>(),
                        ClosePrice = parts[5].To<decimal>(),
                        TimeFrame = timeFrame,
                        Time = time,
                        TotalVolume = parts[6].To<int>()/100,
                        Security = security,
                    };
                }).ToArray();

                return candles;
            }

            return null;
        }

        #endregion

        #region Config settings wrappers

        public decimal Volume
        {
            get
            {
                decimal result;
                if (!Decimal.TryParse(ConfigurationManager.AppSettings["Volume"], out result))
                {
                    result = 1;
                }

                return result;
            }
        }

        #endregion
    }
}