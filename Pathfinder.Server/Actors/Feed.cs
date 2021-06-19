using Akka.Actor;
using Akka.Event;

namespace Pathfinder.Server.Actors
{
    #region Helper types
    
    public enum FeedSide
    {
        Sender,
        Receiver
    }
    public abstract class HelloBase
    {
        public readonly FeedSide Side;

        public HelloBase(FeedSide side)
        {
            Side = side;
        }
    }
    public abstract class GoodbyeBase
    {
    }
    
    #endregion
    
    public class Feed<TPayload> : UntypedActor
    {
        private ILoggingAdapter Log { get; } = Context.GetLogger();
        
        protected override void PreStart() => Log.Info($"Feed<{typeof(TPayload).Name}> started.");
        protected override void PostStop() => Log.Info($"Feed<{typeof(TPayload).Name}> stopped.");
        
        #region Messages
        
        public sealed class Hello : HelloBase
        {
            public Hello(FeedSide side) : base(side)
            {
            }
        }
        
        public sealed class Goodbye : GoodbyeBase
        {
        }

        #endregion

        private readonly HelloBase _feedHello;
        
        private readonly IActorRef _sink;
        private HelloBase? _sinkHello;

        public Feed(IActorRef sink)
        {
            _feedHello = new Hello(FeedSide.Sender);
            
            _sink = sink;
            _sink.Tell(_feedHello);
        }
        
        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case Consumer<TPayload>.Hello hello:
                    Log.Info($"The recipient ({Sender}) accepted the connection.");
                    _sinkHello = hello;
                    break;
                case Consumer<TPayload>.Goodbye goodbye:
                    Log.Info($"The recipient ({Sender}) 'hung up'.");
                    break;
            }
        }
        
        public static Props Props()
            => Akka.Actor.Props.Create<Feed<TPayload>>();
    }
}