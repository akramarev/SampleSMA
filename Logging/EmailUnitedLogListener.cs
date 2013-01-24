using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Mail;
using System.Configuration;
using System.Net;
using System.Timers;
using StockSharp.Logging;

namespace SampleSMA.Logging
{
    public class EmailUnitedLogListener : LogListener
    {
        protected SmtpClient Client { get; set; }

        protected Queue<LogMessage> MessageQueue = new Queue<LogMessage>();
        protected Timer CheckTimer = new Timer(30000);

        public EmailUnitedLogListener()
        {
            this.Client = new SmtpClient();

            this.CheckTimer.Elapsed += CheckTimer_Elapsed;
            this.CheckTimer.Enabled = true;
        }

        void CheckTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            CheckTimer.Stop();

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
                MailMessage mailMessage = new MailMessage(
                new MailAddress("ak.robot.k1@gmail.com"),
                new MailAddress(this.EmailLogTo));

                try
                {
                    mailMessage.Subject = String.Format ("K1 Robot Email Report on {0}", DateTime.Now);
                    mailMessage.Body = builder.ToString();

                    this.Client.SendAsync(mailMessage, null);
                }
                catch (Exception ex)
                {

                }
            }

            CheckTimer.Start();
        }

        protected override void OnWriteMessage(LogMessage message)
        {
            MessageQueue.Enqueue(message);
        }

        public string EmailLogTo
        {
            get
            {
                string email = ConfigurationManager.AppSettings["EmailLogTo"];
                if (String.IsNullOrEmpty(email))
                {
                    email = "ak.robot.k1@gmail.com";
                }

                return email;
            }
        }
    }
}
