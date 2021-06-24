using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Akka.Actor;
using Akka.Event;
using Akka.Util.Internal;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.Hex.HexTypes;
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

        private IActorRef? _catchUpBuffer;
        
        private readonly Dictionary<IActorRef, string> _catchUpQueries = new ();
        private readonly Dictionary<IActorRef, IActorRef> _pathfinderProcessAndBuffer = new();
        private readonly Dictionary<IActorRef, IActorRef> _initializerAndPathfinderProcess = new();
        private readonly Dictionary<IActorRef, IActorRef> _feederAndPathfinderProcess = new();
        private readonly Dictionary<IActorRef, bool> _pathfinderAvailability = new();
        private readonly Dictionary<IActorRef, BigInteger> _pathfinderLastUpdateAtBlock = new();
        private readonly Dictionary<IActorRef, IActorRef> _updateFeederAndPathfinder = new();

        private int _targetPathfinderCount = 8;
        private int _maxParalellUpdates = 1;
        private int _spawningPathfinders = 0;
        
        private BigInteger? _lastBlockInDb;
        
        string _executable = "/home/daniel/src/pathfinder/build/pathfinder";
        string _rpcGateway = "https://rpc.circles.land";
        string _dbFile = "/home/daniel/src/circles-world/PathfinderServer/Pathfinder.Server/db.dat";
        
        protected override SupervisorStrategy SupervisorStrategy()
        {
            // If anything within the PathfinderFeeder dies, let the whole thing die
            return new OneForOneStrategy(2, 5000, ex =>
            {
                if (Sender.Equals(_catchUpBuffer))
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

        private readonly IActorRef _nancyAdapterActor;
        
        public Server(IActorRef nancyAdapterActor)
        {
            if (_targetPathfinderCount <= _maxParalellUpdates)
            {
                throw new Exception($"The target pathfinder count must always be larger than the max. parallel update count.");
            }
            
            _nancyAdapterActor = nancyAdapterActor;
            
            Context.ActorOf(RealTimeClock.Props(), "RealTimeClock");
            Context.ActorOf(BlockClock.Props(_rpcGateway), "BlockClock");
            
            Context.ActorOf(BlockchainEventSource<SignupEventDTO>.Props(_rpcGateway), "SignupEventSource");
            Context.ActorOf(BlockchainEventSource<OrganizationSignupEventDTO>.Props(_rpcGateway), "OrganizationSignupEventSource");
            Context.ActorOf(BlockchainEventSource<TrustEventDTO>.Props(_rpcGateway), "TrustEventSource");
            Context.ActorOf(BlockchainEventSource<TransferEventDTO>.Props(_rpcGateway), "TransferEventSource");
            
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
                if (message.ActorRef.Equals(_catchUpBuffer))
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
                _catchUpBuffer = Context.ActorOf(EventBuffer<Tuple<BigInteger, BigInteger>, IEventLog>.Props(
                        log => Tuple.Create(
                            BigInteger.Parse(log.Log.BlockNumber.ToString()),
                            BigInteger.Parse(log.Log.LogIndex.ToString()))),
                    "_catchUpBuffer");

                // Watch the buffer, it shouldn't ever die ..
                Context.Watch(_catchUpBuffer);
                
                // Start to fill the buffer
                Become(CatchingUp);
            });
        }

        void CatchingUp()
        {
            Log.Info("CatchingUp");
            
            Log.Info($"Subscribing the EventBuffer to all relevant events ..");
            Context.System.EventStream.Subscribe<EventLog<SignupEventDTO>>(_catchUpBuffer);
            Context.System.EventStream.Subscribe<EventLog<OrganizationSignupEventDTO>>(_catchUpBuffer);
            Context.System.EventStream.Subscribe<EventLog<TrustEventDTO>>(_catchUpBuffer);
            Context.System.EventStream.Subscribe<EventLog<TransferEventDTO>>(_catchUpBuffer);
            
            Log.Info($"Waiting for the next block (which defines our catch-up goal) ..");
            Context.System.EventStream.Subscribe(Self, typeof(BlockClock.NextBlock));

            var waitForQueries = false;
            
            Receive<BlockClock.NextBlock>(message =>
            {
                Log.Info($"Catching up until block {message.BlockNo} (while collecting new events in the background) ..");
                Context.System.EventStream.Unsubscribe(Self, typeof(BlockClock.NextBlock));

                if (_lastBlockInDb == null)
                {
                    throw new Exception($"_lastBlockInDb == null");
                }

                // Start all catch up queries and then wait for their completion
                var q = CatchUpQuery<SignupEventDTO>("CatchUp_Signups", message);
                Context.Watch(q);
                
                q = CatchUpQuery<OrganizationSignupEventDTO>("CatchUp_OrganisationSignups", message);
                Context.Watch(q);
                
                q = CatchUpQuery<TrustEventDTO>("CatchUp_Trusts", message);
                Context.Watch(q);
                
                q = CatchUpQuery<TransferEventDTO>("CatchUp_Transfers", message);
                Context.Watch(q);

                waitForQueries = true;
            });

            Receive<Terminated>(message =>
            {
                if (!waitForQueries) return;
                
                // Check if the started queries completed ..
                if (_catchUpQueries.TryGetValue(message.ActorRef, out var name))
                {
                    _catchUpQueries.Remove(message.ActorRef);
                    Log.Info($"{name} completed. {_catchUpQueries.Count} to go ..");
                }

                if (_catchUpQueries.Count == 0)
                {
                    // All catch-up queries completed
                    Context.System.EventStream.Subscribe<RealTimeClock.SecondElapsed>(Self);
                    Become(Started);
                }
            });
        }
        
        void Started()
        {
            Context.System.EventStream.Subscribe(Self, typeof(BlockClock.NextBlock));
            Log.Info("Started");
            
            Receive<RealTimeClock.SecondElapsed>(OnSecondElapsed);
            Receive<Terminated>(OnTerminated);
        }

        void PartiallyAvailable()
        {
            Log.Info("PartiallyAvailable");
            
            Receive<BlockClock.NextBlock>(OnNextBlock);
            Receive<RealTimeClock.SecondElapsed>(OnSecondElapsed);
            Receive<Terminated>(OnTerminated);
            Receive<PathfinderProcess.Call>(OnCall);
            Receive<PathfinderProcess.Return>(OnReturn);
        }

        void Available()
        {
            Log.Info("Available");
            
            Context.Stop(_catchUpBuffer);
            _catchUpBuffer = null;
            
            Receive<BlockClock.NextBlock>(OnNextBlock);
            Receive<RealTimeClock.SecondElapsed>(OnSecondElapsed);
            Receive<Terminated>(OnTerminated);
            Receive<PathfinderProcess.Call>(OnCall);
            Receive<PathfinderProcess.Return>(OnReturn);
        }
        
        #endregion

        private void OnCall(PathfinderProcess.Call message)
        {
            // Find an available pathfinder
            var availablePathfinder = _pathfinderAvailability
                .Where(o => o.Value)
                .Select(o => o.Key)
                .FirstOrDefault();

            if (availablePathfinder == null)
            {
                _nancyAdapterActor.Tell(new PathfinderProcess.Return(message.RpcMessage.Id, "{\"error\":\"temporarily unavailable\"}"));
            }
            else
            {
                _pathfinderAvailability[availablePathfinder] = false;
                availablePathfinder.Tell(new PathfinderProcess.Call(message.RpcMessage, Self));
            }
        }

        private void OnReturn(PathfinderProcess.Return message)
        {
            _pathfinderAvailability[Sender] = true;
            _nancyAdapterActor.Tell(message);
        }

        private IActorRef CatchUpQuery<TEventDto>(string name, BlockClock.NextBlock nextBlock)
            where TEventDto : IEventDTO, new()
        {
            if (_catchUpBuffer == null || _lastBlockInDb == null)
            {
                throw new Exception($"{nameof(_catchUpBuffer)} == null || {nameof(_lastBlockInDb)} == null");
            }
            
            var props = BlockchainEventQuery<TEventDto>.Props(
                _catchUpBuffer,
                _rpcGateway,
                _lastBlockInDb.Value.ToHexBigInteger(),
                nextBlock.BlockNo);
            
            var actor = Context.ActorOf(props, name);
            _catchUpQueries.Add(actor, name);
            
            Log.Info($"Started '{name}'");
            return actor;
        }

        private void OnNextBlock (BlockClock.NextBlock message)
        {
            // Set the last update block to 'current' if none is currently present
            _pathfinderAvailability.ForEach(pathfinder =>
            {
                if (!_pathfinderLastUpdateAtBlock.ContainsKey(pathfinder.Key))
                {
                    _pathfinderLastUpdateAtBlock.Add(pathfinder.Key, message.BlockNo);
                }
            });
            
            // Find the oldest currently available pathfinders and update them
            var oldestAvailable = _pathfinderAvailability
                .Where(o => o.Value 
                            && _pathfinderLastUpdateAtBlock.ContainsKey(o.Key))
                .Select(o => new
                {
                    Key = o.Key,
                    LastUpdate = _pathfinderLastUpdateAtBlock[o.Key]
                })
                .OrderBy(o => o.LastUpdate)
                .Take(_maxParalellUpdates)
                .ToArray();

            if (oldestAvailable.Length > 0)
            {
                oldestAvailable.ForEach(o =>
                {
                    Log.Info($"Updating {o.Key} ..");
                
                    // Set unavailable
                    _pathfinderAvailability[o.Key] = false;
                
                    // Created a feeder 
                    var buffer = _pathfinderProcessAndBuffer[o.Key];
                    var feeder = Context.ActorOf(Feeder.Props(buffer, o.Key, true));
                    Context.Watch(feeder);
                
                    // Add to the list of updating pathfinders
                    _updateFeederAndPathfinder.Add(feeder, o.Key);
                });
            }
        }

        private void OnSecondElapsed(RealTimeClock.SecondElapsed _)
        {
            if (_catchUpBuffer == null) return; // We already reached "Available"
            // TODO: Handle the case when single pathfinder instances die..
            
            // Spawn new pathfinders as long as some are missing
            if (_pathfinderAvailability.Count - _targetPathfinderCount == 0 || _spawningPathfinders > 0)
            {
                return;
            }

            SpawnPathfinder();
            _spawningPathfinders++;
        }

        /// <summary>
        /// Spawns a new pathfinder process and updates it up to
        /// the currently known block.
        /// The first initialization will be performed by the <see cref="PathfinderInitializer"/>.
        /// This step loads the db from a file.
        /// The next step is to catch up until the current block is reached.
        /// This is done by feeding the _catchUpBuffer to the new pathfinder (non-consuming).
        /// When the pathfinder caught up then the next events must be fed
        /// from its own buffer (_pathfinderProcessAndBuffer - consuming) 
        /// </summary>
        private void SpawnPathfinder()
        {
            var pathfinderName = $"Pathfinder_{_pathfinderProcessAndBuffer.Count + 1}";
            Log.Info($"Starting {pathfinderName} ..");

            var pathfinderProcess = Context.ActorOf(
                PathfinderProcess.Props(_executable),
                pathfinderName);

            var bufferName = $"{pathfinderName}_Buffer";
            Log.Info($"Starting {bufferName} ..");

            var buffer = Context.ActorOf(EventBuffer<Tuple<BigInteger, BigInteger>, IEventLog>.Props(
                    log => Tuple.Create(
                        BigInteger.Parse(log.Log.BlockNumber.ToString()),
                        BigInteger.Parse(log.Log.LogIndex.ToString()))),
                bufferName);

            _pathfinderProcessAndBuffer.Add(pathfinderProcess, buffer);

            var initializerName = $"{pathfinderName}_Initializer";
            Log.Info($"Starting {initializerName} ..");

            var initializer = Context.ActorOf(
                PathfinderInitializer.Props(pathfinderProcess, _dbFile),
                initializerName);

            Context.Watch(initializer);
            _initializerAndPathfinderProcess.Add(initializer, pathfinderProcess);
        }

        private void OnTerminated(Terminated message)
        {
            if (_initializerAndPathfinderProcess.TryGetValue(message.ActorRef, out var pathfinderProcessRef))
            {
                if (_catchUpBuffer == null)
                {
                    throw new Exception($"'{nameof(_catchUpBuffer)} == null' in '{nameof(Started)}'");
                }
                Log.Info($"{pathfinderProcessRef} completely loaded the database from '{_dbFile}'.");
                _initializerAndPathfinderProcess.Remove(message.ActorRef);

                var pathfinderBuffer = _pathfinderProcessAndBuffer[pathfinderProcessRef];
                Log.Info($"Subscribing {pathfinderBuffer} to {nameof(EventLog<SignupEventDTO>)} events ..");
                Context.System.EventStream.Subscribe<EventLog<SignupEventDTO>>(pathfinderBuffer);

                Log.Info(
                    $"Subscribing {pathfinderBuffer} to {nameof(EventLog<OrganizationSignupEventDTO>)} events ..");
                Context.System.EventStream.Subscribe<EventLog<OrganizationSignupEventDTO>>(pathfinderBuffer);

                Log.Info($"Subscribing {pathfinderBuffer} to {nameof(EventLog<TrustEventDTO>)} events ..");
                Context.System.EventStream.Subscribe<EventLog<TrustEventDTO>>(pathfinderBuffer);

                Log.Info($"Subscribing {pathfinderBuffer} to {nameof(EventLog<TransferEventDTO>)} events ..");
                Context.System.EventStream.Subscribe<EventLog<TransferEventDTO>>(pathfinderBuffer);

                Log.Info($"Feeding the missing delta between DB and NOW to {pathfinderProcessRef} ..");
                var feeder = Context.ActorOf(Feeder.Props(
                    _catchUpBuffer,
                    pathfinderProcessRef,
                    false));
                Context.Watch(feeder);

                _feederAndPathfinderProcess.Add(feeder, pathfinderProcessRef);
            }
            else if (_feederAndPathfinderProcess.TryGetValue(message.ActorRef, out pathfinderProcessRef))
            {
                Log.Info($"{message.ActorRef} completely fed the feed contents to {pathfinderProcessRef}'.");
                var buffer = _pathfinderProcessAndBuffer[pathfinderProcessRef];
                _feederAndPathfinderProcess.Remove(message.ActorRef);

                Log.Info($"{pathfinderProcessRef} caught up. Setting '_pathfinderAvailability' to 'true' and " +
                         $"using {buffer} for further updates.");

                _pathfinderAvailability.Add(pathfinderProcessRef, true);
                _spawningPathfinders--;

                if (_pathfinderAvailability.Count == _targetPathfinderCount)
                {
                    Become(Available);
                }
                else
                {
                    Become(PartiallyAvailable);
                }
            }
            else if (_updateFeederAndPathfinder.TryGetValue(message.ActorRef, out pathfinderProcessRef))
            {
                Log.Info($"{pathfinderProcessRef} finished updating.");
                _updateFeederAndPathfinder.Remove(message.ActorRef);
                _pathfinderLastUpdateAtBlock.Remove(pathfinderProcessRef);
                
                // Set pathfinder to available again
                _pathfinderAvailability[pathfinderProcessRef] = true;
            }
        }
        
        public static Props Props(IActorRef nancyAdapterActor) 
            => Akka.Actor.Props.Create<Server>(nancyAdapterActor);
    }
}