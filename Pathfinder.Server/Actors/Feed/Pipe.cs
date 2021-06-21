/*
using System;
using Akka.Actor;
using Akka.Event;

namespace Pathfinder.Server.Actors.Feed
{
    public class Pipe<TValue> : ReceiveActor 
    {
        private ILoggingAdapter Log { get; } = Context.GetLogger();
        
        protected override void PreStart() => Log.Info($"Pipe<{typeof(TValue).Name}> started.");
        protected override void PostStop() => Log.Info($"Pipe<{typeof(TValue).Name}> stopped.");

        private TValue? _buffer;

        private readonly IActorRef _feed;
        private readonly IActorRef _consumer;
        
        public Pipe(IActorRef feed, IActorRef consumer)
        {
            _feed = feed;
            _consumer = consumer;
            
            Become(ConnectToFeed);
        }

        void ConnectToFeed()
        {
            throw new NotImplementedException();
        }

        void ConnectToConsumer()
        {
            throw new NotImplementedException();
        }
        
        public static Props Props(IActorRef feed, IActorRef consumer)
            => Akka.Actor.Props.Create<Pipe<TValue>>(feed, consumer);
    }
}
*/