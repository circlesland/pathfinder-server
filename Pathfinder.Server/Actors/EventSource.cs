using System.Collections.Generic;
using System.Numerics;
using Akka.Actor;
using Akka.Event;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;

namespace Pathfinder.Server.Actors
{
    public class EventSource : ReceiveActor
    {
        #region Messages

        sealed class Tick
        {
            public static Tick Instance = new();
            private Tick()
            {
            }
        }
        sealed class Tock
        {
            public readonly HexBigInteger LatestBlock;
            
            public Tock(HexBigInteger latestBlock)
            {
                LatestBlock = latestBlock;
            }
        }

        #endregion
        
        private ILoggingAdapter Log { get; } = Context.GetLogger();

        private readonly string _rpcGateway;
        private readonly int _pollingInterval;

        private ICancelable? _tick;
        private BigInteger? _lastBlock;

        private List<IActorRef> _subscribers = new();
        private List<IActorRef> _queryActorRefs = new();
        
        protected override void PreStart() {
            Log.Info($"EventSource started.");
            _tick = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(
                0, _pollingInterval, Self, Tick.Instance, Self);
        }
        
        protected override void PostStop() {
            Log.Info($"EventSource stopped.");
            _tick?.Cancel();
        }
        
        public EventSource(string rpcGateway, int pollingInterval, object[] topics, IActorRef[] subscribers)
        {
            _rpcGateway = rpcGateway;
            _pollingInterval = pollingInterval;
            _subscribers.AddRange(subscribers);

            Receive<Tick>(_ =>
            {
                var web3 = new Web3(_rpcGateway);
                web3.Eth.Blocks.GetBlockNumber.SendRequestAsync()
                    .ContinueWith(r => new Tock(r.Result))
                    .PipeTo(Self);
            });

            Receive<Tock>(message =>
            {
                if (_lastBlock == null)
                {
                    _lastBlock = message.LatestBlock;
                    Log.Info("EventSource received first Tock. Last block is {0}.", _lastBlock);
                    return;
                }
                
                Log.Info("EventSource received a Tock. Last block is {0}. Latest is {1}", _lastBlock, message.LatestBlock);

                if (_lastBlock == message.LatestBlock)
                {
                    Log.Info("No new block. Waiting one round.", _lastBlock, message.LatestBlock);
                    return;
                }
                
                foreach (var topic in topics)
                {
                    var queryActor = Context.ActorOf(EventQueryWorker.Props(new EventQueryWorker.GetEvents(
                        Self, _rpcGateway, _lastBlock.Value, message.LatestBlock, new []{ topic }, null
                    )));
                    _queryActorRefs.Add(queryActor);
                }

                _lastBlock = message.LatestBlock;
            });
            
            Receive<FilterLog>(message =>
            {
                // TODO: EventBase.DecodeAllEvents<SignupEventDTO>(new[] {log}).FirstOrDefault()
                Log.Debug("New log: {0}", message.TransactionHash);
                _subscribers.ForEach(sub => sub.Tell(message));
            });
            Receive<EventQueryWorker.RunFinished>(message =>
            {
                _queryActorRefs.Remove(Sender);
                Log.Info("Query finished. {0} query actors.", _queryActorRefs.Count);
            });
            Receive<EventQueryWorker.RunCancelled>(message =>
            {
                _queryActorRefs.Remove(Sender);
                Log.Info("Query cancelled. {0} query actors.", _queryActorRefs.Count);
            });
        }
        
        public static Props Props(string rpcGateway, int pollingInterval, object[] topics, IActorRef[] subscribers) 
            => Akka.Actor.Props.Create<EventSource>(rpcGateway, pollingInterval, topics, subscribers);
    }
}