using System;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

using Dev.Common.Exceptions.Generated;
using Dev.Common.Helpers.Extensions;
using Dev.Utility.Messaging.Core.Imp.Interfaces;
using Dev.Utility.Messaging.Core.Imp.Stuff;
using Dev.Utility.Messaging.Data.EF.Pub.Entities;
using Sdk.Common.Dev.Extensions.Pub;

namespace Dev.Utility.Messaging.Core.Imp.Types
{
    internal sealed class DefaultEmailServer : IEmailServer
    {
        private const int Timeout = 10000;
        private readonly SmtpClient _smtpClient;
        private readonly MailAddress _from;

        public DefaultEmailServer( SmtpSettings settings, bool enableSsl )
        {
            _from = new MailAddress( settings.FromEmail, settings.FromDisplayName );

            _smtpClient = new SmtpClient( settings.Host, settings.Port.Value ) {
                Credentials = new NetworkCredential( settings.UserName, settings.Password ),
                EnableSsl = enableSsl,
                Timeout = Timeout,
            };
        }

        public void SendMessage( Guid messageId, string recipientEmail, string subject, string text )
        {
            var message = new MailMessage {
                From = _from,
                To = {
                    recipientEmail
                },
                Body = text,
                Subject = subject,
                DeliveryNotificationOptions = DeliveryNotificationOptions.OnFailure | DeliveryNotificationOptions.Delay,
                Headers = {
                    {Constants.MessageIdHeader, messageId.ToString()}
                },
                IsBodyHtml = true
            };
            try {
                var task = Task.Run( () => _smtpClient.Send( message ) );
                if( !task.Wait( Timeout ) ) {
                    throw new WebException( "Task timeout", WebExceptionStatus.Timeout );
                }
            }
            catch( AggregateException ex ) {
                var firstException = ex.InnerExceptions.First();
                var smtpException = firstException as SmtpException;
                if( smtpException.IsNotNull() &&
                    smtpException.StatusCode == SmtpStatusCode.MustIssueStartTlsFirst ) {
                    throw Error.LoginFailedExceptionCreator.SmtpAuthenticationFailed();
                }
                ex.InnerExceptions.ForEach( Loggers.MainLogger.Error );
                throw Error.NotAvailableExceptionCreator.UnknownSmtpError();
            }
            catch( Exception ex ) {
                Loggers.MainLogger.Error( ex );
                throw Error.NotAvailableExceptionCreator.UnknownSmtpError();
            }
        }
    }
}