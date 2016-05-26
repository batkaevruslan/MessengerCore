using System;

using Dev.Utility.Messaging.Common.Pub.Workers;
using Dev.Utility.Messaging.Core.Pub.Requirements;
using Dev.Utility.Messaging.Data.EF.Pub;
using Sdk.Common.Dev.AbstractSystems.Pub.Abstracts;

namespace Dev.Utility.Messaging.Core.Imp.Workers
{
    internal partial class Messenger : AbstractSystemWorker<IMessengerRequirements, MessengerContext>
    {
        private readonly Lazy<MessagingDbContext> _dbContext;

        private MessagingDbContext DbContext { get { return _dbContext.Value; } }

        public Messenger( IMessengerRequirements requirements )
            : base( requirements )
        {
            _dbContext = new Lazy<MessagingDbContext>( () => CreateMessagingContext( Requirements ) );
        }

        private MessagingDbContext CreateMessagingContext( IMessengerRequirements requirements )
        {
            var context = requirements.CreateDbContext();
            context.Configuration.LazyLoadingEnabled = false;
            return context;
        }

        public void Dispose() {}
    }
}