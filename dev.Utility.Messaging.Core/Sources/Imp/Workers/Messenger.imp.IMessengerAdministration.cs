using System;
using System.Data.Entity;
using System.Linq;

using Dev.Common.Database.EntityFramework.Pub.Extensions;
using Dev.Common.Debug.Extensions;
using Dev.Common.Exceptions.Generated;
using Dev.Common.Helpers.Extensions;
using Dev.Common.Helpers.Stuff;
using Dev.Common.Helpers.Utils;
using Dev.Common.MetaUI.Pub;
using Dev.Common.Types.ComplexTypes;
using Dev.Utility.Messaging.Common.Pub.Types;
using Dev.Utility.Messaging.Common.Pub.Types.Entities;
using Dev.Utility.Messaging.Common.Pub.Workers;
using Dev.Utility.Messaging.Core.Imp.Stuff.Senders;
using Dev.Utility.Messaging.Data.EF.Pub.Entities;

using EventType = Dev.Utility.Messaging.Common.Pub.Types.Enums.EventType;
using SslMode = Dev.Utility.Messaging.Common.Pub.Types.Enums.SslMode;

namespace Dev.Utility.Messaging.Core.Imp.Workers
{
    internal sealed partial class Messenger : IMessengerAdministration
    {
        public DataList<EmailServerBaseInfo> GetEmailServerInfoList()
        {
            var context = GetOrAddContext();
            var smtpSettings =
                DbContext.SmtpSettings.Include( s => s.SslMode ).Where( s => s.ContextId == context.Id ).ToList();
            return smtpSettings.Select(
                s => new EmailServerBaseInfo {
                    Id = s.Id,
                    DisplayName = "Smtp {0}".FormatString( smtpSettings.IndexOf( s ) + 1 ),
                    IsEnabled = s.IsEnabled
                } ).ToDataList();
        }

        private static SslMode ConvertToSslMode( string sslModeName )
        {
            return EnumUtil.GetInfo<SslMode>().GetValue( sslModeName );
        }

        public EmailServerInfo GetEmailServerInfo( int serverId, bool includeMetaInfo )
        {
            var context = GetOrAddContext();
            var settings = FindSmtpSettings( serverId, context );
            var emailServer = new MappingHelper().Map<SmtpSettings, EmailServerInfo>( settings );
            var sslModeName = GetCachedSslModes().Single( m => m.Id == settings.SslModeId ).Name;
            emailServer.SslMode = ConvertToSslMode( sslModeName );

            if( includeMetaInfo ) {
                emailServer.MetaInfo =
                    new MetaInfo(
                        FieldMetaInfo.CreateSelectBool(
                            "IsEnabled",
                            "Is current email server enabled for emailing",
                            "Enabled",
                            "Disabled",
                            "Default limited server will be used, if all servers are disabled" ),
                        FieldMetaInfo.CreateText( "Host", "SMTP Server" ),
                        FieldMetaInfo.CreateText( "Port", "SMTP port" ),
                        FieldMetaInfo.CreateSelectEnum<SslMode>(
                            "SslMode",
                            "SSL\\TSL mode",
                            valueTranslateContext: "interface:Enums:SslMode" ),
                        FieldMetaInfo.CreateText( "UserName", "SMTP User name" ),
                        FieldMetaInfo.CreateText( "Password", "SMTP Password" ),
                        FieldMetaInfo.CreateText( "FromDisplayName", "Display name to use in emails" ),
                        FieldMetaInfo.CreateText( "FromEmail", "Sender's email" ) );
            }
            return emailServer;
        }

        public void UpdateEmailServerInfo( int serverId, EmailServerUpdateData updateData )
        {
            var context = GetOrAddContext();
            var settings = FindSmtpSettings( serverId, context );

            UpdateSmtpSettings( updateData, settings );
            DbContext.SaveChanges( DebugInfoCollector, "Update smtp settings" );
        }

        public void TestEmailServer( int serverId, string recipientEmail, string language )
        {
            AssertEmailIsValid( recipientEmail );

            var context = GetOrAddContext();
            var settings = FindSmtpSettings( serverId, context );
            settings.Validate();
            var eventTypeDB = GetCachedEventType( EventType.TestEmailSent );
            var eventDB = GetNotCustomizableEvent( eventTypeDB, context );
            var languageDB = GetOrAddCachedLanguage( language );
            var template = GetTemplateForLanguageOrDefaultForEvent( eventDB, languageDB );
            var emailSender = new EmailSender( settings );

            DebugInfoCollector.MeasuredExecute(
                "External",
                "Send email with smtp",
                () => emailSender.SendMessage( Guid.NewGuid(), recipientEmail, template.Subject, template.Text ) );
        }

        public void UpdateMessageTemplate( EventType eventType, string language, MessageTemplateUpdateData updateData )
        {
            updateData.Validate();
            var context = GetOrAddContext();
            var eventTypeDB = GetCachedEventType( eventType );
            var eventDB = GetNotCustomizableEvent( eventTypeDB, context );
            var languageDB = GetOrAddCachedLanguage( language );
            var template = GetTemplateForLanguageOrDefaultForEvent( eventDB, languageDB );
            if( Context.InstanceKey == Constants.SystemContextName ) {
                if( DbContext.Messages.Any( m => m.TemplateId == template.Id ) ) {
                    var newTemplate = new Template {
                        EventId = eventDB.Id,
                        IsActual = true,
                        Subject = template.Subject,
                        Text = template.Text,
                        LanguageId = languageDB.Id,
                        IsDefault = template.IsDefault
                    };
                    UpdateTemplate( updateData, newTemplate );
                    DbContext.Templates.Add( newTemplate );
                    template.IsActual = false;
                    template.IsDefault = false;
                }
                else {
                    UpdateTemplate( updateData, template );
                }
            }
            else {
                //var doesContextHaveItsOwnEvent = template.is
            }
            DbContext.SaveChanges( DebugInfoCollector, "Save" );
        }

        public DataList<EventInfo> GetEmailedEvents()
        {
            var eventsQuery = DbContext.Events.Include( e => e.EventType ).AsQueryable();
            if( Context.InstanceKey != Constants.SystemContextName ) {
                eventsQuery = eventsQuery.Where( e => !e.EventType.IsInternal );
            }
            return eventsQuery.ToList().Select( ConvertToEventInfo ).OrderBy( x => x.Description ).ToDataList();
        }

        public MessageTemplateInfo GetMessageTemplate( EventType eventType, string language )
        {
            var context = GetOrAddContext();
            var eventTypeDB = GetCachedEventType( eventType );
            var eventDB = GetNotCustomizableEvent( eventTypeDB, context );
            var languageDB = GetOrAddCachedLanguage( language );
            var template = GetTemplateForLanguageOrDefaultForEvent( eventDB, languageDB );
            return new MessageTemplateInfo {
                Subject = template.Subject,
                Text = template.Text,
                Parameters =
                    eventTypeDB.Parameters.Select( p => new TemplateParameterInfo( p.Name, null, p.Description ) )
                        .ToList()
            };
        }

        #region Routines
        //===============================================================================================[]
        private static void AssertEmailIsValid( string email )
        {
            if( string.IsNullOrWhiteSpace( email ) ) {
                throw Error.ContractViolationExceptionCreator.EmailNotProvided();
            }
            if( !RegexHelper.IsValidEmail( email ) ) {
                throw Error.ValidationErrorExceptionCreator.EmailIsIncorrect( email );
            }
        }

        private static void UpdateTemplate( MessageTemplateUpdateData updateData, Template template )
        {
            if( updateData.Subject.IsNotNullOrWhiteSpace() ) {
                template.Subject = updateData.Subject;
            }
            if( updateData.Text.IsNotNullOrWhiteSpace() ) {
                template.Text = updateData.Text;
            }
        }

        private void UpdateSmtpSettings( EmailServerUpdateData updateData, SmtpSettings settings )
        {
            if( updateData.IsEnabled.HasValue ) {
                settings.IsEnabled = updateData.IsEnabled.Value;
            }
            if( updateData.SslMode.HasValue ) {
                settings.SslModeId = GetCachedSslModeByName( updateData.SslMode.Value ).Id;
            }
            if( updateData.Host.IsNotNull() ) {
                settings.Host = updateData.Host;
            }
            if( updateData.Port.HasValue ) {
                settings.Port = updateData.Port.Value;
            }
            if( updateData.UserName.IsNotNullOrWhiteSpace() ) {
                settings.UserName = updateData.UserName;
            }
            if( updateData.Password.IsNotNullOrWhiteSpace() ) {
                settings.Password = updateData.Password;
            }
            if( updateData.FromDisplayName.IsNotNullOrWhiteSpace() ) {
                settings.FromDisplayName = updateData.FromDisplayName;
            }
            if( updateData.FromEmail.IsNotNullOrWhiteSpace() ) {
                AssertEmailIsValid( updateData.FromEmail );
                settings.FromEmail = updateData.FromEmail;
            }
        }

        private Data.EF.Pub.Entities.SslMode GetCachedSslModeByName( SslMode sslMode )
        {
            return GetCachedSslModes().Single( m => m.Name == sslMode.ToString() );
        }

        private SmtpSettings FindSmtpSettings( int smtpSettingsId, Context context )
        {
            return
                DbContext.SmtpSettings.Include( s => s.SslMode )
                    .Single(
                        s => s.ContextId == context.Id && s.Id == smtpSettingsId,
                        () => Error.NotFoundExceptionCreator.EmailServerWithIdNotFound( smtpSettingsId ) );
        }

        //===============================================================================================[]
        #endregion
    }
}