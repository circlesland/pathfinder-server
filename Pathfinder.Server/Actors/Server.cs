using Akka.Actor;
using Akka.Event;
using Pathfinder.Server.contracts;

namespace Pathfinder.Server.Actors
{
    public class Server : UntypedActor
    {       
        private ILoggingAdapter Log { get; } = Context.GetLogger();
        
        protected override void PreStart() => Log.Info($"Main started.");
        protected override void PostStop() => Log.Info($"Main stopped.");

        private readonly IActorRef _realTimeClock;
        private readonly IActorRef _blockClock;
        
        private readonly IActorRef _signupEventSource;
        private readonly IActorRef _organizationSignupEventSource;
        private readonly IActorRef _trustEventSource;
        private readonly IActorRef _transferEventSource;
        
        public Server(string rpcGateway)
        {
            _realTimeClock = Context.ActorOf(RealTimeClock.Props(), "RealTimeClock");
            _blockClock = Context.ActorOf(BlockClock.Props(rpcGateway), "BlockClock");
            
            _signupEventSource = Context.ActorOf(BlockchainEventSource<SignupEventDTO>.Props(rpcGateway), "SignupEventSource");
            _organizationSignupEventSource = Context.ActorOf(BlockchainEventSource<OrganizationSignupEventDTO>.Props(rpcGateway), "OrganizationSignupEventSource");
            _trustEventSource = Context.ActorOf(BlockchainEventSource<TrustEventDTO>.Props(rpcGateway), "TrustEventSource");
            _transferEventSource = Context.ActorOf(BlockchainEventSource<TransferEventDTO>.Props(rpcGateway), "TransferEventSource");
        }
        
        protected override void OnReceive(object message)
        {
        }
        
        public static Props Props(string rpcGateway) 
            => Akka.Actor.Props.Create<Server>(rpcGateway);
    }
}