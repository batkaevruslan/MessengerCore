using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Mail;
using System.Text;

using AegisImplicitMail;

using Dev.Common.Exceptions.Generated;
using Dev.Utility.Messaging.Core.Imp.Interfaces;
using Dev.Utility.Messaging.Core.Imp.Stuff;
using Dev.Utility.Messaging.Data.EF.Pub.Entities;

using SslMode = AegisImplicitMail.SslMode;

namespace Dev.Utility.Messaging.Core.Imp.Types
{
    internal sealed class SSLEmailServer : IEmailServer
    {
        private readonly MimeMailer _smtpClient;
        private readonly string _address;
        private readonly string _displayName;

        public SSLEmailServer( SmtpSettings settings )
        {
            _address = settings.FromEmail;
            _displayName = settings.FromDisplayName;

            _smtpClient = new MimeMailer( settings.Host, settings.Port.Value ) {
                User = settings.UserName,
                Password = settings.Password,
                AuthenticationMode = AuthenticationType.Base64,
                Timeout = 10000,
                SslType = SslMode.Ssl
            };
        }

        public void SendMessage( Guid messageId, string recipientEmail, string subject, string text )
        {
            var message = new MimeMailMessage
            {
                From = new MimeMailAddress(_address),
                Subject = subject,
                Body = text,
                IsBodyHtml = true,
                DeliveryNotificationOptions = DeliveryNotificationOptions.OnFailure | DeliveryNotificationOptions.Delay,
                To = {
                    recipientEmail
                },
                Headers = {
                    {Constants.MessageIdHeader, messageId.ToString()}
                },
                BodyEncoding = Encoding.Unicode
            };
            _smtpClient.SendCompleted += mailer_SendCompleted;
            _smtpClient.SendMail(message);
        }

        private static void mailer_SendCompleted(
          object sender,
          AsyncCompletedEventArgs e)
        {
            if (e.UserState != null)
                Trace.WriteLine(e.UserState.ToString());
            if (e.Error != null)
            {
                Trace.WriteLine(e.Error.Message);
                throw Error.NotAvailableExceptionCreator.UnknownSmtpError();
            }
            else if (!e.Cancelled)
            {
                Trace.WriteLine("Send successfull!");
            }
        }
    }
}