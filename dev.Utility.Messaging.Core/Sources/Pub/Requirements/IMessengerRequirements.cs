using Dev.Common.Debug;
using Dev.Utility.Messaging.Common.Pub.Types.BusinessConfiguration;
using Dev.Utility.Messaging.Data.EF.Pub;
using Dev.Utility.Messaging.Data.EF.Pub.Interfaces;

namespace Dev.Utility.Messaging.Core.Pub.Requirements
{
    public interface IMessengerRequirements
    {
        IDebugInfoCollector DebugCollector { get; }
        IMessengerDependency Dependencies { get; }
        IMessengerConfiguration Configuration { get; }
        MessagingDbContext CreateDbContext();
        string MessageReadingCheckerUrl { get; }
    }
}