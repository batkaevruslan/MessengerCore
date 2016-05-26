using System;
using System.Collections.Generic;

using Dev.Common.Caching;
using Dev.Utility.Messaging.Data.EF.Pub.Entities;

using EventType = Dev.Utility.Messaging.Common.Pub.Types.Enums.EventType;

namespace Dev.Utility.Messaging.Core.Imp.Stuff
{
    internal static class Caches
    {
        public static readonly MemoryCache<EventType, Data.EF.Pub.Entities.EventType> EventType =
            new MemoryCache<EventType, Data.EF.Pub.Entities.EventType>( TimeSpan.FromDays( 1 ) );
        public static readonly MemoryCache<string, Context> ContextByName =
            new MemoryCache<string, Context>( TimeSpan.FromDays( 1 ) );

        /// <summary>
        /// &lt;contextId, eventTypeId, Event&gt;
        /// </summary>
        public static readonly MemoryCacheWithComplexKey<int, int, Event> Events =
            new MemoryCacheWithComplexKey<int, int, Event>( TimeSpan.FromDays( 1 ) );
        public static readonly MemoryCache<string, Language> LanguageByName =
            new MemoryCache<string, Language>( TimeSpan.FromDays( 1 ) );
        public static readonly MemoryCache<string, DeliveryStatus> DeliveryStatusByName =
            new MemoryCache<string, DeliveryStatus>( TimeSpan.FromDays( 1 ) );
        public static readonly SingleValueMemoryCache<List<SslMode>> SslModes =
            new SingleValueMemoryCache<List<SslMode>>( TimeSpan.FromHours( 1 ) );
    }
}