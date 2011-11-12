﻿namespace SampleSMA
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

	public partial class MainWindow
	{
		private readonly TimeSpan _timeFrame = TimeSpan.FromMinutes(5);
		private ITrader _trader;
		private EmaStrategy _strategy;
		private bool _isDdeStarted;
		private DateTime _lastCandleTime = DateTime.Now.Date.AddDays(-1);
		private bool _isTodaySmaDrawn;
		private CandleManager _candleManager;
        private readonly ICollection<CustomChartIndicator> _filterMAGraph;
		private readonly ICollection<CustomChartIndicator> _longMAGraph;
		private readonly ICollection<CustomChartIndicator> _shortMAGraph;
		private Security _security;

        private DateTime _lastUpdateDate;
        private DateTime _startEmulationTime;

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

			_longMAGraph = _chart.CreateTrend("LongMA", GraphType.Line);
			_shortMAGraph = _chart.CreateTrend("ShortMA", GraphType.Line);
            //_filterMAGraph = _chart.CreateTrend("FilterMA", GraphType.Line);

            SetDefaultHistoryRange();

            // попробовать сразу найти месторасположение Quik по запущенному процессу
            this.Path.Text = QuikTerminal.GetDefaultPath();

            _logManager.Sources.Add(_log);
            _logManager.Listeners.Add(_monitor);
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

                _strategy = new EmaStrategy(_candleManager,
                    new ExponentialMovingAverage { Length = this._filterMAPeriod },
                    new ExponentialMovingAverage { Length = this._longMAPeriod }, new ExponentialMovingAverage { Length = this._shortMAPeriod },
                    _timeFrame)
                {
                    Volume = 1,
                    Security = _security,
                    Portfolio = this.Portfolios.SelectedPortfolio,
                    Trader = _trader,
                };
                _strategy.NewOrder += OnNewOrder;
                _strategy.PropertyChanged += OnStrategyPropertyChanged;
                _logManager.Sources.Add(_strategy);

                IEnumerable<TimeFrameCandle> historyCandles = GetHistoryCandlesFromFile(_security);
                if (historyCandles != null)
                {
                    DrawCandles(historyCandles.Cast<Candle>());

                    foreach (var candle in historyCandles)
                    {
                        _strategy.FilterMA.Process((DecimalIndicatorValue)candle.ClosePrice);
                        _strategy.LongMA.Process((DecimalIndicatorValue)candle.ClosePrice);
                        _strategy.ShortMA.Process((DecimalIndicatorValue)candle.ClosePrice);

                        DrawSmaLines(_strategy, candle.Time);

                        _lastCandleTime = candle.Time;
                    }
                }

                // регистрируем наш тайм-фрейм
                _candleManager.RegisterTimeFrameCandles(_security, _timeFrame);

                // вычисляем временные отрезки текущей свечки
                var bounds = _timeFrame.GetCandleBounds(_trader);

                Thread.Sleep(1000);

                IEnumerable<TimeFrameCandle> candles = _candleManager.GetTimeFrameCandles(_strategy.Security, _timeFrame, new Range<DateTime>(_lastCandleTime + _timeFrame, bounds.Min));

                if (candles.Count() > 0)
                {
                    foreach (var candle in candles)
                    {
                        _strategy.FilterMA.Process((DecimalIndicatorValue)candle.ClosePrice);
                        _strategy.LongMA.Process((DecimalIndicatorValue)candle.ClosePrice);
                        _strategy.ShortMA.Process((DecimalIndicatorValue)candle.ClosePrice);

                        DrawSmaLines(_strategy, candle.Time);

                        _lastCandleTime = candle.Time;
                    }
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

        private void btnHistoryStart_Click(object sender, RoutedEventArgs e)
        {
            // создаем тестовый инструмент, на котором будет производится тестирование
            var security = new Security
            {
                Id = this.txtSecurityId.Text, // по идентификатору инструмента будет искаться папка с историческими маркет данными
                Code = this.txtSecurityCode.Text,
                Name = this.txtSecurityCode.Text,
                MinStepSize = 1,
                MinStepPrice = 5,
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
                Log = _log
            };

            EmulationTrader trader = optimizer.GetOptTraderContext(this._filterMAPeriod, this._longMAPeriod, this._shortMAPeriod, new ManualResetEvent(false));
            EmaStrategy strategy = optimizer.Strategies[0];

            _logManager.Sources.Add(strategy);

            DateTime lastUpdateDate = DateTime.MinValue;

            // и подписываемся на событие изменения времени, чтобы обновить ProgressBar
            trader.MarketTimeChanged += () =>
            {
                // в целях оптимизации обновляем ProgressBar только при начале нового дня
                if (trader.MarketTime.Date != lastUpdateDate || trader.MarketTime >= stopTime)
                {
                    lastUpdateDate = trader.MarketTime.Date;
                    this.GuiAsync(() => this.pbHistoryTestProgress.Value = (trader.MarketTime - startTime).TotalMinutes);
                }
            };

            trader.StateChanged += () =>
            {
                if (trader.State == EmulationStates.Stopped)
                {
                    this.GuiAsync(() =>
                    {
                        btnHistoryStart.IsEnabled = true;

                        // рисуем график
                        var candles = strategy.CandleManager.GetTimeFrameCandles(security, _timeFrame).Cast<Candle>();
                        DrawCandles(candles);

                        foreach (var candle in candles)
                        {
                            strategy.FilterMA.Process((DecimalIndicatorValue)candle.ClosePrice);
                            strategy.LongMA.Process((DecimalIndicatorValue)candle.ClosePrice);
                            strategy.ShortMA.Process((DecimalIndicatorValue)candle.ClosePrice);

                            DrawSmaLines(strategy, candle.Time);
                        }

                        // заполняем order'ы
                        _orders.Orders.Clear();
                        _orders.Orders.AddRange(strategy.Orders);
                        this.GuiAsync(() => _chart.Orders.AddRange(strategy.Orders));

                        // заполняем трейды
                        _trades.Trades.Clear();
                        _trades.Trades.AddRange(trader.MyTrades);

                        // обновляем стату по стратегии
                        UpdateStrategyStat(strategy);

                        _log.AddLog(new LogMessage(_log, DateTime.Now, ErrorTypes.Warning,
                            String.Format("Strategy History Result: {0}, {1}, {2}. PnL: {3} ", strategy.FilterMA.Length, strategy.LongMA.Length, strategy.ShortMA.Length, strategy.PnLManager.PnL)));
                    });
                }
                else if (trader.State == EmulationStates.Started)
                {
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
                MinStepPrice = 5,
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

                    // deactivate trading mode radio buttons
                    rbFightMode.IsEnabled = false;
                    rbTrainingMode.IsEnabled = false;

                    // connect trader with portoflio
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

                            if (_isTodaySmaDrawn)
                            {
                                DrawSma();
                            }
                        };

                        _candleManager.CandlesChanged += (token, candles) => DrawCandles(candles);

                        _trader.ConnectionError += ex =>
                        {
                            if (ex != null)
                            {
                                this._log.AddLog(new LogMessage(this._log, DateTime.Now, ErrorTypes.Error, ex.Message));
                            }
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
            {
                StopDde();
            }
            else
            {
                StartDde();
            }
        }

        private void CancelOrders_Click(object sender, RoutedEventArgs e)
        {
            _orders.SelectedOrders.ForEach(_trader.CancelOrder);
        }

        private void Report_Click(object sender, RoutedEventArgs e)
        {
            // сгерерировать отчет по прошедшему тестированию
            new ExcelStrategyReport(_strategy, "sma.xlsx").Generate();

            // открыть отчет
            //Process.Start("sma.xlsx");
        }

        #endregion

        #region Draw candles and indicators

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
            if ((_lastCandleTime + _timeFrame) <= bounds.Min)
            {
                // отступ с конца интервала, чтобы не захватить текущую свечку - 1 сек.
                var endOffset = TimeSpan.FromSeconds(1); //TimeSpan.Zero

                bounds = new Range<DateTime>(_lastCandleTime + _timeFrame, bounds.Min - endOffset);

                // получаем эти свечки
                var candles = _candleManager.GetTimeFrameCandles(_strategy.Security, _timeFrame, bounds);

                if (candles.Count() > 0)
                {
                    // получаем время самой последней свечки и запоминаем его как новое начало
                    _lastCandleTime = candles.Max(c => c.Time);

                    DrawSmaLines(_strategy, bounds.Min);
                }
            }
        }

        private void DrawSmaLines(EmaStrategy strategy, DateTime time)
        {
            this.GuiSync(() =>
            {
                //_filterMAGraph.Add(new CustomChartIndicator
                //{
                //    Time = time,
                //    Value = (double)strategy.FilterMA.LastValue
                //});
                _longMAGraph.Add(new CustomChartIndicator
                {
                    Time = time,
                    Value = (double)strategy.LongMA.LastValue
                });
                _shortMAGraph.Add(new CustomChartIndicator
                {
                    Time = time,
                    Value = (double)strategy.ShortMA.LastValue
                });
            });
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
            UpdateStrategyStat(_strategy);
        }

        private void UpdateStrategyStat(EmaStrategy strategy)
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
            this.GuiAsync(() => _chart.Orders.Add(order));
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

        #endregion

        #region Helpers

        private void SetDefaultHistoryRange()
        {
            DateTime d = DateTime.Now.AddDays(-1);

            txtHistoryRangeBegin.Text = (d.Date + Exchange.Rts.WorkingTime.Times[0].Min).ToString("g");
            txtHistoryRangeEnd.Text = (d.Date + Exchange.Rts.WorkingTime.Times[2].Max).ToString("g");

            txtHistoryRangeBegin.Text = "20.10.2011 10:00";
            txtHistoryRangeEnd.Text = "20.10.2011 23:45";
        }

        private IEnumerable<TimeFrameCandle> GetHistoryCandlesFromFile(Security security)
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
                        TimeFrame = _timeFrame,
                        Time = time,
                        TotalVolume = parts[6].To<int>()/100,
                        Security = security,
                    };
                });

                return candles;
            }

            return null;
        }

        #endregion
	}
}