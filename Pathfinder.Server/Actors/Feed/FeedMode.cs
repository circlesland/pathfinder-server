using System;

namespace Pathfinder.Server.Actors.Feed
{
    [Flags]
    public enum FeedMode
    {
        Finite = 1,
        Infinite = 2,
        Both = 1 | 2
    }
}