using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Event;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.Hex.HexTypes;
using Nethereum.JsonRpc.Client;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;

namespace Pathfinder.Server.Actors.Chain
{
    public class BlockchainEventQuery<TEventDTO> : ReceiveActor
        where TEventDTO : IEventDTO, new()
    {
        public sealed class Empty
        {
        }
        
        private ILoggingAdapter Log { get; } = Context.GetLogger();

        protected override void PreStart() => Log.Debug($"BlockchainEventQuery started.");
        protected override void PostStop() => Log.Debug($"BlockchainEventQuery stopped.");

        private readonly Web3 _web3;
        private readonly HexBigInteger _from;
        private readonly HexBigInteger _to;
        private readonly IActorRef _answerTo;

        public BlockchainEventQuery(IActorRef answerTo, string rpcGateway, HexBigInteger from, HexBigInteger to)
        {
            _answerTo = answerTo;
            _web3 = new Web3(rpcGateway);
            ClientBase.ConnectionTimeout = TimeSpan.FromMinutes(2);
            _from = from;
            _to = to;

            Become(Fetch);

            Self.Tell("trigger");
        }

        void Fetch()
        {
            Receive<string>(message =>
            {
                Log.Debug($"Fetching from {_from} to {_to} ..");

                var eventDefinition = _web3.Eth.GetEvent<TEventDTO>();
                var eventFilterInput =
                    eventDefinition.CreateFilterInput(new BlockParameter(_from), new BlockParameter(_to));

                Become(Publish);
               
                eventDefinition.GetAllChanges(eventFilterInput).PipeTo(Self);
            });
        }
        
        void Publish()
        {
            Receive<Status.Failure>(message =>
            {
                Log.Error(message.Cause, "Fetch failed.");
                throw message.Cause;
            });

            Receive<List<EventLog<TEventDTO>>>(message =>
            {
                var n = message.Count;
                var t = typeof(TEventDTO).Name;

                if (n > 0)
                {
                    Log.Info($"Publish: Sending all {n} items of the received 'List<EventLog<{t}>>' to '{_answerTo}' ..");
                    foreach (var eventLog in message)
                    {
                        _answerTo.Tell(eventLog);   
                    }
                }
                else
                {
                    Log.Debug($"Publish: No events to publish.");
                    _answerTo.Tell(new Empty());
                }
                
                Context.Stop(Self);
            });
        }

        public static Props Props(IActorRef answerTo, string rpcGateway, HexBigInteger from, HexBigInteger to)
            => Akka.Actor.Props.Create<BlockchainEventQuery<TEventDTO>>(answerTo, rpcGateway, from, to);
    }
}