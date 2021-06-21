using Akka.Actor;
using Akka.Event;
using Pathfinder.Server.Actors.Feed;

namespace Pathfinder.Server.Actors.MessageContracts
{
    public class Buffer
    {
        public abstract class EmptyBuffer
        {
        }
        
        /// <summary>
        /// Sends the buffered events one by one to the actor at the <see cref="To"/> address and clears the buffer.
        /// </summary>
        public sealed class Unroll : EmptyBuffer
        {
            public readonly IActorRef To;

            public Unroll(IActorRef to)
            {
                To = to;
            }
        }
        
        /// <summary>
        /// When a buffer is fed to another actor then the items are "offered" one by one and
        /// the <see cref="To"/> actor "pulls" the offered item when its done with processing the current one.
        /// </summary>
        public sealed class Feed : EmptyBuffer
        {
            public readonly IActorRef To;
            public readonly FeedMode FeedMode;

            public Feed(IActorRef to, FeedMode feedMode)
            {
                To = to;
                FeedMode = feedMode;
            }
        }
        
        /// <summary>
        /// Sends the buffered events one by one to the system's <see cref="EventStream"/> and clears the buffer.
        /// </summary>
        public sealed class Publish : EmptyBuffer
        {
        }
    }
}