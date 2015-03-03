using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ecng.Collections;
using StockSharp.Logging;

namespace SampleSMA.Logging
{
    public class SimpleLogSource : ILogSource
    {
        public SimpleLogSource()
        {
            this.Id = Guid.NewGuid();
            this.Name = "Unnamed Log Source";
        }

        public DateTime CurrentTime { get; private set; }

        public INotifyList<ILogSource> Childs
        {
            get { return null; }
        }

        public Guid Id
        {
            get;
            set;
        }

        public void AddLog(LogMessage message)
        {
            if (this.Log != null)
            {
                this.Log(message);
            }
        }

        public event Action<LogMessage> Log;

        public string Name { get; set; }

        public ILogSource Parent
        {
            get { return null; }
        }

        public LogLevels LogLevel { get; set; }

        public void Dispose()
        {

        }
    }
}
