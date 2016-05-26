using System;

namespace Dev.Utility.Messaging.Core.Imp.Interfaces
{
    internal interface IEmailServer
    {
        void SendMessage( Guid messageId, string recipientEmail, string subject, string text );
    }
}