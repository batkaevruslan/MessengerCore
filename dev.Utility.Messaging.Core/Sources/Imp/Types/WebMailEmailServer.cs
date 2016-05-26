using System;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using System.Web.Mail;

using Dev.Common.Exceptions.Generated;
using Dev.Utility.Messaging.Core.Imp.Interfaces;
using Dev.Utility.Messaging.Core.Imp.Stuff;
using Dev.Utility.Messaging.Data.EF.Pub.Entities;

using MailMessage = System.Web.Mail.MailMessage;

namespace Dev.Utility.Messaging.Core.Imp.Types
{
    internal sealed class WebMailEmailServer : IEmailServer
    {
        private readonly string _userName;
        private readonly string _password;
        private readonly string _host;
        private int? _port;
        private readonly MailAddress _from;

        public WebMailEmailServer( SmtpSettings settings )
        {
            _userName = settings.UserName;
            _password = settings.Password;
            _host = settings.Host;
            _port = settings.Port;
            _from = new MailAddress( settings.FromEmail, settings.FromDisplayName );
        }

        public void SendMessage( Guid messageId, string recipientEmail, string subject, string text )
        {
            var myMail = new MailMessage {
                From = _from.ToString(),
                To = recipientEmail,
                Subject = subject,
                BodyFormat = MailFormat.Html,
                Body = text,
                Headers = {
                    {Constants.MessageIdHeader, messageId.ToString()}
                }
            };
            myMail.Fields.Add( "http://schemas.microsoft.com/cdo/configuration/smtpserver", _host );
            if( _port.HasValue ) {
                myMail.Fields.Add( "http://schemas.microsoft.com/cdo/configuration/smtpserverport", _port );
            }
            myMail.Fields.Add( "http://schemas.microsoft.com/cdo/configuration/sendusing", "2" );
            //sendusing: cdoSendUsingPort, value 2, for sending the message using 
            //the network.

            //smtpauthenticate: Specifies the mechanism used when authenticating 
            //to an SMTP 
            //service over the network. Possible values are:
            //- cdoAnonymous, value 0. Do not authenticate.
            //- cdoBasic, value 1. Use basic clear-text authentication. 
            //When using this option you have to provide the user name and password 
            //through the sendusername and sendpassword fields.
            //- cdoNTLM, value 2. The current process security context is used to 
            // authenticate with the service.
            myMail.Fields.Add( "http://schemas.microsoft.com/cdo/configuration/smtpauthenticate", "1" );
            //Use 0 for anonymous
            myMail.Fields.Add( "http://schemas.microsoft.com/cdo/configuration/sendusername", _userName );
            myMail.Fields.Add( "http://schemas.microsoft.com/cdo/configuration/sendpassword", _password );
            myMail.Fields.Add( "http://schemas.microsoft.com/cdo/configuration/smtpusessl", "true" );

            var portPart = _port.HasValue ? ( ":" + _port.Value ) : string.Empty;
            SmtpMail.SmtpServer = _host + portPart;

            try {
                var task = Task.Run( () => SmtpMail.Send( myMail ) );
                const int Timeout = 10000;
                if( !task.Wait( Timeout ) ) {
                    throw new WebException( "Task timeout", WebExceptionStatus.Timeout );
                }
            }
            catch( Exception ex ) {
                Loggers.MainLogger.Error( ex );
                throw Error.NotAvailableExceptionCreator.UnknownSmtpError();
            }
        }
    }
}