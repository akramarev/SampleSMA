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
            double candleTime = candle.Time.ToOADate();

            // cleanup old candle at this time
            var oldCandle = _seriesCandles.Points.FirstOrDefault(p => p.XValue == candleTime);
            if (oldCandle != null)
            {
                _seriesCandles.Points.Remove(oldCandle);
            }

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
        }

        public void AddFilterMA(DateTime time, double value)
        {
            var point = new DataPoint(time.ToOADate(), value) { Color = Color.DarkGray };
            _seriesFilterMA.Points.Add(point);
        }

        public void AddLongMA(DateTime time, double value)
        {
            var point = new DataPoint(time.ToOADate(), value) { Color = Color.LightBlue };
            _seriesLongMA.Points.Add(point);
        }

        public void AddShortMA(DateTime time, double value)
        {
            var point = new DataPoint(time.ToOADate(), value) { Color = Color.LightPink };
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