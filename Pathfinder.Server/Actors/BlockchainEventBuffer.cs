using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Akka.Actor;
using Akka.Event;
using Nethereum.Contracts;
using Nethereum.RPC.Eth.DTOs;
using Pathfinder.Server.contracts;

namespace Pathfinder.Server.Actors
{
    public class BlockchainEventBuffer : UntypedActor
    {
        #region Messages

        public sealed class GetStats
        {
        }
        
        public sealed class GetStatsResult
        {
            public readonly Tuple<BigInteger, BigInteger> MinKey;
            public readonly Tuple<BigInteger, BigInteger> MaxKey;
            public readonly long Items;

            public GetStatsResult(
                Tuple<BigInteger, BigInteger> minKey,
                Tuple<BigInteger, BigInteger> maxKey,
                long items)
            {
                MinKey = minKey;
                MaxKey = maxKey;
                Items = items;
            }
        }

        #endregion
        
        private ILoggingAdapter Log { get; } = Context.GetLogger();
        
        protected override void PreStart() => Log.Info($"EventBuffer started.");
        protected override void PostStop() => Log.Info($"EventBuffer stopped.");
        
        private SortedDictionary<Tuple<BigInteger, BigInteger>, IEventLog> _buffer = new();
        
        public BlockchainEventBuffer()
        {
        }

        private Tuple<BigInteger, BigInteger> GetKey(FilterLog log)
        {
            var blockNo = BigInteger.Parse(log.BlockNumber.ToString());
            var logIdx = BigInteger.Parse(log.LogIndex.ToString());
            
            return Tuple.Create(blockNo, logIdx);
        }

        protected override void OnReceive(object message)
        {   
            switch (message)
            {
                // Check if the message is a command
                case GetStats:
                    if (_buffer.Count == 0)
                    {
                        Sender.Tell(new GetStatsResult(
                            Tuple.Create(BigInteger.MinusOne, BigInteger.MinusOne),
                            Tuple.Create(BigInteger.MinusOne, BigInteger.MinusOne),
                            0));
                        
                        return;
                    }
                    Sender.Tell(new GetStatsResult(
                        _buffer.First().Key,
                        _buffer.Last().Key,
                        _buffer.Count));
                    break;
                case Buffer.Unroll unroll:
                    foreach (var eventLog in _buffer)
                    {
                        unroll.To.Tell(eventLog.Value);
                    }
                    Log.Info($"{Sender} unrolled the contents of the buffer ({_buffer.Count} items) to {unroll.To}.");
                    _buffer.Clear();
                    break;
                case Buffer.Publish:
                    foreach (var eventLog in _buffer)
                    {
                        Context.System.EventStream.Publish(eventLog.Value);
                    }
                    Log.Info($"{Sender} published the contents of the buffer ({_buffer.Count} items) to the EventStream.");
                    _buffer.Clear();
                    break;

                // Check if the message is a collectible event
                case IEventLog log:
                    var key = GetKey(log.Log);
                    if (_buffer.ContainsKey(key))
                    {
                        var typeName = message.GetType().GetGenericArguments()[0].Name;
                        Log.Warning($"Received duplicate '{typeName}' event with key '{key}' from '{Sender}'. Ignoring it ..");
                        return;
                    }
                    
                    switch (log)
                    {
                        case EventLog<SignupEventDTO> signupEvent:
                            _buffer.Add(key, signupEvent);
                            break;
                        case EventLog<OrganizationSignupEventDTO> organizationSignup:
                            _buffer.Add(key, organizationSignup);
                            break;
                        case EventLog<TrustEventDTO> trustEvent:
                            _buffer.Add(key, trustEvent);
                            break;
                        case EventLog<TransferEventDTO> transferEvent:
                            _buffer.Add(key, transferEvent);
                            break;
                    }
                    break;
            }
        }
        
        public static Props Props()
            => Akka.Actor.Props.Create<BlockchainEventBuffer>();
    }
}