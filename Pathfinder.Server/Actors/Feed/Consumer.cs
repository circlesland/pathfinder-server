using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;

namespace Pathfinder.Server.Actors.Feed
{
    public class Consumer<TPayload> : ReceiveActor
    {
        #region Messages

        public sealed class Hello : HelloBase
        {
            public Hello(FeedSide side, FeedMode selectedFeedMode) : base(side, selectedFeedMode)
            {
            }
        }

        public sealed class Next
        {
        }

        public sealed class Goodbye : GoodbyeBase
        {
            public Goodbye(GoodbyeReason reason) : base(reason)
            {
            }
        }

        #endregion
        
        private ILoggingAdapter Log { get; } = Context.GetLogger();
        
        protected override void PreStart() => Log.Info($"Consumer<{typeof(TPayload).Name}> started.");
        protected override void PostStop() => Log.Info($"Consumer<{typeof(TPayload).Name}> stopped.");
        
        private readonly List<HelloBase> _handshake = new();
        private readonly Func<IEnumerable<HelloBase>, TPayload, Task<bool>> _sink;
        private readonly FeedMode _supportedFeedModes;

        private IActorRef? _feed;
        
        public Consumer(Func<IEnumerable<HelloBase>, TPayload, Task<bool>> sink, FeedMode supportedFeedModes)
        {
            _sink = sink;
            _supportedFeedModes = supportedFeedModes;
            
            Become(WaitingForConnection);
        }

        void WaitingForConnection()
        {
            Log.Info("WaitingForConnection.");
            
            Receive<Feed<TPayload>.Hello>(message =>
            {
                _handshake.Add(message);
                
                // if the feed offers the Infinite mode and we support it then choose it.
                var hello = _supportedFeedModes.HasFlag(FeedMode.Infinite) && message.Mode.HasFlag(FeedMode.Infinite)
                    ? new Hello(FeedSide.Receiver, FeedMode.Infinite)
                    : new Hello(FeedSide.Receiver, FeedMode.Finite);
                
                _handshake.Add(hello);
                Sender.Tell(hello);

                _feed = Sender;
                
                Log.Info("Connected.");
                Become(Ready);
            });
            Receive<Feed<TPayload>.Goodbye>(message =>
            {
                Log.Warning($"The feed ({Sender}) closed the connection before the Hello could be sent. Reason: {Enum.GetName(message.Reason)}");
                Become(Closed);
            });
        }

        void Ready()
        {
            Log.Debug("Ready. Asking for the next item ..");
            _feed.Tell(new Next());
            
            Receive<TPayload>(message =>
            {
                Log.Debug($"Received new TPayload event: {message}");

                var sinkTask = _sink(_handshake, message);
                sinkTask.PipeTo(Self);         
                
                Become(WaitingForSink);
            });
            
            Receive<Feed<TPayload>.Goodbye>(message =>
            {
                if (message.Reason == GoodbyeReason.Eof)
                {
                    Log.Info($"The feed ({Sender}) closed the connection. Reason: {Enum.GetName(message.Reason)}");   
                }
                else
                {
                    Log.Warning($"The feed ({Sender}) closed the connection. Reason: {Enum.GetName(message.Reason)}");
                }
                Become(Closed);
            });
        }

        void WaitingForSink()
        {
            Log.Debug("WaitingForSink.");

            Receive<bool>(message =>
            {
                if (!message)
                {
                    Log.Info($"The sink wants to cancel the feed subscription.");
                    Become(Cancel);
                    return;
                }
                
                Log.Debug($"The sink successfully processed the item. Requesting next item ..");
                Become(Ready);
            });
            Receive<Status.Failure>(message =>
            {
                Log.Error(message.Cause, $"{nameof(_sink)} failed to process the received item.");
                _feed.Tell(new Goodbye(GoodbyeReason.Error));
                throw message.Cause;
            });
            Receive<Status.Success>(_ =>
            {
                Log.Debug($"The sink successfully processed the item. Requesting next item ..");
                Become(Ready);
            });
        }

        void Cancel()
        {
            _feed.Tell(new Goodbye(GoodbyeReason.Cancel));
            Become(Closed);
        }

        void Closed()
        {
            Log.Debug("Closed.");
            Context.Stop(Self);
        }
        
        public static Props Props(Func<IEnumerable<HelloBase>, TPayload, Task<bool>> sink, FeedMode supportedFeedModes)
            => Akka.Actor.Props.Create<Consumer<TPayload>>(sink, supportedFeedModes);
    }
}