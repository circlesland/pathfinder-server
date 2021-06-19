using Akka.Actor;
using Akka.Event;

namespace Pathfinder.Server.Actors
{
    public class Consumer<TPayload> : UntypedActor
    {
        private ILoggingAdapter Log { get; } = Context.GetLogger();
        
        protected override void PreStart() => Log.Info($"Sink<{typeof(TPayload).Name}> started.");
        protected override void PostStop() => Log.Info($"Sink<{typeof(TPayload).Name}> stopped.");
        
        #region Messages
        
        public sealed class Hello : HelloBase
        {
            public Hello(FeedSide side) : base(side)
            {
            }
        }
        
        public sealed class Request
        {
        }
        
        public sealed class Goodbye : GoodbyeBase
        {
        }

        #endregion
        
        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case Feed<TPayload>.Hello hello:
                    if (hello.Side == FeedSide.Sender)
                    {
                        Sender.Tell(new Hello(FeedSide.Receiver));
                    }
                    else
                    {
                        Sender.Tell(new Goodbye());
                    }
                    break;
                case Feed<TPayload>.Goodbye goodbye:
                    Log.Info($"The sender ({Sender}) 'hung up'.");
                    break;
            }
        }
        
        public static Props Props()
            => Akka.Actor.Props.Create<Consumer<TPayload>>();
    }
}