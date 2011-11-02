namespace IndicatorsXaml
{
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Drawing;
    using System.Linq;
    using System.Text;
    using System.Windows.Forms.DataVisualization.Charting;

    using Ecng.Common;

    using StockSharp.Algo.Candles;
    using StockSharp.Algo.Indicators.Trend;

    public partial class CandleChart
    {
        private readonly ChartArea _chartAreaCandles;
        //private readonly ChartArea _chartAreaVolume;

        private readonly Series _seriesCandles;
        //private readonly Series _seriesVolume;

        private List<TimeFrameCandle> _candlesList;

        /// <summary>
        /// Графической компонент отображения индикаторов на Candle графике
        /// </summary>
        public CandleChart()
        {
            InitializeComponent();

            var chart = Chart;

            chart.BackColor = System.Drawing.Color.FromArgb(211, 223, 240);
            //chart.BackSecondaryColor = Color.White;
            //chart.BackGradientStyle = GradientStyle.TopBottom;
            //chart.BorderlineColor = Color.FromArgb(26, 59, 105);
            chart.BorderlineDashStyle = ChartDashStyle.Solid;
            //chart.BorderlineWidth = 2;
            //chart.BorderSkin.SkinStyle = BorderSkinStyle.Emboss;

            _chartAreaCandles = new ChartArea("ChartAreaCandles")
            {
                AxisX =
                    {
                        IsLabelAutoFit = false,
                        LabelStyle =
                            {
                                Font = new System.Drawing.Font("Trebuchet MS", 8.25F, System.Drawing.FontStyle.Bold),
                                IsEndLabelVisible = false,
                                Format = "HH:mm"
                            },
                        LineColor = System.Drawing.Color.FromArgb(64, 64, 64, 64),
                        MajorGrid =
                            {
                                LineColor = System.Drawing.Color.FromArgb(64, 64, 64, 64)
                            }
                    },
                AxisY =
                    {
                        IsLabelAutoFit = false,
                        LabelStyle =
                            {
                                Font = new System.Drawing.Font("Trebuchet MS", 8.25F, System.Drawing.FontStyle.Bold)
                            },
                        LineColor = System.Drawing.Color.FromArgb(64, 64, 64, 64),
                        MajorGrid =
                            {
                                LineColor = System.Drawing.Color.FromArgb(64, 64, 64, 64)
                            },
                        IsStartedFromZero = false
                    },
                BackColor = System.Drawing.Color.White
            };

            chart.ChartAreas.Add(_chartAreaCandles);

            chart.Palette = ChartColorPalette.SemiTransparent;

            _seriesCandles = new Series("SeriesCandles")
            {
                ChartArea = "ChartAreaCandles",
                ChartType = SeriesChartType.Candlestick,
                XValueType = ChartValueType.DateTime,
                IsXValueIndexed = true
            };

            chart.Series.Add(_seriesCandles);
        }

        public void AddCandle(TimeFrameCandle candle)
        {
            Trace.WriteLine("Adding candle: " + candle.Time);
            Trace.Flush();

            var candleSb = new StringBuilder();
            candleSb.Append(candle.LowPrice.ToString().Replace(',', '.')).Append(',');
            candleSb.Append(candle.HighPrice.ToString().Replace(',', '.')).Append(',');
            candleSb.Append(candle.OpenPrice.ToString().Replace(',', '.')).Append(',');
            candleSb.Append(candle.ClosePrice.ToString().Replace(',', '.'));
            var pointCandle = new DataPoint(candle.Time.ToOADate(), candleSb.ToString());

            pointCandle["PriceUpColor"] = "Green";
            pointCandle["PriceDownColor"] = "Red";
            pointCandle.BorderColor = Color.DarkSlateGray;

            _seriesCandles.Points.Add(pointCandle);

            // get max/min y values in bounded set
            var maxY = _seriesCandles.Points.Max(x => x.YValues[1]);
            var minY = _seriesCandles.Points.Min(x => x.YValues[0]);

            // pad max/min y values
            _chartAreaCandles.AxisY.Maximum = maxY + ((maxY - minY) * 0.05);
            _chartAreaCandles.AxisY.Minimum = minY - ((maxY - minY) * 0.05);
        }

        public void AddCandles(IEnumerable<TimeFrameCandle> candlesList)
        {
            _candlesList = candlesList.ToList();

            foreach (var candle in _candlesList)
            {
                AddCandle(candle);
            }
        }

        public void AddNewChartArea(string name, Chart chart)
        {
            var chartArea = new ChartArea
            {
                Name = name,
                AlignWithChartArea = "ChartAreaCandles",
                Area3DStyle =
                {
                    IsClustered = true,
                    Perspective = 10,
                    IsRightAngleAxes = false,
                    WallWidth = 0,
                    Inclination = 10,
                    Rotation = 10
                },
                AxisX =
                {
                    IntervalType = DateTimeIntervalType.Minutes,
                    IsLabelAutoFit = false,
                    LabelStyle =
                    {
                        Font = new System.Drawing.Font("Trebuchet MS", 8.25F, System.Drawing.FontStyle.Bold),
                        IsEndLabelVisible = false,
                        Format = "HH:mm"
                    },
                    LineColor = System.Drawing.Color.FromArgb(64, 64, 64, 64),
                    MajorGrid =
                    {
                        LineColor = System.Drawing.Color.FromArgb(64, 64, 64, 64)
                    }
                },
                AxisY =
                {
                    IsLabelAutoFit = false,
                    LabelStyle =
                    {
                        Font = new System.Drawing.Font("Trebuchet MS", 8.25F, System.Drawing.FontStyle.Bold)
                    },
                    LineColor = System.Drawing.Color.FromArgb(64, 64, 64, 64),
                    MajorGrid =
                    {
                        LineColor = System.Drawing.Color.FromArgb(64, 64, 64, 64)
                    },
                    IsStartedFromZero = false
                },
                BackColor = System.Drawing.Color.FromArgb(64, 165, 191, 228),
                BackSecondaryColor = Color.White,
                BackGradientStyle = GradientStyle.TopBottom,
                BorderColor = System.Drawing.Color.FromArgb(64, 64, 64, 64),
                BorderDashStyle = ChartDashStyle.Solid,
                ShadowColor = System.Drawing.Color.Transparent
            };

            chart.ChartAreas.Add(chartArea);
        }
    }
}