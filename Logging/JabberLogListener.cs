using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using StockSharp.Algo.Logging;
using System.Net.Mail;
using System.Configuration;
using System.Net;
using jabber.client;
using System.Timers;

namespace SampleSMA.Logging
{
    public class JabberLogListener : LogListener
    {
        protected JabberClient Client { get; set; }

        protected Queue<LogMessage> MessageQueue = new Queue<LogMessage>();
        protected Timer CheckTimer = new Timer(10000);

        public JabberLogListener()
        {
            this.Client = new JabberClient()
            {
                User = ConfigurationManager.AppSettings["JabberUser"],
                Password = ConfigurationManager.AppSettings["JabberPassword"],
                Server = ConfigurationManager.AppSettings["JabberServer"]
            };

            this.Client.OnAuthenticate += new bedrock.ObjectHandler(Client_OnAuthenticate);
            this.CheckTimer.Elapsed += new ElapsedEventHandler(CheckTimer_Elapsed);

            this.Client.Connect();
        }

        void Client_OnAuthenticate(object sender)
        {
            CheckTimer.Enabled = true;
        }

        void CheckTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            StringBuilder builder = new StringBuilder();

            while (this.MessageQueue.Count != 0)
            {
                var message = this.MessageQueue.Dequeue();

                builder.AppendLine(String.Format("{0} | {1} || {2}",
                    message.Source.Name,
                    message.Time,
                    message.Message));
            }

            if (builder.Length != 0)
            {
                this.Client.Message(JabberLogTo, builder.ToString());
            }
        }

        protected override void OnWriteMessage(LogMessage message)
        {
            MessageQueue.Enqueue(message);
        }

        public static string JabberLogTo
        {
            get
            {
                string email = ConfigurationManager.AppSettings["JabberLogTo"];
                if (String.IsNullOrEmpty(email))
                {
                    email = "ak.robot.k1@gmail.com";
                }

                return email;
            }
        }
    }
}
