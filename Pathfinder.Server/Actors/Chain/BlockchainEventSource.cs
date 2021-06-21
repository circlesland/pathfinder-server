using System;
using System.Numerics;
using Akka.Actor;
using Akka.Event;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.Hex.HexTypes;
using Nethereum.Web3;
using Buffer = Pathfinder.Server.Actors.MessageContracts.Buffer;

namespace Pathfinder.Server.Actors.Chain
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

        private readonly IActorRef _eventBuffer;

        protected override SupervisorStrategy SupervisorStrategy()
        {
            return new OneForOneStrategy(0, 0, ex =>
            {
                if (!Sender.Equals(_eventBuffer))
                {
                    // Immediately stop all other failing child actors and simply wait for the next round.
                    return Directive.Stop;
                }
             
                // Failing _eventBuffers are fatal.   
                Log.Error(ex, $"The {nameof(EventBuffer<Tuple<BigInteger,BigInteger>, IEventLog>)} of this {GetType().Name} failed.");
                return Directive.Escalate;
            });
        }

        public BlockchainEventSource(string rpcGateway)
        {
            _rpcGateway = rpcGateway;
            _web3 = new Web3(_rpcGateway);

            _eventBuffer = Context.ActorOf(EventBuffer<Tuple<BigInteger,BigInteger>, IEventLog>.Props(
                    log => Tuple.Create(
                        BigInteger.Parse(log.Log.BlockNumber.ToString()), 
                        BigInteger.Parse(log.Log.LogIndex.ToString()))),
                "eventBuffer");
            
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
                
                // The BlockchainEventQuery actor sends a List<EventLog<TEventDTO>> with the results to Self
                var nextBlock = BigInteger.Parse(_latestBlock.ToString()) + 1;
                Log.Debug($"Fetch: From {nextBlock} to {message} ..");
                
                var worker = Context.ActorOf(
                    BlockchainEventQuery<TEventDTO>.Props(_eventBuffer, _rpcGateway, nextBlock.ToHexBigInteger(), message));
                
                Context.Watch(worker);
                _fetchingBlock = message;
                
                Become(Publish);
            });
        }

        void Publish()
        {
            ReceiveAsync<Terminated>(async message =>
            {
                var stats = await _eventBuffer.Ask<EventBuffer<Tuple<BigInteger,BigInteger>, IEventLog>.GetStatsResult>(
                    new EventBuffer<Tuple<BigInteger,BigInteger>, IEventLog>.GetStats());
                
                if (stats.Items > 0)
                {
                    Log.Info($"Publish: The query worker received {stats.Items} events in the range from {stats.MinKey} to {stats.MaxKey}. Publishing the events and going back to Wait() ..");
                    _latestBlock = _fetchingBlock;
                    
                    _eventBuffer.Tell(new Buffer.Publish());
                }
                else
                {
                    Log.Debug($"Publish: The query worker terminated without a result. Going back to Wait().");
                }
                
                _fetchingBlock = null;
                Become(Wait);
            });
        }
        
        public static Props Props(string rpcGateway) 
            => Akka.Actor.Props.Create<BlockchainEventSource<TEventDTO>>(rpcGateway);
    }
}