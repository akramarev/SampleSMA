using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using StockSharp.Algo.Logging;
using System.Net.Mail;
using System.Configuration;
using System.Net;

namespace SampleSMA.Logging
{
    public class EmailUnitedLogListener : LogListener
    {
        public List<LogMessage> MessageHistory = new List<LogMessage>();

        protected override void OnWriteMessage(LogMessage message)
        {
            this.MessageHistory.Add(message);

            // send email only as a reaction on really important events
            if ((message is ExtendedLogMessage)
                && ((ExtendedLogMessage)message).Importance >= ExtendedLogMessage.ImportanceLevel.High)
            {
                this.SendEmail();
            }
        }

        protected void SendEmail()
        {
            var lastMessage = this.MessageHistory.LastOrDefault();
            if (lastMessage == null)
            {
                return;
            }

            MailMessage message = new MailMessage(
                new MailAddress("ak.robot.k1@gmail.com"),
                new MailAddress(this.EmailLogTo));

            try
            {
                message.Subject = lastMessage.Message;
                message.Body = this.GetEmailBody();

                SmtpClient smtp = new SmtpClient();
                smtp.SendAsync(message, null);
            }
            catch (Exception ex)
            {

            }
        }

        protected string GetEmailBody()
        {
            StringBuilder builder = new StringBuilder();

            foreach (var message in this.MessageHistory.OrderByDescending(m => m.Time))
            {
                builder.AppendLine(String.Format("{0} | {1} || {2}",
                    message.Source.Name,
                    message.Time,
                    message.Message));
            }

            return builder.ToString();
        }

        public string EmailLogTo
        {
            get
            {
                string email = ConfigurationManager.AppSettings["EmailLogTo"];
                if (String.IsNullOrEmpty(email))
                {
                    email = "kramarew@gmail.com";
                }

                return email;
            }
        }
    }
}
