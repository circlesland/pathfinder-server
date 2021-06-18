using System.Collections.Generic;
using Akka.Actor;
using Akka.Event;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.Hex.HexTypes;
using Nethereum.Web3;

namespace Pathfinder.Server.Actors
{
    public class BlockchainEventSource<TEventDTO> : ReceiveActor
        where TEventDTO : IEventDTO, new()
    {
        private ILoggingAdapter Log { get; } = Context.GetLogger();
        
        protected override void PreStart() => Log.Info($"BlockchainEventSource started.");
        protected override void PostStop() => Log.Info($"BlockchainEventSource stopped.");

        private readonly Web3 _web3;
        private readonly string _rpcGateway;
        
        private HexBigInteger? _latestBlock;
        private HexBigInteger? _fetchingBlock;

        protected override SupervisorStrategy SupervisorStrategy()
        {
            // Immediately stop failing child actors.
            // Simply wait for the next round and extend the next request by the amount of missed blocks.
            return new OneForOneStrategy(0, 0, ex => Directive.Stop);
        }

        public BlockchainEventSource(string rpcGateway)
        {
            _rpcGateway = rpcGateway;
            _web3 = new Web3(_rpcGateway);
            
            Context.System.EventStream.Subscribe(Self, typeof(BlockClock.NextBlock));
            Log.Info($"Subscribed to BlockClock.NextBlock.");

            Become(Wait);
        }

        void Wait()
        {
            Receive<BlockClock.NextBlock>(message =>
            {
                Log.Debug($"Wait: Triggered at block {message.BlockNo}.");
                Become(Fetch);
                
                Self.Tell(message.BlockNo);
            });

            Receive<Terminated>(message =>
            {
                Log.Debug("Query worker terminated.");
            });
        }

        void Fetch()
        {
            Receive<HexBigInteger>(message =>
            {
                if (_latestBlock == null)
                {
                    Log.Info($"Fetch: First call to Fetch(). Setting _latestBlock to 'currentBlock' - 1");
                    _latestBlock = new HexBigInteger(message.Value - 1);
                }
                
                if (message == _latestBlock)
                {
                    Log.Warning($"Fetch: The block didn't change according to the BlockClock but still Fetch was triggered. Going back to Wait.");
                    Become(Wait);
                    return;
                }
                
                Log.Debug($"Fetch: Latest known block: {message}. Fetching from {_latestBlock} to {message} ..");
                
                // The BlockchainEventQuery actor sends a List<EventLog<TEventDTO>> with the results to Self
                var worker = Context.ActorOf(BlockchainEventQuery<TEventDTO>.Props(Self, _rpcGateway, _latestBlock, message));
                Context.Watch(worker);
                
                _fetchingBlock = message;
                
                Become(Publish);
            });
        }

        void Publish()
        {
            Receive<BlockchainEventQuery<TEventDTO>.Empty>(message =>
            {
                _latestBlock = _fetchingBlock;
                _fetchingBlock = null;
                
                Log.Debug($"Publish: No events to publish.");
                Become(Wait);
            });
            
            Receive<List<EventLog<TEventDTO>>>(message =>
            {
                _latestBlock = _fetchingBlock;
                _fetchingBlock = null;
                
                var n = message.Count;
                var t = typeof(TEventDTO).Name;

                Log.Info($"Publish: publishing {n} 'EventLog<{t}>' messages to the EventStream ..");
                foreach (var eventLog in message)
                {
                    Context.System.EventStream.Publish(eventLog);
                }
                Log.Debug($"Publish: publishing {n} 'EventLog<{t}>' messages to the EventStream .. Done.");

                Become(Wait);
            });
            
            Receive<Terminated>(message =>
            {
                _fetchingBlock = null;
                
                Log.Warning($"Publish: The query worker terminated without a result. Going back to Wait().");
                Become(Wait);
            });
        }
        
        public static Props Props(string rpcGateway) 
            => Akka.Actor.Props.Create<BlockchainEventSource<TEventDTO>>(rpcGateway);
    }
}