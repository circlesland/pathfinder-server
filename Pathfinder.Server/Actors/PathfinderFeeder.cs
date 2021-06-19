using System;
using System.Numerics;
using Akka.Actor;
using Akka.Event;
using Nethereum.Contracts;
using Nethereum.Hex.HexTypes;
using Pathfinder.Server.contracts;

namespace Pathfinder.Server.Actors
{
    public class PathfinderFeeder : ReceiveActor
    {
        #region Messages

        public sealed class CaughtUp
        {
        }

        #endregion
        
        private ILoggingAdapter Log { get; } = Context.GetLogger();
        
        protected override void PreStart() => Log.Info("PathfinderFeeder started.");
        protected override void PostStop() => Log.Info("PathfinderFeeder stopped");
        
        private readonly IActorRef _eventBuffer;
        private readonly HexBigInteger? _startAtBlock;
        private readonly string? _rpcGateway;
        
        public PathfinderFeeder(HexBigInteger? startAtBlock = null, string? rpcGateway = null)
        {
            _startAtBlock = startAtBlock;
            _rpcGateway = rpcGateway;
            
            // The buffer in which all events are kept until applied
            _eventBuffer = Context.ActorOf(BlockchainEventBuffer.Props(), "eventBuffer");
            
            // Subscribe to all relevant events
            Context.System.EventStream.Subscribe<EventLog<SignupEventDTO>>(_eventBuffer);
            Context.System.EventStream.Subscribe<EventLog<OrganizationSignupEventDTO>>(_eventBuffer);
            Context.System.EventStream.Subscribe<EventLog<TrustEventDTO>>(_eventBuffer);
            Context.System.EventStream.Subscribe<EventLog<TransferEventDTO>>(_eventBuffer);

            if (startAtBlock != null)
            {
                Become(CatchUp);
            }
            else
            {
                Become(Ready);
            }
        }

        void CatchUp()
        {
            Context.System.EventStream.Subscribe(Self, typeof(BlockClock.NextBlock));
            Log.Info("Subscribed to 'BlockClock.NextBlock' events on the system EventStream.");

            var running = 0;
            
            Receive<BlockClock.NextBlock>(message =>
            {
                if (_startAtBlock == null || _rpcGateway == null)
                {
                    throw new Exception("_startAtBlock || _rpcGateway are null");
                }
                
                Log.Info($"CatchUp: Need to catch up {message.BlockNo - BigInteger.Parse(_startAtBlock.ToString())} blocks ..");
                
                Context.System.EventStream.Unsubscribe(Self, typeof(BlockClock.NextBlock));
                Log.Info("Unsubscribed from 'BlockClock.NextBlock' events on the system EventStream.");
            
                Log.Info($"CatchUp: Starting all query workers (from: {_startAtBlock},to: {message.BlockNo}) ..");
                var signupQuery = Context.ActorOf(
                    BlockchainEventQuery<SignupEventDTO>.Props(_eventBuffer, _rpcGateway, _startAtBlock, message.BlockNo),
                    "CatchUp_querySignups");
                Context.Watch(signupQuery);
                running++;
                
                var organisationSignupQuery = Context.ActorOf(
                    BlockchainEventQuery<OrganizationSignupEventDTO>.Props(_eventBuffer, _rpcGateway, _startAtBlock, message.BlockNo),
                    "CatchUp_queryOrganisationSignups");
                Context.Watch(organisationSignupQuery);
                running++;
                
                var trustQuery = Context.ActorOf(
                    BlockchainEventQuery<TrustEventDTO>.Props(_eventBuffer, _rpcGateway, _startAtBlock, message.BlockNo),
                    "CatchUp_queryTrusts");
                Context.Watch(trustQuery);
                running++;
                
                var transfersQuery = Context.ActorOf(
                    BlockchainEventQuery<TransferEventDTO>.Props(_eventBuffer, _rpcGateway, _startAtBlock, message.BlockNo),
                    "CatchUp_queryTransfers");
                Context.Watch(transfersQuery);
                running++;
            });
            
            Receive<Terminated>(message =>
            {
                running--;
                Log.Debug($"CatchUp: Query worker '{message.ActorRef}' terminated. {running} to go ..");
                        
                if (running > 0)
                {
                    return;
                }
                
                Log.Info("CatchUp: All query workers terminated. Getting the buffer stats ..");
                _eventBuffer.Tell(new BlockchainEventBuffer.GetStats());
            });

            Receive<BlockchainEventBuffer.GetStatsResult>(message =>
            {
                Log.Info($"CatchUp finished. Total events: {message.Items}, Min key: {message.MinKey}, Max key: {message.MaxKey}"); 
                
                // All new events up to now should be buffered
                Context.Parent.Tell(new CaughtUp());
                Become(Ready);
            });
        }

        private IActorRef? _unrollTo;
        
        void Ready()
        {
            Receive<Buffer.Unroll>(message =>
            {
                _unrollTo = message.To;
                Become(Unrolling);
                _eventBuffer.Tell(new Buffer.Unroll(Self));
            });
        }

        void Unrolling()
        {
            ReceiveAny(message =>
            {
                switch (message)
                {
                    case EventLog<SignupEventDTO> signup:
                        _unrollTo.Tell(new PathfinderProcess.Call(RpcMessage.Signup(signup.Event.User, signup.Event.Token), Self));
                        break;
                    case EventLog<OrganizationSignupEventDTO> organisationSignup:
                        _unrollTo.Tell(new PathfinderProcess.Call(RpcMessage.OrganizationSignup(organisationSignup.Event.Organization), Self));
                        break;
                    case EventLog<TrustEventDTO> trust:
                        _unrollTo.Tell(new PathfinderProcess.Call(RpcMessage.Trust(trust.Event.CanSendTo,trust.Event.User, Int32.Parse(trust.Event.Limit.ToString())), Self));
                        break;
                    case EventLog<TransferEventDTO> transfer:
                        _unrollTo.Tell(new PathfinderProcess.Call(RpcMessage.Transfer(transfer.Log.Address, transfer.Event.From, transfer.Event.To, transfer.Event.Value.ToString()), Self));
                        break;
                    case PathfinderProcess.Return returnMessage:
                        
                        break;
                }
            });
        }
        
        public static Props Props(HexBigInteger? startAtBlock = null, string? rpcGateway = null) 
            => Akka.Actor.Props.Create<PathfinderFeeder>(startAtBlock, rpcGateway);
    }
}