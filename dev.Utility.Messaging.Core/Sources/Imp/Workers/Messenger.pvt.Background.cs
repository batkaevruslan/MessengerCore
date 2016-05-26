using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;

using NLog;

using Dev.Common.Helpers.Extensions;
using Dev.Utility.Messaging.Common.Pub.Types;
using Dev.Utility.Messaging.Core.Imp.Stuff.Senders;
using Dev.Utility.Messaging.Core.Pub.Requirements;
using Dev.Utility.Messaging.Data.EF.Pub;
using Dev.Utility.Messaging.Data.EF.Pub.Entities;
using Sdk.Common.Dev.Extensions.Pub;

using EventTypeEnum = Dev.Utility.Messaging.Common.Pub.Types.Enums.EventType;

namespace Dev.Utility.Messaging.Core.Imp.Workers
{
    internal partial class Messenger
    {
        private static readonly Logger MessageBackgroundDeliveryLogger =
            LogManager.GetLogger( Constants.MessageBackgroundDeliveryLoggerName );

        public void DeliverMessage()
        {
            DoDeliverMessages( Requirements );
        }

        private void DoDeliverMessages( object state )
        {
            //NOTE: в случае рассылки писем и от клиентов покупателям нужно параллелить данный алгоритм по разные smtp, ибо каждый из них может виснуть по таймауту
            var requirements = state as IMessengerRequirements;
            var dbContext = CreateMessagingContext( requirements );
            var deliveryStatusSent = GetCachedDeliveryStatus( dbContext, Constants.DeliveryStatus.Sent );
            var deliveryStatusRead = GetCachedDeliveryStatus( dbContext, Constants.DeliveryStatus.Read );
            var deliveryStatusError = GetCachedDeliveryStatus( dbContext, Constants.DeliveryStatus.Error );
            var messagesToDeliver = GetMessagesToDeliverGroupedByContextId(
                dbContext,
                deliveryStatusSent,
                deliveryStatusRead,
                requirements );
            var systemDefaultSmtpSettings = GetDefaultSmtpSettingsForClientNeeds();
            foreach( var messageGroup in messagesToDeliver ) {
                try {
                    var smtpSettings = GetFirstEnabledSmtpSettings( dbContext, messageGroup );
                    if( smtpSettings.IsNotNull() ) {
                        smtpSettings.Validate();
                    }
                    foreach( var message in messageGroup ) {
                        try {
                            AssertSmtpSettingsCanBeUsed( smtpSettings, message );

                            message.RetriesCount++;
                            //для тех отправлений, что не являются рассылками, а являются системными сообщениями, инициализируемыми сервисами, а не клиентом
                            //при отсутствии заданных smtp-настроек используем Smtp нашей компании
                            if( smtpSettings.IsNull() &&
                                !message.Template.Event.EventType.IsCustomizable ) {
                                SendMessage( systemDefaultSmtpSettings, message );
                            }
                            else {
                                SendMessage( smtpSettings, message );
                            }

                            message.DeliveryStatusId = deliveryStatusSent.Id;
                        }
                        catch( Exception ex ) {
                            message.DeliveryStatusId = deliveryStatusError.Id;
                            MessageBackgroundDeliveryLogger.Error(
                                ex,
                                string.Format(
                                    "Error sending message with id '{0}'.{1} Error: {2}",
                                    message.Id,
                                    Environment.NewLine,
                                    ex.Message ) );
                        }
                        message.UpdateTime = DateTime.Now;
                        dbContext.SaveChanges();
                    }
                }
                catch( Exception ex ) {
                    MessageBackgroundDeliveryLogger.Error(
                        ex,
                        "Failed to process messages of context with id = '{0}'".FormatString( messageGroup.Key ) );
                }
            }
        }

        private static void AssertSmtpSettingsCanBeUsed( SmtpSettings smtpSettings, Message message )
        {
            if( smtpSettings.IsNull() &&
                message.Template.Event.EventType.IsCustomizable ) {
                throw new MessengerException( "There is no any available email server" );
            }
        }

        private SmtpSettings GetDefaultSmtpSettingsForClientNeeds()
        {
            var defaultContext = GetDefaultContext();
            var smtpSetting =
                DbContext.SmtpSettings.Include( s => s.SslMode )
                    .FirstOrDefault(
                        settings =>
                            settings.ContextId == defaultContext.Id &&
                            settings.UserName ==
                            Requirements.Configuration.UserNameOfDefaultSystemSmtpSettingsForClientNeeds );
            if( smtpSetting.IsNull() ) {
                //тут можно сообщение в диагностику ватсона
                throw new MessengerException( "Default Smtp settings for client needs were not found" );
            }
            return smtpSetting;
        }

        private static SmtpSettings GetFirstEnabledSmtpSettings(
            MessagingDbContext dbContext,
            IGrouping<int, Message> messageGroup )
        {
            return
                dbContext.SmtpSettings.Include( s => s.SslMode )
                    .FirstOrDefault( settings => settings.ContextId == messageGroup.Key && settings.IsEnabled );
        }

        private void SendMessage( SmtpSettings smtpSettings, Message message )
        {
            var emailSender = new EmailSender( smtpSettings );
            var text = BuildMessageText( message );
            var subject = BuildMessageSubject( message );
            emailSender.SendMessage( message.Code, message.Contact.ContactData, subject, text );
        }

        private static List<IGrouping<int, Message>> GetMessagesToDeliverGroupedByContextId(
            MessagingDbContext dbContext,
            DeliveryStatus deliveryStatusSent,
            DeliveryStatus deliveryStatusRead,
            IMessengerRequirements requirements )
        {
            return
                dbContext.Messages.Include( m => m.Contact.Context )
                    .Include( m => m.Template.Event.EventType )
                    .Include( m => m.TemplateParameterValues.Select( tpv => tpv.Parameter ) )
                    .Where(
                        m =>
                            m.DeliveryStatusId != deliveryStatusSent.Id && m.DeliveryStatusId != deliveryStatusRead.Id &&
                            m.RetriesCount < requirements.Configuration.BackgroundDeliverySettings.MaxRetriesCount )
                    .ToList()
                    .GroupBy( m => m.Contact.ContextId )
                    .ToList();
        }

        private string BuildMessageSubject( Message message )
        {
            return message.TemplateParameterValues.Aggregate(
                message.Template.Subject,
                ( current, parameterValues ) =>
                    current.Replace( string.Format( "{{{0}}}", parameterValues.Parameter.Name ), parameterValues.Value ) );
        }

        private string BuildMessageText( Message message )
        {
            var resultMessage = message.TemplateParameterValues.Aggregate(
                message.Template.Text,
                ( current, parameterValues ) =>
                    current.Replace( string.Format( "{{{0}}}", parameterValues.Parameter.Name ), parameterValues.Value ) );
            if( NeedCheckReading( message ) ) {
                resultMessage = resultMessage +
                                "<br/><img src=\"{0}?{1}={2}\">".FormatString(
                                    Requirements.MessageReadingCheckerUrl,
                                    Constants.MessageCodeParameterName,
                                    message.Code );
            }
            return resultMessage;
        }

        private static bool NeedCheckReading( Message message )
        {
            return message.Contact.Context.Name == Constants.SystemContextName;
        }
    }
}