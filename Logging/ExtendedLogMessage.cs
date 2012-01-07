using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using StockSharp.Algo.Logging;

namespace SampleSMA.Logging
{
    public class ExtendedLogMessage : LogMessage
    {
        public enum ImportanceLevel : byte
        {
            None = 0,
            Low = 63,
            Middle = 127,
            High = 255
        }

        public ImportanceLevel Importance { get; protected set; }

        public ExtendedLogMessage(ILogSource source, DateTime time, ErrorTypes type, ImportanceLevel importance, string message, params object[] args)
            : base(source, time, type, message, args)
        {
            this.Importance = importance;
        }
    }
}
