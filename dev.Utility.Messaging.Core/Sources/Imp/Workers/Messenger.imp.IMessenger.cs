using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;

using NLog;

using Dev.Backbone.Dispatching.ServiceModel.Common.Pub.Types;
using Dev.Common.Database.EntityFramework.Pub.Extensions;
using Dev.Common.Debug;
using Dev.Common.Debug.Extensions;
using Dev.Common.Exceptions.Generated;
using Dev.Common.Helpers.Extensions;
using Dev.Common.Types.ComplexTypes;
using Dev.Utility.Messaging.Common.Pub.Types.Entities;
using Dev.Utility.Messaging.Common.Pub.Types.TypeDefs;
using Dev.Utility.Messaging.Common.Pub.Workers;
using Dev.Utility.Messaging.Core.Imp.Stuff;
using Dev.Utility.Messaging.Data.EF.Pub;
using Dev.Utility.Messaging.Data.EF.Pub.Entities;
using Dev.Utility.Messaging.Data.EF.Pub.Interfaces;
using Sdk.Common.Dev.Extensions.Pub;

using Constants = Dev.Utility.Messaging.Common.Pub.Types.Constants;
using EventTypeEnum = Dev.Utility.Messaging.Common.Pub.Types.Enums.EventType;

namespace Dev.Utility.Messaging.Core.Imp.Workers
{
    internal partial class Messenger : IMessenger
    {
        private IDebugInfoCollector DebugInfoCollector { get { return Requirements.DebugCollector; } }

        public void SendMessageWithShadowCopies(
            ComponentType contactSource,
            EventTypeEnum eventType,
            ContactInfo carbonCopyRecipient,
            List<ContactInfo> blindCarbonCopyRecipients,
            List<TemplateParameterInfo> templateParameters,
            string language )
        {
            DoSendMessageWithCarbonCopies(
                contactSource,
                eventType,
                carbonCopyRecipient,
                blindCarbonCopyRecipients,
                templateParameters,
                language );
        }

        private void DoSendMessageWithCarbonCopies(
            ComponentType contactSource,
            EventTypeEnum eventType,
            ContactInfo carbonCopyRecipient,
            List<ContactInfo> blindCarbonCopyRecipients,
            List<TemplateParameterInfo> templateParameters,
            string language )
        {
            carbonCopyRecipient.Validate();
            blindCarbonCopyRecipients.ForEach( r => r.Validate() );

            var context = GetOrAddContext();
            var eventTypeDB = GetCachedEventType( eventType );
            AssertAllTemplateParametersSpecified( eventTypeDB, templateParameters );
            var eventDB = GetNotCustomizableEvent( eventTypeDB, context );
            if( !eventDB.IsEnabled ) {
                Loggers.MainLogger.Info(
                    "Event with id '{0}' for context with id '{1}' is disabled, message creation skipped",
                    eventDB.Id,
                    context.Id );
                return;
            }
            var languageDB = GetOrAddCachedLanguage( language );
            var template = GetTemplateForLanguageOrDefaultForEvent( eventDB, languageDB );
            var source = GetContactSource( contactSource );

            var contact = GetOrAddContact( source, context, carbonCopyRecipient );
            CreateMessage( eventTypeDB.Parameters, template, contact, templateParameters );

            CreateBlindCarbnCopyMessages(
                blindCarbonCopyRecipients,
                source,
                context,
                eventTypeDB.Parameters,
                template,
                templateParameters );
            DbContext.SaveChanges( DebugInfoCollector, "Save changes" );
        }

        public void SendMessage(
            ComponentType contactSource,
            EventTypeEnum eventType,
            ContactInfo recipient,
            List<TemplateParameterInfo> templateParameters,
            string language )
        {
            DoSendMessageWithCarbonCopies(
                contactSource,
                eventType,
                recipient,
                new List<ContactInfo>(),
                templateParameters,
                language );
        }

        public DataSubList<MessageInfo> SearchMessages(
            MessageSearchParameters searchParameters,
            int framePosition,
            int frameSize )
        {
            ValidateFramePosition( framePosition );
            ValidateFrameSize( frameSize );
            searchParameters.Validate();

            var context = GetOrAddContext();
            var source = GetContactSource( searchParameters.Source.Value );

            var messagesQuery =
                DbContext.Messages.Include( m => m.Contact )
                    .Include( m => m.Status )
                    .Include( m => m.Template.Event.EventType )
                    .Where( m => m.Contact.SourceId == source.Id && m.Contact.ContextId == context.Id );

            messagesQuery = ApplyFilters( searchParameters, messagesQuery );

            var resultMessages =
                messagesQuery.OrderByDescending( m => m.UpdateTime )
                    .Skip( framePosition )
                    .Take( frameSize )
                    .ToList( DebugInfoCollector, "Search messages" );
            return resultMessages.Select( ConvertToMessageInfo ).ToSubList( messagesQuery.Count() );
        }

        public void ConfirmMessageReading( Guid code )
        {
            var message = DbContext.Messages.Single(
                m => m.Code == code,
                Requirements.DebugCollector,
                "Find message by code",
                () => Error.NotFoundExceptionCreator.MessageWithCode( code ) );
            message.DeliveryStatusId = GetCachedDeliveryStatus( DbContext, Constants.DeliveryStatus.Read ).Id;
            DbContext.SaveChanges();
        }

        private IQueryable<Message> ApplyFilters(
            MessageSearchParameters searchParameters,
            IQueryable<Message> messagesQuery )
        {
            if( searchParameters.EventTypes.IsNotNull() ) {
                var eventTypes = searchParameters.EventTypes.Select( GetCachedEventType );
                var eventTypeIds = eventTypes.Select( x => x.Id );
                messagesQuery = messagesQuery.Where( m => eventTypeIds.Contains( m.Template.Event.EventTypeId ) );
            }
            if( searchParameters.ContactExternalIds.IsNotNull() ) {
                var externalIds = searchParameters.GetContactExternalIds();
                messagesQuery = messagesQuery.Where( m => externalIds.Contains( m.Contact.ExternalId ) );
            }
            return messagesQuery;
        }

        private static MessageInfo ConvertToMessageInfo( Message message )
        {
            return new MessageInfo {
                Contact = ConvertToContactInfo( message ),
                Id = message.Code,
                Event = ConvertToEventInfo( message.Template.Event ),
                Status = ConvertToStatusInfo( message ),
                CreationTime = message.CreationTime,
                UpdateTime = message.UpdateTime
            };
        }

        private static EventInfo ConvertToEventInfo( Event @event )
        {
            return new EventInfo {
                Name = @event.EventType.Name,
                Description = @event.EventType.Description,
                IsEnabled = @event.IsEnabled
            };
        }

        private static ContactInfo ConvertToContactInfo( Message m )
        {
            return new ContactInfo {
                Email = m.Contact.ContactData,
                ExternalId = new ContactId( m.Contact.ExternalId )
            };
        }

        private static DeliveryStatusInfo ConvertToStatusInfo( Message m )
        {
            return new DeliveryStatusInfo {
                Name = m.Status.Name,
                Description = m.Status.Description
            };
        }

        private void CreateBlindCarbnCopyMessages(
            List<ContactInfo> blindCarbonCopyRecipients,
            ContactSource source,
            Context context,
            ICollection<TemplateParameter> requiredTemplateParameters,
            Template template,
            List<TemplateParameterInfo> templateParameters )
        {
            foreach( var recipient in blindCarbonCopyRecipients ) {
                var contact = GetOrAddContact( source, context, recipient );
                CreateMessage( requiredTemplateParameters, template, contact, templateParameters );
            }
        }

        #region Routines
        //===============================================================================================[]

        private void CreateMessage(
            ICollection<TemplateParameter> requiredTemplateParameters,
            Template template,
            Contact contact,
            List<TemplateParameterInfo> actualParameters )
        {
            var deliveryStatus = GetCachedDeliveryStatus( DbContext, Constants.DeliveryStatus.Pending );
            var now = DateTime.Now;
            var message = new Message {
                Contact = contact,
                CreationTime = now,
                RetriesCount = 0,
                UpdateTime = now,
                TemplateId = template.Id,
                DeliveryStatusId = deliveryStatus.Id
            };
            DbContext.Messages.Add( message );
            requiredTemplateParameters.ForEach(
                p => DbContext.TemplateParameterValues.Add(
                    new TemplateParameterValue {
                        Message = message,
                        TemplateParameterId = p.Id,
                        Value = actualParameters.Single( x => x.Name == p.Name ).Value
                    } ) );
        }

        private void AssertAllTemplateParametersSpecified(
            EventType eventTypeDB,
            List<TemplateParameterInfo> actualParameters )
        {
            if( actualParameters.Any( x => x.IsNull() || x.Name.IsNull() ) ) {
                throw new MessengerException( "TemplateParameterInfo can't be null and it's name must be not null" );
            }
            actualParameters.GroupBy( x => x.Name ).Where( g => g.Count() > 1 ).ForEach(
                g => {
                    throw new MessengerException(
                        "Parameter '{0}' is expected once, but it's value was specified several times",
                        g.Key );
                } );
            foreach( var expectedParameter in eventTypeDB.Parameters ) {
                var actualParameter = actualParameters.FirstOrDefault( x => x.Name == expectedParameter.Name );
                if( actualParameter.IsNull() ||
                    actualParameter.Value.IsNullOrWhiteSpace() ) {
                    throw new MessengerException(
                        "Parameter '{0}' is expected, but it's value was not specified ",
                        expectedParameter.Name );
                }
            }
            foreach( var actualParameter in actualParameters ) {
                var expectedParameter = eventTypeDB.Parameters.FirstOrDefault( x => x.Name == actualParameter.Name );
                if( expectedParameter.IsNull() ) {
                    throw new MessengerException(
                        "Parameter '{0}' was not expected, but it is specified with value '{1}'",
                        actualParameter.Name,
                        actualParameter.Value );
                }
            }
        }

        /// <summary>
        /// Возвращает не пользовательские события. Для таких событий справедливо отношение 1 к 1 между EventType и Event
        /// </summary>
        /// <param name="eventTypeDB"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        private Event GetNotCustomizableEvent( EventType eventTypeDB, Context context )
        {
            AssertEventTypeIsNotCustomizable( eventTypeDB );
            var eventDB =
                DbContext.Events.SingleOrDefault(
                    x => x.EventTypeId == eventTypeDB.Id && x.ContextId == context.Id,
                    DebugInfoCollector,
                    "Find event by eventTypeId and contextId" );
            if( eventDB.IsNull() ) {
                eventDB = GetDefaultEvent( eventTypeDB );
            }
            return eventDB;
        }

        private void AssertEventTypeIsNotCustomizable( EventType eventTypeDB )
        {
            if( eventTypeDB.IsCustomizable ) {
                throw new MessengerException( "Event type is customizable" );
            }
        }

        private Event GetDefaultEvent( EventType eventTypeDB )
        {
            var defaultContext = GetDefaultContext();
            return Caches.Events.GetValue(
                defaultContext.Id,
                eventTypeDB.Id,
                () =>
                    DbContext.Events.Single(
                        x => x.ContextId == defaultContext.Id && x.EventTypeId == eventTypeDB.Id,
                        DebugInfoCollector,
                        "Get event by contextId and eventTypeId",
                        () =>
                            new MessengerException(
                                "System context doesn't have event with eventTypeId '{0}'",
                                eventTypeDB.Id ) ) );
        }

        private Context GetDefaultContext()
        {
            return Caches.ContextByName.GetValue(
                Constants.SystemContextName,
                name => {
                    var context = DbContext.Contexts.AsNoTracking()
                        .Single(
                            x => x.Name == name,
                            DebugInfoCollector,
                            "Get system context",
                            () => new MessengerException( "System context was not found in database, create in manually" ) );
                    DbContext.Entry( context ).State = EntityState.Detached;
                    return context;
                } );
        }

        private Context GetOrAddContext()
        {
            var context = DbContext.Contexts.SingleOrDefault(
                x => x.Name == Context.InstanceKey,
                DebugInfoCollector,
                "Get context by name" );
            if( context.IsNull() ) {
                var localDbContext = CreateMessagingContext( Requirements );
                context = new Context {
                    Name = Context.InstanceKey,
                    SmtpSettingsList = new List<SmtpSettings> {
                        new SmtpSettings {
                            IsEnabled = false,
                            SslModeId = GetCachedSslModeByName( Common.Pub.Types.Enums.SslMode.None ).Id
                        }
                    }
                };
                localDbContext.Contexts.Add( context );
                localDbContext.SaveChanges(DebugInfoCollector, "Add new context" );
                DbContext.Entry( context ).State = EntityState.Unchanged;
            }
            return context;
        }

        private ContactSource GetContactSource( ComponentType sourceComponent )
        {
            return DbContext.ContactSources.Single(
                x => x.Name == sourceComponent.ToString(),
                DebugInfoCollector,
                "Get event type by name",
                () =>
                    new MessengerException( "Source '{0}' was not found in database, create it manually", sourceComponent ) );
        }

        private EventType GetCachedEventType( EventTypeEnum eventType )
        {
            return Caches.EventType.GetValue(
                eventType,
                () => {
                    var eventTypeDB =
                        DbContext.EventTypes.AsNoTracking()
                            .Include( x => x.Parameters )
                            .Single(
                                x => x.Name == eventType.ToString(),
                                DebugInfoCollector,
                                "Get event type by name",
                                () =>
                                    new MessengerException(
                                        "Event type '{0}' was not found in database, create it manually",
                                        eventType ) );
                    DbContext.Entry( eventTypeDB ).State = EntityState.Detached;
                    return eventTypeDB;
                } );
        }

        private Language GetOrAddCachedLanguage( string language )
        {
            return Caches.LanguageByName.GetValue(
                language,
                () => {
                    //NOTE: RB> надо бы НЕ создавать запись в таблице автоматом
                    var languageDB = DbContext.Languages.AsNoTracking()
                        .SingleOrDefault( x => x.Name == language, DebugInfoCollector, "Get language by name" );
                    if( languageDB.IsNull() ) {
                        var localDbContext = CreateMessagingContext( Requirements );
                        languageDB = new Language {
                            Name = language
                        };
                        localDbContext.Languages.Add( languageDB );
                        localDbContext.SaveChanges( DebugInfoCollector, "Add new language" );
                        localDbContext.Entry( languageDB ).State = EntityState.Detached;
                    }
                    else {
                        DbContext.Entry( languageDB ).State = EntityState.Detached;
                    }
                    return languageDB;
                } );
        }

        private DeliveryStatus GetCachedDeliveryStatus( MessagingDbContext dbContext, string statusName )
        {
            return Caches.DeliveryStatusByName.GetValue(
                statusName,
                () => {
                    var deliveryStatus =
                        dbContext.DeliveryStatuses.AsNoTracking()
                            .SingleOrDefault(
                                x => x.Name == statusName,
                                DebugInfoCollector,
                                "Get delivary status by name" );
                    if( deliveryStatus.IsNull() ) {
                        deliveryStatus = new DeliveryStatus {
                            Name = statusName,
                            Description = statusName
                        };
                        var dc = CreateMessagingContext( Requirements );
                        dc.DeliveryStatuses.Add( deliveryStatus );
                        dc.SaveChanges();
                    }
                    dbContext.Entry( deliveryStatus ).State = EntityState.Detached;
                    return deliveryStatus;
                } );
        }

        private List<SslMode> GetCachedSslModes()
        {
            return Caches.SslModes.GetValue(
                () =>
                {
                    var dc = CreateMessagingContext(Requirements);
                    var sslModes =
                        dc.SslModes.ToList(DebugInfoCollector,
                                "Get delivary status by name");
                    sslModes.ForEach( mode=> dc.Entry( mode ).State = EntityState.Detached  );

                    return sslModes;
                });
        }

        private Template GetTemplateForLanguageOrDefaultForEvent( Event eventDB, Language language )
        {
            var template = GetActualTemplateForLanguage( eventDB, language );
            if( template.IsNull() ) {
                template = DbContext.Templates.Single(
                    x => x.IsDefault && x.EventId == eventDB.Id && x.IsActual,
                    DebugInfoCollector,
                    "Get template by eventId and languageId",
                    () => new MessengerException( "There is no default template for event with id '{0}'", eventDB.Id ) );
            }
            return template;
        }

        private Template GetActualTemplateForLanguage( Event eventDB, Language language )
        {
            return
                DbContext.Templates.SingleOrDefault(
                    x => x.LanguageId == language.Id && x.EventId == eventDB.Id && x.IsActual,
                    DebugInfoCollector,
                    "Get template by eventId and languageId" );
        }

        private Contact GetOrAddContact( ContactSource source, Context context, ContactInfo contactInfo )
        {
            var contact =
                DbContext.Contacts.SingleOrDefault(
                    x =>
                        x.ContextId == context.Id && x.SourceId == source.Id && x.ExternalId == contactInfo.ExternalId &&
                        x.ContactData == contactInfo.Email,
                    DebugInfoCollector,
                    "Get contact" );
            if( contact.IsNull() ) {
                contact = new Contact {
                    ContextId = context.Id,
                    SourceId = source.Id,
                    ExternalId = contactInfo.ExternalId,
                    ContactData = contactInfo.Email
                };
                DbContext.Contacts.Add( contact );
            }
            return contact;
        }

        //===============================================================================================[]
        #endregion

        #region Validation
        //===============================================================================================[]
        private void ValidateFramePosition( int framePosition )
        {
            if( framePosition < 0 ) {
                throw Error.ContractViolationExceptionCreator.FramePositionIsNegative( framePosition );
            }
        }

        private void ValidateFrameSize( int frameSize )
        {
            if( frameSize < 1 ) {
                throw Error.ContractViolationExceptionCreator.FrameSizeIsNotPositive( frameSize );
            }
            if( frameSize > Stuff.Constants.MaxMessageSearchFrameSize ) {
                throw Error.ContractViolationExceptionCreator.FrameSizeIsTooBig(
                    Stuff.Constants.MaxMessageSearchFrameSize,
                    frameSize );
            }
        }

        //===============================================================================================[]
        #endregion
    }
}