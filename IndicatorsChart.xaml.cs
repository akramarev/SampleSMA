namespace IndicatorsXaml
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Linq;
    using System.Text;
    using System.Windows.Forms.DataVisualization.Charting;
    using StockSharp.Algo.Candles;

    public partial class CandleChart
    {
        public CandleChart()
        {
            InitializeComponent();
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

        public void Clear()
        {
            _seriesCandles.Points.Clear();
            _seriesFilterMA.Points.Clear();
            _seriesLongMA.Points.Clear();
            _seriesShortMA.Points.Clear();
        }
    }
}