using NLog;

namespace Dev.Utility.Messaging.Core.Imp.Stuff
{
    internal static class Loggers
    {
        public static readonly Logger MainLogger = LogManager.GetLogger( "MainLogger" );
    }
}