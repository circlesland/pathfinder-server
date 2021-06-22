using System;
using System.Collections.Immutable;
using System.IO;
using Akka.Actor;
using Akka.Event;
using Pathfinder.Server.Actors.Chain;
using Pathfinder.Server.Actors.Feed;
using Buffer = Pathfinder.Server.Actors.MessageContracts.Buffer;

namespace Pathfinder.Server.Actors.Pathfinder
{
    /// <summary>
    /// Wraps all required components of a pathfinder and makes them accessible as one.
    /// </summary>
    public class Pathfinder : ReceiveActor
    {
        #region Messages

        public sealed class Started
        {
        }
        
        public sealed class Available
        {
        }
        
        public sealed class UnAvailable
        {
        }

        public sealed class FindPath
        {
            public readonly string From;
            public readonly string To;
            public readonly string Value;

            public FindPath(string @from, string to, string value)
            {
                From = from;
                To = to;
                Value = value;
            }
        }
        
        public sealed class FindPathResult
        {
            public readonly bool Success;
            public readonly string? Flow;
            public readonly ImmutableArray<TransferStep> Steps;

            public FindPathResult()
            {
                Success = false;
                Steps = new ImmutableArray<TransferStep>();
            }
            public FindPathResult(string flow, TransferStep[] steps)
            {
                Success = false;
                Flow = flow;
                Steps = steps.ToImmutableArray();
            }
        }
        
        public sealed class TransferStep
        {
            public readonly string Value;
            public readonly string Token;
            public readonly string From;
            public readonly string To;

            public TransferStep(string @from, string to, string token, string value)
            {
                From = from;
                To = to;
                Token = token;
                Value = value;
            }
        }

        #endregion
        
        private ILoggingAdapter Log { get; } = Context.GetLogger();

        protected override void PreStart() => Log.Info($"Pathfinder started.");
        protected override void PostStop() => Log.Info($"Pathfinder stopped.");
        
        
        private readonly IActorRef _pathfinderProcess;
        private IActorRef? _pathfinderFeeder;
        
        private readonly string _databaseFile;
        private readonly string _rpcGateway;
        
        public Pathfinder(
            string executable,
            string databaseFile,
            string rpcGateway)
        {
            if (!File.Exists(databaseFile))
            {
                throw new FileNotFoundException($"Couldn't find the database file at '{databaseFile}'.");
            }
            
            _databaseFile = databaseFile;
            _rpcGateway = rpcGateway;
            
            _pathfinderProcess = Context.ActorOf(
                PathfinderProcess.Props(executable), 
                "PathfinderProcess");
            
            Become(Starting);
        }

        private bool _pathfinderInitializerDone;

        protected override SupervisorStrategy SupervisorStrategy()
        {
            // If anything within the pathfinder dies, let the whole thing die
            return new AllForOneStrategy(0, 0, ex => Directive.Escalate);
        }

        void Starting()
        {
            // Start a PathfinderInitializer which will load the db and performs the edge update for the first time  
            var pathfinderInitializer = Context.ActorOf(
                PathfinderInitializer.Props(_pathfinderProcess, _databaseFile),
                "PathfinderInitializer");

            Context.Watch(pathfinderInitializer);

            Receive<PathfinderInitializer.Done>(message =>
            {
                _pathfinderInitializerDone = true;
             
                /*
                // When the initializer is done create a PathfinderFeeder which can supply 
                // the PathfinderProcess with fresh events.
                // TODO: If the feeder fails, the whole pathfinder fails.
                _pathfinderFeeder = Context.ActorOf(
                    PathfinderFeeder.Props(message.LatestKnownBlock, _rpcGateway), 
                    "Feeder");
                */
            });
            
            Receive<Terminated>(message =>
            {
                // Check if the initializer terminated too early
                if (!_pathfinderInitializerDone && message.ActorRef.Equals(pathfinderInitializer))
                {
                    throw new Exception($"The PathfinderInitializer ({pathfinderInitializer}) stopped before " +
                                        $"a 'PathfinderInitializer.Done' message was received.");
                }
            });
/*
            Receive<PathfinderFeeder.CaughtUp>(_ =>
            {
                // The feeder now has all relevant events up to the most recent block in its buffer.
                // Tell it to feed all events of it's internal buffer to the pathfinder..
                // The feeder will send an "Updated" event when its done.
                _pathfinderFeeder.Tell(new Buffer.FeedToActor(_pathfinderProcess, FeedMode.Finite));
            });
*/          
            Receive<PathfinderFeeder.Updated>(message =>
            {
                // Pathfinder is updated and ready for user requests.
                Context.Parent.Tell(new Started());
                Become(Running);
            });
        }

        void Updating()
        {
            Context.Parent.Tell(new UnAvailable());
            Context.System.EventStream.Unsubscribe(Self, typeof(BlockClock.NextBlock));
            
            Receive<PathfinderFeeder.Updated>(message =>
            {
                // Pathfinder is updated and ready for user requests.
                Become(Running);
            });
        }
        
        void Running()
        {
            Context.Parent.Tell(new Available());
            Context.System.EventStream.Subscribe(Self, typeof(BlockClock.NextBlock));
            
            Receive<BlockClock.NextBlock>(message =>
            {
                // Sync on every new arriving block. If the feeding is still in progress then the PathfinderFeeder
                // will simply ignore the request but will still send a "CaughtUp" message when its done.
                _pathfinderFeeder.Tell(new Buffer.FeedToActor(_pathfinderProcess, FeedMode.Finite));
                Become(Updating);
            });

            Receive<FindPath>(message =>
            {
                Become(Calling);
            });
        }

        void Calling()
        {
            // Pathfinder is processing a user request ..
            Context.Parent.Tell(new UnAvailable());
            Context.System.EventStream.Unsubscribe(Self, typeof(BlockClock.NextBlock));

            Receive<PathfinderProcess.Return>(message =>
            {
                
            });
        }
        
        public static Props Props(
            string executable,
            string databaseFile,
            string rpcGateway) 
            => Akka.Actor.Props.Create<Pathfinder>(executable, databaseFile, rpcGateway);
    }
}