using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using StockSharp.Algo.Indicators.Trend;
using StockSharp.Algo.Indicators;

namespace SampleSMA
{
    public static class ExponentialMovingAverageEx
    {
        public static void RemoveStartFootprint(this ExponentialMovingAverage ema, DecimalIndicatorValue value)
        {
            for (int i = 0; i < ema.Length * 3; i++)
            {
                ema.Process((DecimalIndicatorValue)value);
            } 
        }
    }
}
