using Akka.Actor;
using Akka.Event;
using Pathfinder.Server.Actors.Chain;
using Pathfinder.Server.Actors.System;
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
        
        private readonly IActorRef _pathfinder;

        public Server()
        {
            var executable = "/home/daniel/src/pathfinder/build/pathfinder";
            var rpcGateway = "https://rpc.circles.land";
            var dbFile = "/home/daniel/src/circles-world/PathfinderServer/Pathfinder.Server/db.dat";
            
            _realTimeClock = Context.ActorOf(RealTimeClock.Props(), "RealTimeClock");
            _blockClock = Context.ActorOf(BlockClock.Props(rpcGateway), "BlockClock");
            
            _signupEventSource = Context.ActorOf(BlockchainEventSource<SignupEventDTO>.Props(rpcGateway), "SignupEventSource");
            _organizationSignupEventSource = Context.ActorOf(BlockchainEventSource<OrganizationSignupEventDTO>.Props(rpcGateway), "OrganizationSignupEventSource");
            _trustEventSource = Context.ActorOf(BlockchainEventSource<TrustEventDTO>.Props(rpcGateway), "TrustEventSource");
            _transferEventSource = Context.ActorOf(BlockchainEventSource<TransferEventDTO>.Props(rpcGateway), "TransferEventSource");
            
            _pathfinder = Context.ActorOf(Pathfinder.Pathfinder.Props(executable, dbFile, rpcGateway), "Pathfinder");
        }
        
        protected override void OnReceive(object message)
        {
        }
        
        public static Props Props() 
            => Akka.Actor.Props.Create<Server>();
    }
}