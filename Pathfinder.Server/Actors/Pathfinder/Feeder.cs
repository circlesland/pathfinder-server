using System;
using Akka.Actor;
using Akka.Event;
using Nethereum.Contracts;
using Pathfinder.Server.Actors.Feed;
using Pathfinder.Server.contracts;
using Buffer = Pathfinder.Server.Actors.MessageContracts.Buffer;

namespace Pathfinder.Server.Actors.Pathfinder
{
    /// <summary>
    /// Feeds an <see cref="EventBuffer{TKey,TValue}"/> to a <see cref="PathfinderProcess"/>.
    /// </summary>
    public class Feeder : ReceiveActor
    {
        private ILoggingAdapter Log { get; } = Context.GetLogger();

        protected override void PreStart() => Log.Info("PathfinderFeeder started.");
        protected override void PostStop() => Log.Info("PathfinderFeeder stopped");

        private readonly IActorRef _feedEventBuffer;
        private readonly IActorRef _toPathfinderProcess;
        private readonly bool _consumeFeed;
        
        private IActorRef? _feedConsumer;

        protected override SupervisorStrategy SupervisorStrategy()
        {
            // If anything under the Feeder dies, the whole thing dies.
            return new AllForOneStrategy(0, 0, ex => Directive.Escalate);
        }

        public Feeder(IActorRef feedEventBuffer, IActorRef toPathfinderProcess, bool consumeFeed)
        {
            _feedEventBuffer = feedEventBuffer;
            _toPathfinderProcess = toPathfinderProcess;
            _consumeFeed = consumeFeed;
            
            _toPathfinderProcess.Tell(new PathfinderProcess.Call(RpcMessage.DelayEdgeUpdates(), Self));
            Become(Starting);
        }

        void Starting()
        {
            Receive<PathfinderProcess.Return>(message =>
            {
                _feedConsumer = Context.ActorOf(Consumer<IEventLog>.Props(
                    async (handshake, payload) =>
                    {
                        // Convert all received events to pathfinder rpc calls
                        // and execute them ..
                        var rpcCall = EventToCall(payload);
                        await _toPathfinderProcess.Ask<PathfinderProcess.Return>(rpcCall);
                        return true;
                    }, FeedMode.Finite));
                
                _feedEventBuffer.Tell(new Buffer.FeedToActor(_feedConsumer, FeedMode.Finite, _consumeFeed));
                Context.Watch(_feedConsumer);
                
                Become(Running);
            });
        }
        
        void Running()
        {
            Receive<Terminated>(message =>
            {
                // End the bulk insert mode
                _toPathfinderProcess.Tell(new PathfinderProcess.Call(RpcMessage.PerformEdgeUpdates(), Self));
            });
            
            Receive<PathfinderProcess.Return>(message =>
            {
                Log.Info($"Feeding finished. PathfinderProcess ({_toPathfinderProcess}) should now be up to date.");
                Context.Stop(Self);
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
                            int.Parse(trust.Event.Limit.ToString())),
                        answerTo);
                case EventLog<TransferEventDTO> transfer:
                    return new PathfinderProcess.Call(
                        RpcMessage.Transfer(transfer.Log.Address, transfer.Event.From, transfer.Event.To,
                            transfer.Event.Value.ToString()), answerTo);
                default:
                    throw new ArgumentException($"Unknown type '{message.GetType().Name}'", nameof(message));
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="feedEventBuffer"></param>
        /// <param name="toPathfinderProcess"></param>
        /// <param name="consumeFeed">If 'true' then the consumed items are deleted from the feed</param>
        /// <returns></returns>
        public static Props Props(IActorRef feedEventBuffer, IActorRef toPathfinderProcess, bool consumeFeed)
            => Akka.Actor.Props.Create<Feeder>(feedEventBuffer, toPathfinderProcess, consumeFeed);
    }
}