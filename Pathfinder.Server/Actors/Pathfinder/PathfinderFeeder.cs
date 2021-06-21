using System;
using System.Numerics;
using Akka.Actor;
using Akka.Event;
using Nethereum.Contracts;
using Nethereum.Hex.HexTypes;
using Pathfinder.Server.Actors.Chain;
using Pathfinder.Server.Actors.Feed;
using Pathfinder.Server.Actors.System;
using Pathfinder.Server.contracts;
using Buffer = Pathfinder.Server.Actors.MessageContracts.Buffer;

namespace Pathfinder.Server.Actors.Pathfinder
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
        
        // private IActorRef? _feed;
        private IActorRef? _feedConsumer;

        public PathfinderFeeder( 
            HexBigInteger? startAtBlock = null, 
            string? rpcGateway = null)
        {
            _startAtBlock = startAtBlock;
            _rpcGateway = rpcGateway;

            // The buffer in which all events are kept until applied
            _eventBuffer = Context.ActorOf(EventBuffer<Tuple<BigInteger, BigInteger>, IEventLog>.Props(
                    log => Tuple.Create(
                        BigInteger.Parse(log.Log.BlockNumber.ToString()),
                        BigInteger.Parse(log.Log.LogIndex.ToString()))),
                "eventBuffer");

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

                Log.Info(
                    $"CatchUp: Need to catch up {message.BlockNo - BigInteger.Parse(_startAtBlock.ToString())} blocks ..");

                Context.System.EventStream.Unsubscribe(Self, typeof(BlockClock.NextBlock));
                Log.Info("Unsubscribed from 'BlockClock.NextBlock' events on the system EventStream.");

                Log.Info($"CatchUp: Starting all query workers (from: {_startAtBlock},to: {message.BlockNo}) ..");
                var signupQuery = Context.ActorOf(
                    BlockchainEventQuery<SignupEventDTO>.Props(_eventBuffer, _rpcGateway, _startAtBlock,
                        message.BlockNo),
                    "CatchUp_querySignups");
                Context.Watch(signupQuery);
                running++;

                var organisationSignupQuery = Context.ActorOf(
                    BlockchainEventQuery<OrganizationSignupEventDTO>.Props(_eventBuffer, _rpcGateway, _startAtBlock,
                        message.BlockNo),
                    "CatchUp_queryOrganisationSignups");
                Context.Watch(organisationSignupQuery);
                running++;

                var trustQuery = Context.ActorOf(
                    BlockchainEventQuery<TrustEventDTO>.Props(_eventBuffer, _rpcGateway, _startAtBlock,
                        message.BlockNo),
                    "CatchUp_queryTrusts");
                Context.Watch(trustQuery);
                running++;

                var transfersQuery = Context.ActorOf(
                    BlockchainEventQuery<TransferEventDTO>.Props(_eventBuffer, _rpcGateway, _startAtBlock,
                        message.BlockNo),
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
                _eventBuffer.Tell(new EventBuffer<Tuple<BigInteger, BigInteger>, IEventLog>.GetStats());
            });

            Receive<EventBuffer<Tuple<BigInteger, BigInteger>, IEventLog>.GetStatsResult>(message =>
            {
                Log.Info(
                    $"CatchUp finished. Total events: {message.Items}, Min key: {message.MinKey}, Max key: {message.MaxKey}");

                // All new events up to now should be buffered
                Context.Parent.Tell(new CaughtUp());
                Become(Ready);
            });
        }
        
        IActorRef? _feedingTo = null;

        void Ready()
        {
            
            Receive<Buffer.Feed>(message =>
            {
                // Prepare the pathfinder for bulk insert
                _feedingTo = message.To;
                message.To.Tell(new PathfinderProcess.Call(RpcMessage.DelayEdgeUpdates(), Self));
            });
            
            Receive<PathfinderProcess.Return>(message =>
            {
                if (_feedingTo == null) { throw new Exception("WTF?"); }
                
                _feedConsumer = Context.ActorOf(Consumer<IEventLog>.Props(
                    async (handshake, payload) =>
                    {
                        var rpcCall = EventToCall(payload);
                        var rpcCallReturn = await _feedingTo.Ask<PathfinderProcess.Return>(rpcCall);
                        
                        return true;
                    }, FeedMode.Finite));
                
                _eventBuffer.Tell(new Buffer.Feed(_feedConsumer, FeedMode.Finite)); // TODO: Intentionally broken.. Continue here
                Context.Watch(_feedConsumer);

                Become(Feeding);
            });
        }

        void Feeding()
        {
            Receive<Terminated>(message =>
            {
                if (_feedingTo == null) { throw new Exception("WTF?"); }
                
                // End the bulk insert mode
                _feedingTo.Tell(new PathfinderProcess.Call(RpcMessage.PerformEdgeUpdates(), Self));
            });
            
            Receive<PathfinderProcess.Return>(message =>
            {
                Log.Info($"Feeding finished. PathfinderProcess ({_feedingTo}) should now be up to date.");
                _feedingTo = null;
                Become(Ready);
            });
        }

        private PathfinderProcess.Call EventToCall(object message, IActorRef? answerTo = null)
        {
            switch (message)
            {
                case EventLog<SignupEventDTO> signup
                    when (signup.Event.User != null && signup.Event.Token != null):
                    return new PathfinderProcess.Call(RpcMessage.Signup(signup.Event.User, signup.Event.Token), answerTo);
                case EventLog<OrganizationSignupEventDTO> organisationSignup
                    when (organisationSignup.Event.Organization != null):
                    return new PathfinderProcess.Call(
                        RpcMessage.OrganizationSignup(organisationSignup.Event.Organization), answerTo);
                case EventLog<TrustEventDTO> trust
                    when (trust.Event.CanSendTo != null && trust.Event.User != null):
                    return new PathfinderProcess.Call(
                        RpcMessage.Trust(trust.Event.CanSendTo, trust.Event.User,
                            Int32.Parse(trust.Event.Limit.ToString())),
                        answerTo);
                case EventLog<TransferEventDTO> transfer:
                    return new PathfinderProcess.Call(
                        RpcMessage.Transfer(transfer.Log.Address, transfer.Event.From, transfer.Event.To,
                            transfer.Event.Value.ToString()), answerTo);
                default:
                    throw new ArgumentException($"Unknown type '{message.GetType().Name}'", nameof(message));
            }
        }

        public static Props Props(HexBigInteger? startAtBlock = null, string? rpcGateway = null)
            => Akka.Actor.Props.Create<PathfinderFeeder>(startAtBlock, rpcGateway);
    }
}