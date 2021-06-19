using System;
using System.IO;
using Akka.Actor;

namespace Pathfinder.Server.Actors
{
    public class Pathfinder : ReceiveActor
    {
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
        private bool _feedderCaughtUp;
        
        void Starting()
        {
            // Start a PathfinderInitializer which will load the db and performs the edge update for the first time  
            var pathfinderInitializer = Context.ActorOf(
                PathfinderInitializer.Props(_pathfinderProcess, _databaseFile),
                "PathfinderInitializer");

            Context.Watch(pathfinderInitializer);
            
            Receive<Terminated>(message =>
            {
                // Check if the initializer terminated too early
                if (!_pathfinderInitializerDone && message.ActorRef.Equals(pathfinderInitializer))
                {
                    throw new Exception($"The PathfinderInitializer ({pathfinderInitializer}) stopped before " +
                                        $"a 'PathfinderInitializer.Done' message was received.");
                }
            });

            Receive<PathfinderInitializer.Done>(message =>
            {
                _pathfinderInitializerDone = true;
             
                // When the initializer is done create a PathfinderFeeder which can supply 
                // the PathfinderProcess with fresh events.
                // TODO: If the feeder fails, the whole pathfinder fails.
                _pathfinderFeeder = Context.ActorOf(
                    PathfinderFeeder.Props(message.LatestKnownBlock, _rpcGateway), 
                    "Feeder");
            });

            Receive<PathfinderFeeder.CaughtUp>(_ =>
            {
                // The feeder now has all relevant events up to the most recent block in its buffer.
                _feedderCaughtUp = true;
                
                // Set "Feeding" mode and unroll all buffered events to the process.
                Become(Feeding);
                _pathfinderFeeder.Tell(new Buffer.Unroll(_pathfinderProcess));
            });
        }
        
        void Feeding()
        {
        }

        void Available()
        {
        }
        
        
        public static Props Props(
            string executable,
            string databaseFile,
            string rpcGateway) 
            => Akka.Actor.Props.Create<Pathfinder>(executable, databaseFile, rpcGateway);
    }
}