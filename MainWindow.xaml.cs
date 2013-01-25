using System.Windows.Media;
using Ecng.Collections;
using Ecng.Common;
using Ecng.Xaml;
using SampleSMA.Logging;
using StockSharp.Algo;
using StockSharp.Algo.Candles;
using StockSharp.Algo.Candles.Compression;
using StockSharp.Algo.Indicators;
using StockSharp.Algo.Indicators.Trend;
using StockSharp.Algo.Storages;
using StockSharp.Algo.Testing;
using StockSharp.BusinessEntities;
using StockSharp.Logging;
using StockSharp.Quik;
using StockSharp.Xaml;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;

namespace SampleSMA
{
	public partial class MainWindow
	{
		private readonly TimeSpan _timeFrame = TimeSpan.FromMinutes(10);
		private ITrader _trader;
        private EMAEventModelStrategy _strategy;
		private CandleManager _candleManager;
		private Security _security;

        private int _filterMAPeriod = 84;
        private int _longMAPeriod = 12;
        private int _shortMAPeriod = 9;

        private LogManager _logManager = new LogManager();
        private SimpleLogSource _log = new SimpleLogSource() { Name = "Main App" };

        // Chart objects
        private readonly ChartArea _area;
        private ChartCandleElement _candlesElem;

	    private ExponentialMovingAverage _longMA;
        private ExponentialMovingAverage _shortMA;
        private ExponentialMovingAverage _filterMA;

        private ChartIndicatorElement _longMaElem;
        private ChartIndicatorElement _shortMaElem;
        private ChartIndicatorElement _filterMaElem;
	    private ChartTradeElement _tradeElem;

		public MainWindow()
		{
			InitializeComponent();

			// изменяет текущий формат, чтобы нецелое числа интерпритировалось как разделенное точкой.
			var cci = new CultureInfo(Thread.CurrentThread.CurrentCulture.Name) { NumberFormat = { NumberDecimalSeparator = "." } };
			Thread.CurrentThread.CurrentCulture = cci;

            SetDefaultHistoryRange();

            // попробовать сразу найти месторасположение Quik по запущенному процессу
            this.Path.Text = QuikTerminal.GetDefaultPath();

            _area = new ChartArea();
            _chart.Areas.Add(_area);

            _logManager.Sources.Add(_log);
            _logManager.Listeners.Add(new GuiLogListener(Monitor));
            _logManager.Listeners.Add(new EmailUnitedLogListener());
            _logManager.Listeners.Add(new JabberLogListener());
		}

        #region Main Event Handlers

        private void OnConnectClick(object sender, RoutedEventArgs e)
        {
            if (_trader != null)
            {
                if (_trader.IsConnected)
                {
                    _trader.Disconnect();
                }
                
                _trader.Dispose();
            }

            if (this.Path.Text.IsEmpty())
            {
                MessageBox.Show(this, "Путь к Quik не выбран.");
                return;
            }

            // Initialize Trader
            if (rbFightMode.IsChecked != null && rbFightMode.IsChecked.Value)
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
                _trader.NewSecurities += securities => this.GuiAsync(() =>
                {
                    var security = securities.FirstOrDefault(s => s.Code.Equals(this.txtSecurityCode.Text, StringComparison.InvariantCultureIgnoreCase));

                    if (security != null)
                    {
                        _security = security;

                        this.GuiAsync(() =>
                        {
                            this.Start.IsEnabled = true;
                        });
                    }
                    else
                    {
                        this._log.AddLog(new LogMessage(this._log, DateTime.Now, LogLevels.Error, "Mentioned security {0} can't be found in terminal.", this.txtSecurityCode.Text));
                    }
                });

                this.GuiAsync(() =>
                {
                    rbFightMode.IsEnabled = false;
                    rbTrainingMode.IsEnabled = false;
                    this.ConnectBtn.IsEnabled = false;
                });

                _trader.StartExport();
            };

            _trader.ConnectionError += ex =>
            {
                if (ex != null)
                {
                    this._log.AddLog(new LogMessage(this._log, DateTime.Now, LogLevels.Error, ex.Message));
                }
            };

            _trader.Connect();
        }

        private void OnStartClick(object sender, RoutedEventArgs e)
        {
            if (_strategy != null && _strategy.ProcessState != ProcessStates.Stopped)
            {
                _strategy.Stop();
                this.Start.Content = "Start";
                return;
            }

            if (this.Portfolios.SelectedPortfolio == null)
            {
                this.Portfolios.SelectedIndex = this.Portfolios.Items.Count - 1;
            }

            this.InitGrids();

            _candleManager = new CandleManager(_trader);
            _candleManager.Processing += (candleSeries, candle) => DrawCandleAndEma(candle);

            // Добавление в источник свечек TimeFrameCandleBuilder источник данных в виде файлов гидры
            var storageRegistry = new StorageRegistry();
            ((LocalMarketDataDrive)storageRegistry.DefaultDrive).Path = this.txtHistoryPath.Text;

            var cbs = new TradeStorageCandleBuilderSource { StorageRegistry = storageRegistry };
            _candleManager.Sources.OfType<TimeFrameCandleBuilder>().Single().Sources.Add(cbs);

            // регистрируем наш тайм-фрейм
            var series = new CandleSeries(typeof(TimeFrameCandle), _security, _timeFrame);

            _strategy = new EMAEventModelStrategy(series,
                new ExponentialMovingAverage { Length = this._filterMAPeriod },
                new ExponentialMovingAverage { Length = this._longMAPeriod }, 
                new ExponentialMovingAverage { Length = this._shortMAPeriod })
            {
                Volume = this.Volume,
                Security = _security,
                Portfolio = this.Portfolios.SelectedPortfolio,
                Trader = _trader,
            };

            this.InitChart(_strategy);
            _candleManager.Start(series, DateTime.Now.AddDays(-3), DateTime.MaxValue);

            _strategy.OrderRegistered += OnOrderRegistered;
            _strategy.PropertyChanged += OnStrategyPropertyChanged;
            _strategy.NewMyTrades += OnNewTrades;

            _logManager.Sources.Add(_strategy);

            // запускаем процесс получения стакана, необходимый для работы алгоритма котирования
            //_trader.RegisterMarketDepth(_strategy.Security);
            _strategy.Start();
            this.Start.Content = "Stop";
        }

        private void OnHistoryStartClick(object sender, RoutedEventArgs e)
        {
            this.InitGrids();

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

            var storageRegistry = new StorageRegistry();
            ((LocalMarketDataDrive)storageRegistry.DefaultDrive).Path = this.txtHistoryPath.Text;

            var portfolio = new Portfolio { Name = "test account", BeginValue = 30000m };

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

            EMAStrategyOptimizer optimizer = new EMAStrategyOptimizer(security, storageRegistry, portfolio, startTime, stopTime)
            {
                Volume = this.Volume,
                Log = _log
            };

            _trader = optimizer.GetOptTraderContext(this._timeFrame, this._filterMAPeriod, this._longMAPeriod, this._shortMAPeriod, new ManualResetEvent(false));
            _strategy = optimizer.Strategies[0];

            this.InitChart(_strategy);

            _strategy.Trader.NewOrders += orders => orders.ForEach(OnOrderRegistered);
            _strategy.Trader.NewMyTrades += OnNewTrades;

            _logManager.Sources.Add(_strategy);

            // устанавливаем в визуальный элемент ProgressBar максимальное количество итераций)
            this.pbHistoryTestProgress.Maximum = 10;
            this.pbHistoryTestProgress.Value = 0;

            var totalMinutes = (stopTime - startTime).TotalMinutes;
            var segment = Math.Floor(totalMinutes / 10);
            var nSegment = 1;
            var sSegment = segment;
            _trader.MarketTimeChanged += span =>
            {
                var currentMinute = (_trader.CurrentTime - startTime).TotalMinutes;
                if (currentMinute >= sSegment || _trader.CurrentTime >= stopTime)
                {
                    nSegment += 1;
                    sSegment = segment * nSegment;

                    this.UpdateStrategyStat(_strategy);
                    this.GuiAsync(() => this.pbHistoryTestProgress.Value = nSegment);
                }
            };

            Stopwatch sw = new Stopwatch();

            ((EmulationTrader)_trader).StateChanged += (states, emulationStates) =>
            {
                if (((EmulationTrader)_trader).State == EmulationStates.Stopped)
                {
                    sw.Stop();

                    this.UpdateStrategyStat(_strategy);
                    _strategy.CandleSeries.GetCandles<TimeFrameCandle>().ForEach(DrawCandleAndEma);
                    _strategy.Trader.MyTrades.ForEach(DrawTrade);

                    this.GuiAsync(() =>
                    {
                        this.pbHistoryTestProgress.Value = 0;
                        btnHistoryStart.IsEnabled = true;
                    });

                    _log.AddLog(new LogMessage(_log, DateTime.Now, LogLevels.Info,
                            String.Format("History testing done ({0}). Result: {1}, {2}, {3}. PnL: {4} ",
                                sw.Elapsed,
                                _strategy.FilterMA.Length,
                                _strategy.LongMA.Length,
                                _strategy.ShortMA.Length,
                                _strategy.PnLManager.PnL)));
                }
                else if (((EmulationTrader)_trader).State == EmulationStates.Started)
                {
                    sw.Start();

                    this.GuiAsync(() =>
                    {
                        btnHistoryStart.IsEnabled = false;
                    });
                }
            };

            // соединяемся с трейдером и запускаем экспорт,
            // чтобы инициализировать переданными инструментами и портфелями необходимые свойства EmulationTrader
            _trader.Connect();
            _trader.StartExport();

            // запускаем эмуляцию, задавая период тестирования (startTime, stopTime).
            ((EmulationTrader)_trader).Start(startTime, stopTime);
        }

        //private void btnOptimize_Click(object sender, RoutedEventArgs e)
        //{
        //    this.GuiAsync(() =>
        //    {
        //        btnOptimize.IsEnabled = false;
        //    });

        //    // создаем тестовый инструмент, на котором будет производится тестирование
        //    var security = new Security
        //    {
        //        Id = this.txtSecurityId.Text, // по идентификатору инструмента будет искаться папка с историческими маркет данными
        //        Code = this.txtSecurityCode.Text,
        //        Name = this.txtSecurityCode.Text,
        //        MinStepSize = 1,
        //        MinStepPrice = 1,
        //        Exchange = Exchange.Rts,
        //    };

        //    var storageRegistry = new StorageRegistry();
        //    ((LocalMarketDataDrive)storageRegistry.DefaultDrive).Path = this.txtHistoryPath.Text;

        //    var portfolio = new Portfolio { Name = "test account", BeginValue = 30000m };

        //    DateTime startTime;
        //    DateTime stopTime;

        //    if (!DateTime.TryParse(txtHistoryRangeEnd.Text, out stopTime))
        //    {
        //        stopTime = DateTime.Now;
        //        txtHistoryRangeEnd.Text = stopTime.ToString();
        //    }

        //    if (!DateTime.TryParse(txtHistoryRangeBegin.Text, out startTime))
        //    {
        //        startTime = stopTime.AddDays(-3);
        //        txtHistoryRangeBegin.Text = startTime.ToString();
        //    }

        //    Stopwatch sw = new Stopwatch();

        //    EMAStrategyOptimizer optimizer = new EMAStrategyOptimizer(security, storageRegistry, portfolio, startTime, stopTime)
        //        {
        //            Volume = this.Volume,
        //            Log = _log
        //        };

        //    optimizer.StateChanged += () =>
        //    {
        //        if (optimizer.State == OptimizationState.Finished)
        //        {
        //            sw.Stop();

        //            _log.AddLog(new LogMessage(_log, DateTime.Now, LogLevels.Info, String.Format("Opt done ({0}). The best startegy: {1}, {2}, {3} PnL: {4}",
        //                sw.Elapsed,
        //                optimizer.BestStrategy.FilterMA.Length,
        //                optimizer.BestStrategy.LongMA.Length, optimizer.BestStrategy.ShortMA.Length,
        //                optimizer.BestStrategy.PnLManager.PnL)));

        //            this.GuiAsync(() =>
        //            {
        //                btnOptimize.IsEnabled = true;
        //            });
        //        }
        //    };

        //    sw.Start();
        //    optimizer.Optimize();
        //}

        #endregion

        #region Draw candles and indicators

        private void InitChart(EMAEventModelStrategy strategy)
        {
            _longMA = new ExponentialMovingAverage { Length = strategy.LongMA.Length};
            _shortMA = new ExponentialMovingAverage { Length = strategy.ShortMA.Length };
            _filterMA = new ExponentialMovingAverage { Length = strategy.FilterMA.Length };

            _area.Elements.Clear();

            _candlesElem = new ChartCandleElement()
            {
                ColorPriceDown = Color.FromRgb(133, 133, 133),
                ColorPriceUp = Color.FromRgb(255, 255, 130)
            };
            _area.Elements.Add(_candlesElem);

            _longMaElem = new ChartIndicatorElement
            {
                Title = "LongMA",
                Indicator = _longMA,
            };
            _area.Elements.Add(_longMaElem);

            _shortMaElem = new ChartIndicatorElement
            {
                Title = "ShortMA",
                Indicator = _shortMA,
            };
            _area.Elements.Add(_shortMaElem);

            _filterMaElem = new ChartIndicatorElement
            {
                Title = "FilterMA",
                Indicator = _filterMA,
            };
            _area.Elements.Add(_filterMaElem);

            _tradeElem = new ChartTradeElement()
            {
                BuyColor = Color.FromRgb(255, 0, 0),
                SellColor = Color.FromRgb(0, 0, 255)
            };
            _area.Elements.Add(_tradeElem);
        }

        private void DrawCandleAndEma(Candle candle)
        {
            if (candle.State == CandleStates.Finished)
            {
                var longValue = new ChartIndicatorValue(_longMA, _longMA.Process(candle));
                var shortValue = new ChartIndicatorValue(_shortMA, _shortMA.Process(candle));
                var filterValue = new ChartIndicatorValue(_filterMA, _filterMA.Process(candle));

                this.GuiAsync(() => _chart.ProcessValues(candle.OpenTime, new Dictionary<IChartElement, object>
                {
                    {_candlesElem, candle},
                    {_longMaElem, longValue},
                    {_shortMaElem, shortValue},
                    {_filterMaElem, filterValue}
                }));
            }
        }

        private void DrawTrade(MyTrade trade)
        {
            this.GuiAsync(() => _chart.ProcessValues(trade.Trade.Time, new Dictionary<IChartElement, object>
                {
                    {_tradeElem, trade}
                }));
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
                this.Latency.Content = strategy.LatencyManager.LatencyRegistration;
            });

            this._log.AddLog(
                new LogMessage(this._log, DateTime.Now, LogLevels.Info,
                    "Stat Changed. State: {0}, PnL: {1} {2}, Slippage: {3}, Position: {4}, LatencyRegistration: {5}, LatencyCancellation: {6}",
                    strategy.ProcessState,
                    strategy.PnLManager.PnL,
                    (strategy.PnLManager.PnL < 0) ? ":(" : ":)",
                    strategy.SlippageManager.Slippage,
                    strategy.PositionManager.Position,
                    strategy.LatencyManager.LatencyRegistration,
                    strategy.LatencyManager.LatencyCancellation));
        }

        private void OnOrderRegistered(Order order)
        {
            OrdersGrid.Orders.Add(order);
        }

        private void OnNewTrades(IEnumerable<MyTrade> trades)
        {
            TradesGrid.Trades.AddRange(trades);

            var newTradeLogMessage = "I've {0} {1} futures contracts at {2}";
            trades.ForEach(t => this._log.AddLog(
                new LogMessage(this._log, DateTime.Now, LogLevels.Info,
                    newTradeLogMessage,
                    (t.Order.Direction == OrderDirections.Buy) ? "bought" : "sold",
                    t.Trade.Volume,
                    t.Trade.Price)));
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (_trader != null)
            {
                if (_trader.IsExportStarted)
                {
                    _trader.StopExport();
                }

                _trader.Dispose();
            }

            base.OnClosing(e);
        }

        #endregion

        #region Helpers

        private void InitGrids()
        {
            OrdersGrid.Orders.Clear();
            TradesGrid.Trades.Clear();
        }

        private void SetDefaultHistoryRange()
        {
            txtHistoryRangeBegin.Text = (DateTime.Now.AddDays(-5).Date + Exchange.Rts.WorkingTime.Times[0].Min).ToString("g");
            txtHistoryRangeEnd.Text = (DateTime.Now.Date + Exchange.Rts.WorkingTime.Times[2].Max).ToString("g");
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