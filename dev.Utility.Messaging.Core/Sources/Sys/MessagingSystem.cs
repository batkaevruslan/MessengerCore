using Dev.Utility.Messaging.Common.Pub.Workers;
using Dev.Utility.Messaging.Core.Imp.Workers;
using Dev.Utility.Messaging.Core.Pub.Requirements;
using Sdk.Common.Dev.AbstractSystems.Pub.Abstracts;
using Sdk.Common.Dev.AbstractSystems.Pub.Requirements;

namespace Dev.Utility.Messaging.Core.Sys
{
    public class MessagingSystem : AbstractSystem<EmptyRequirements>
    {
        #region Constructor
        //===============================================================================================[]
        public MessagingSystem()
            : base( new EmptyRequirements() ) {}

        //===============================================================================================[]
        #endregion

        #region Workers creation
        //===============================================================================================[]
        public static IMessenger Create( IMessengerRequirements requirements )
        {
            return new Messenger( requirements );
        }

        public IMessengerAdministration CreateAdministration( IMessengerRequirements requirements )
        {
            return new Messenger( requirements );
        }

        //===============================================================================================[]
        #endregion
    }
}