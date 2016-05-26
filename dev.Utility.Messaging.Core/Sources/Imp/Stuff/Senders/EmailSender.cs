using System;

using Dev.Common.Helpers.Utils;
using Dev.Utility.Messaging.Core.Imp.Interfaces;
using Dev.Utility.Messaging.Core.Imp.Types;
using Dev.Utility.Messaging.Data.EF.Pub.Entities;

using SslMode = Dev.Utility.Messaging.Common.Pub.Types.Enums.SslMode;

namespace Dev.Utility.Messaging.Core.Imp.Stuff.Senders
{
    internal sealed class EmailSender
    {
        private readonly IEmailServer _emailServer;

        public EmailSender( SmtpSettings settings )
        {
            var sslType = EnumUtil.GetInfo<SslMode>().GetValue( settings.SslMode.Name );
            _emailServer = sslType == SslMode.SSL
                ? ( IEmailServer ) new WebMailEmailServer( settings )
                : new DefaultEmailServer( settings, sslType != SslMode.None );
        }

        public void SendMessage( Guid messageId, string recipientEmail, string subject, string text )
        {
            _emailServer.SendMessage( messageId, recipientEmail, subject, text );
        }
    }
}