using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Mail;
using System.Configuration;
using System.Net;
using StockSharp.Logging;
using jabber.client;
using System.Timers;

namespace SampleSMA.Logging
{
    public class JabberLogListener : LogListener
    {
        protected const int JABBER_MESSAGE_LENGTH_LIMIT = 1500;
        protected JabberClient Client { get; set; }

        protected Queue<LogMessage> MessageQueue = new Queue<LogMessage>();
        protected Timer CheckTimer = new Timer(5000);

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
            CheckTimer.Start();
        }

        void CheckTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            CheckTimer.Stop();

            StringBuilder builder = new StringBuilder();
            bool isLimitExceeded = false;

            while (this.MessageQueue.Count != 0 && !isLimitExceeded)
            {
                LogMessage message = this.MessageQueue.Dequeue();

                string messageType = String.Empty;
                if (message.Level > LogLevels.Debug)
                {
                    messageType = @"+/'\ ";
                }

                builder.AppendLine(String.Format("{0}{1} | {2} || {3}",
                    messageType,
                    message.Source.Name,
                    message.Time.ToLongTimeString(),
                    message.Message));

                if (builder.Length > JABBER_MESSAGE_LENGTH_LIMIT)
                {
                    isLimitExceeded = true;
                }
            }

            if (builder.Length != 0)
            {
                this.Client.Message(JabberLogTo, builder.ToString());
            }

            CheckTimer.Start();
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

