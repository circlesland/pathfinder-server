using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Akka.Actor;
using Akka.Event;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Newtonsoft.Json.Linq;
using Pathfinder.Server.Actors.Chain;
using Pathfinder.Server.Actors.Pathfinder;
using Pathfinder.Server.Actors.System;
using Pathfinder.Server.contracts;

namespace Pathfinder.Server.Actors
{
    public class Server : ReceiveActor
    {       
        private ILoggingAdapter Log { get; } = Context.GetLogger();
        
        protected override void PreStart() => Log.Info($"Main started.");
        protected override void PostStop() => Log.Info($"Main stopped.");

        private readonly IActorRef _realTimeClock;
        private readonly IActorRef _blockClock;
        
        private readonly IActorRef _signupEventSource;
        private readonly IActorRef _organizationSignupEventSource;
        private readonly IActorRef _trustEventSource;
        private readonly IActorRef _transferEventSource;

        private IActorRef? _eventBuffer;
        
        private readonly Dictionary<IActorRef, bool> _pathfinders = new();

        private int _missingPathfinders = 2;
        private BigInteger? _lastBlockInDb;
        
        string _executable = "/home/daniel/src/pathfinder/build/pathfinder";
        string _rpcGateway = "https://rpc.circles.land";
        string _dbFile = "/home/daniel/src/circles-world/PathfinderServer/Pathfinder.Server/db.dat";
        
        protected override SupervisorStrategy SupervisorStrategy()
        {
            // If anything within the PathfinderFeeder dies, let the whole thing die
            return new OneForOneStrategy(2, 5000, ex =>
            {
                if (Sender.Equals(_eventBuffer))
                {
                    // We're dead..
                    // Since all pathfinders use the same shared buffer but have
                    // different states, it will not be possible to reconstruct who
                    // needs which event.
                    // To avoid instances with corrupted data its best to kill it all
                    // and to start over.
                    // TODO: It doesn't have to be over.. Restart the Buffer, leave the instances running and then restart them one by one
                }

                if (_catchUpQueries.ContainsKey(Sender))
                {
                    // Catch up queries can be restarted
                    return Directive.Restart;
                }

                return Directive.Escalate;
            });
        }
        
        public Server()
        {
            _realTimeClock = Context.ActorOf(RealTimeClock.Props(), "RealTimeClock");
            _blockClock = Context.ActorOf(BlockClock.Props(_rpcGateway), "BlockClock");
            
            _signupEventSource = Context.ActorOf(BlockchainEventSource<SignupEventDTO>.Props(_rpcGateway), "SignupEventSource");
            _organizationSignupEventSource = Context.ActorOf(BlockchainEventSource<OrganizationSignupEventDTO>.Props(_rpcGateway), "OrganizationSignupEventSource");
            _trustEventSource = Context.ActorOf(BlockchainEventSource<TrustEventDTO>.Props(_rpcGateway), "TrustEventSource");
            _transferEventSource = Context.ActorOf(BlockchainEventSource<TransferEventDTO>.Props(_rpcGateway), "TransferEventSource");

            SpawnNextPathfinder();
            
            Become(Starting);
        }

        #region States

        void Starting()
        {
            Log.Info("Starting");
            
            // Start the first pathfinder process and initialize it with the database file.
            // Then find the last block in the database file and throw away the pathfinder instance.
            var scout = Context.ActorOf(PathfinderProcess.Props(_executable));
            scout.Tell(new PathfinderProcess.Call(RpcMessage.LoadDb(_dbFile), Self));

            Context.Watch(scout); // Watch the scout. It shouldn't die before it did its work
            var scoutMustDie = false; // Is 'true' when the death of the scout is expected

            Receive<Terminated>(message =>
            {
                if (message.ActorRef.Equals(scout))
                {
                    if (!scoutMustDie)
                    {
                        // The scout process terminated before we got our result.
                        // This is fatal. Something might be wrong with the db file.
                        throw new Exception($"Couldn't start the server with the following parameters: " +
                                            $"executable: {_executable}; " +
                                            $"dbFile: {_dbFile}; " +
                                            $"rpcGateway: {_rpcGateway}");
                    }
                }
                if (message.ActorRef.Equals(_eventBuffer))
                {
                    throw new Exception($"The Buffer was Terminated.");
                }
            });
            
            Receive<PathfinderProcess.Return>(message =>
            {
                // See if we got the blockNumber
                dynamic result = JObject.Parse(message.ResultJson);
                if (result.blockNumber == 0)
                {
                    throw new Exception($"The scout process finished the 'loaddb' call with the following (unexpected) result: {message.ResultJson}");
                }

                // Store the blockNo and kill the scout 
                _lastBlockInDb = (ulong)result.blockNumber;
                
                scoutMustDie = true;
                Context.Stop(scout);
                
                // Start a buffer and fill it with all events from _latestKnownBlock to now afterwards.
                // When the buffer is filled up to the requested block then the first user-facing instance is started.
                _eventBuffer = Context.ActorOf(EventBuffer<Tuple<BigInteger, BigInteger>, IEventLog>.Props(
                        log => Tuple.Create(
                            BigInteger.Parse(log.Log.BlockNumber.ToString()),
                            BigInteger.Parse(log.Log.LogIndex.ToString()))),
                    "eventBuffer");

                // Watch the buffer, it shouldn't ever die ..
                Context.Watch(_eventBuffer);
                
                // Start to fill the buffer
                Become(CatchingUp);
            });
        }

        readonly Dictionary<IActorRef, string> _catchUpQueries = new ();
        
        void CatchingUp()
        {
            Log.Info("CatchingUp");
            
            Log.Info($"Subscribing the EventBuffer to all relevant events ..");
            Context.System.EventStream.Subscribe<EventLog<SignupEventDTO>>(_eventBuffer);
            Context.System.EventStream.Subscribe<EventLog<OrganizationSignupEventDTO>>(_eventBuffer);
            Context.System.EventStream.Subscribe<EventLog<TrustEventDTO>>(_eventBuffer);
            Context.System.EventStream.Subscribe<EventLog<TransferEventDTO>>(_eventBuffer);
            
            Log.Info($"Waiting for the next block (which defines our catch-up goal) ..");
            Context.System.EventStream.Subscribe(Self, typeof(BlockClock.NextBlock));
            
            Receive<BlockClock.NextBlock>(message =>
            {
                Log.Info($"Catching up until block {message.BlockNo} (while collecting new events in the background) ..");
                Context.System.EventStream.Unsubscribe(Self, typeof(BlockClock.NextBlock));

                if (_lastBlockInDb == null)
                {
                    throw new Exception($"_lastBlockInDb == null");
                }

                // Start all catch up queries and then wait for their completion
                CatchUpQuery<SignupEventDTO>("CatchUp_Signups", message);
                CatchUpQuery<OrganizationSignupEventDTO>("CatchUp_OrganisationSignups", message);
                CatchUpQuery<TrustEventDTO>("CatchUp_Trusts", message);
                CatchUpQuery<TransferEventDTO>("CatchUp_Transfers", message);
            });

            Receive<Terminated>(message =>
            {
                // Check if the started queries completed ..
                if (_catchUpQueries.TryGetValue(message.ActorRef, out var name))
                {
                    _catchUpQueries.Remove(message.ActorRef);
                    Log.Info($"{name} completed. {_catchUpQueries.Count} to go ..");
                }

                if (_catchUpQueries.Count == 0)
                {
                    // All catch-up queries completed
                    Become(Started);
                }
            });
        }

        void Started()
        {
            Log.Info("Started");
            
            // Start the first pathfinder instance which is meant to serve user requests ..
            Log.Info($"Starting the first pathfinder ..");
            SpawnNextPathfinder();
        }

        void PartiallyAvailable()
        {
            Log.Info("PartiallyAvailable");
        }

        void Available()
        {
            Log.Info("Available");
        }
        
        #endregion

        private void CatchUpQuery<TEventDto>(string name, BlockClock.NextBlock nextBlock)
            where TEventDto : IEventDTO, new()
        {
            if (_eventBuffer == null || _lastBlockInDb == null)
            {
                throw new Exception($"{nameof(_eventBuffer)} == null || {nameof(_lastBlockInDb)} == null");
            }
            
            var props = BlockchainEventQuery<TEventDto>.Props(
                _eventBuffer,
                _rpcGateway,
                _lastBlockInDb.Value.ToHexBigInteger(),
                nextBlock.BlockNo);
            
            var actor = Context.ActorOf(props, name);
            Context.Watch(actor);
            _catchUpQueries.Add(actor, name);
            
            Log.Info($"Started '{name}'");
        }

        void SpawnNextPathfinder()
        {
            // Every pathfinder gets its own event buffer which will
            // be subscribed to the event stream once the pathfinder has 
            // caught up.
            var pathfinder = Context.ActorOf(
                Pathfinder.Pathfinder.Props(_executable, _dbFile, _rpcGateway), $"Pathfinder_{_missingPathfinders}");
            
            _pathfinders.Add(pathfinder, false);
        }
        
        public static Props Props() 
            => Akka.Actor.Props.Create<Server>();
    }
}