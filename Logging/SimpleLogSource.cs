using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using StockSharp.Algo.Logging;
using Ecng.Collections;

namespace SampleSMA.Logging
{
    public class SimpleLogSource : ILogSource
    {
        public SimpleLogSource()
        {
            this.Id = Guid.NewGuid();
            this.Name = "Unnamed Log Source";
        }

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
            this.Log(message);
        }

        public event Action<LogMessage> Log;

        public string Name { get; set; }

        public ILogSource Parent
        {
            get { return null; }
        }
    }
}
