using System.Collections.Generic;
using Akka.Actor;
using Akka.Event;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
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
        private HexBigInteger? _latestBlock;
        
        public BlockchainEventSource(string rpcGateway)
        {
            _web3 = new Web3(rpcGateway);
            
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
                
                Log.Debug($"Fetch: Latest known block: {message}. Fetching from {_latestBlock} to {message} ..");
                
                var eventDefinition = _web3.Eth.GetEvent<TEventDTO>();
                var eventFilterInput =
                    eventDefinition.CreateFilterInput(new BlockParameter(_latestBlock), new BlockParameter(message));
                eventDefinition.GetAllChanges(eventFilterInput).PipeTo(Self);
                
                Become(Publish);
                _latestBlock = message;
            });
        }

        void Publish()
        {
            Receive<List<EventLog<TEventDTO>>>(message =>
            {
                var n = message.Count;
                var t = typeof(TEventDTO).Name;

                if (n > 0)
                {
                    Log.Info($"Publish: publishing {n} 'EventLog<{t}>' messages to the EventStream ..");
                    foreach (var eventLog in message)
                    {
                        Context.System.EventStream.Publish(eventLog);
                    }
                    Log.Debug($"Publish: publishing {n} 'EventLog<{t}>' messages to the EventStream .. Done.");
                }
                else
                {
                    Log.Debug($"Publish: No events to publish.");
                }

                Become(Wait);
            });
        }
        
        public static Props Props(string rpcGateway) 
            => Akka.Actor.Props.Create<BlockchainEventSource<TEventDTO>>(rpcGateway);
    }
}