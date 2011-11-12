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
using StockSharp.Xaml;
using System;

    public partial class CandleChart
    {
        private readonly ChartArea _chartAreaCandles;

        private readonly Series _seriesCandles;

        private readonly Series _seriesFilterMA;
        private readonly Series _seriesLongMA;
        private readonly Series _seriesShortMA;

        /// <summary>
        /// Графической компонент отображения индикаторов на Candle графике
        /// </summary>
        public CandleChart()
        {
            InitializeComponent();

            var chart = Chart;

            chart.BackColor = System.Drawing.Color.FromArgb(211, 223, 240);
            chart.BorderlineDashStyle = ChartDashStyle.Solid;

            _chartAreaCandles = new ChartArea("ChartAreaCandles")
            {
                AxisX =
                    {
                        IsLabelAutoFit = false,
                        LabelStyle =
                            {
                                Font = new System.Drawing.Font("Trebuchet MS", 8F, System.Drawing.FontStyle.Regular),
                                IsEndLabelVisible = false,
                                Format = "HH:mm"
                            },
                        LineColor = System.Drawing.Color.FromArgb(64, 64, 64, 64),
                        MajorGrid =
                            {
                                LineColor = System.Drawing.Color.FromArgb(64, 64, 64, 64)
                            },
                        IntervalAutoMode = System.Windows.Forms.DataVisualization.Charting.IntervalAutoMode.VariableCount,
                        IntervalType = DateTimeIntervalType.Minutes
                    },
                AxisY =
                    {
                        IsLabelAutoFit = false,
                        LabelStyle =
                            {
                                Font = new System.Drawing.Font("Trebuchet MS", 8F, System.Drawing.FontStyle.Bold)
                            },
                        LineColor = System.Drawing.Color.FromArgb(64, 64, 64, 64),
                        MajorGrid =
                            {
                                LineColor = System.Drawing.Color.FromArgb(64, 64, 64, 64)
                            },
                        IsStartedFromZero = false
                    },
                BackColor = System.Drawing.Color.White,
            };

            chart.ChartAreas.Add(_chartAreaCandles);

            chart.Palette = ChartColorPalette.SemiTransparent;

            #region Create Chart Series (Candles and 3 MAs)

            _seriesCandles = new Series("SeriesCandles")
            {
                ChartArea = "ChartAreaCandles",
                ChartType = SeriesChartType.Candlestick,
                XValueType = ChartValueType.DateTime,
                IsXValueIndexed = true
            };

            _seriesFilterMA = new Series("SeriesFilterMA")
            {
                ChartArea = "ChartAreaCandles",
                ChartType = SeriesChartType.Line,
                XValueType = ChartValueType.DateTime,
                IsXValueIndexed = true
            };

            _seriesLongMA = new Series("SeriesLongMA")
            {
                ChartArea = "ChartAreaCandles",
                ChartType = SeriesChartType.Line,
                XValueType = ChartValueType.DateTime,
                IsXValueIndexed = true
            };

            _seriesShortMA = new Series("SeriesShortMA")
            {
                ChartArea = "ChartAreaCandles",
                ChartType = SeriesChartType.Line,
                XValueType = ChartValueType.DateTime,
                IsXValueIndexed = true
            };

            #endregion

            chart.Series.Add(_seriesCandles);

            chart.Series.Add(_seriesFilterMA);
            chart.Series.Add(_seriesLongMA);
            chart.Series.Add(_seriesShortMA);
        }

        public void AddCandle(TimeFrameCandle candle)
        {
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

            //// get max/min y values in bounded set
            //var maxY = _seriesCandles.Points.Max(x => x.YValues[1]);
            //var minY = _seriesCandles.Points.Min(x => x.YValues[0]);

            //// pad max/min y values
            //_chartAreaCandles.AxisY.Maximum = maxY + ((maxY - minY) * 0.05);
            //_chartAreaCandles.AxisY.Minimum = minY - ((maxY - minY) * 0.05);
        }

        public void AddFilterMA(DateTime time, double value)
        {
            var point = new DataPoint(time.ToOADate(), value) { Color = Color.DarkGray };
            _seriesFilterMA.Points.Add(point);
        }

        public void AddLongMA(DateTime time, double value)
        {
            var point = new DataPoint(time.ToOADate(), value) { Color = Color.LightCoral };
            _seriesLongMA.Points.Add(point);
        }

        public void AddShortMA(DateTime time, double value)
        {
            var point = new DataPoint(time.ToOADate(), value) { Color = Color.LightBlue };
            _seriesShortMA.Points.Add(point);
        }

        public void AddCandles(IEnumerable<TimeFrameCandle> candlesList)
        {
            foreach (var candle in candlesList.ToList())
            {
                AddCandle(candle);
            }
        }

        //public void AddNewChartArea(string name, Chart chart)
        //{
        //    var chartArea = new ChartArea
        //    {
        //        Name = name,
        //        AlignWithChartArea = "ChartAreaCandles",
        //        Area3DStyle =
        //        {
        //            IsClustered = true,
        //            Perspective = 10,
        //            IsRightAngleAxes = false,
        //            WallWidth = 0,
        //            Inclination = 10,
        //            Rotation = 10
        //        },
        //        AxisX =
        //        {
        //            IntervalType = DateTimeIntervalType.Minutes,
        //            IsLabelAutoFit = false,
        //            LabelStyle =
        //            {
        //                Font = new System.Drawing.Font("Trebuchet MS", 8.25F, System.Drawing.FontStyle.Bold),
        //                IsEndLabelVisible = false,
        //                Format = "HH:mm"
        //            },
        //            LineColor = System.Drawing.Color.FromArgb(64, 64, 64, 64),
        //            MajorGrid =
        //            {
        //                LineColor = System.Drawing.Color.FromArgb(64, 64, 64, 64)
        //            }
        //        },
        //        AxisY =
        //        {
        //            IsLabelAutoFit = false,
        //            LabelStyle =
        //            {
        //                Font = new System.Drawing.Font("Trebuchet MS", 8.25F, System.Drawing.FontStyle.Bold)
        //            },
        //            LineColor = System.Drawing.Color.FromArgb(64, 64, 64, 64),
        //            MajorGrid =
        //            {
        //                LineColor = System.Drawing.Color.FromArgb(64, 64, 64, 64)
        //            },
        //            IsStartedFromZero = false
        //        },
        //        BackColor = System.Drawing.Color.FromArgb(64, 165, 191, 228),
        //        BackSecondaryColor = Color.White,
        //        BackGradientStyle = GradientStyle.TopBottom,
        //        BorderColor = System.Drawing.Color.FromArgb(64, 64, 64, 64),
        //        BorderDashStyle = ChartDashStyle.Solid,
        //        ShadowColor = System.Drawing.Color.Transparent
        //    };

        //    chart.ChartAreas.Add(chartArea);
        //}
    }
}