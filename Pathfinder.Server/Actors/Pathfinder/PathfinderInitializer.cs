using System.Numerics;
using Akka.Actor;
using Akka.Event;
using Nethereum.Hex.HexTypes;
using Newtonsoft.Json.Linq;

namespace Pathfinder.Server.Actors.Pathfinder
{
    public class PathfinderInitializer : ReceiveActor
    {
        #region Messages

        public sealed class Done
        {
            public readonly HexBigInteger LatestKnownBlock;

            public Done(HexBigInteger latestKnownBlock)
            {
                LatestKnownBlock = latestKnownBlock;
            }
        }

        #endregion
        
        private ILoggingAdapter Log { get; } = Context.GetLogger();
        
        protected override void PreStart() => Log.Info("PathfinderInitializer started.");
        protected override void PostStop() => Log.Info("PathfinderInitializer stopped");
        
        private readonly IActorRef _answerTo;
        private readonly IActorRef _pathfinder;
        private readonly string _databaseFile;

        private BigInteger? _latestKnownBlock;
        
        public PathfinderInitializer(IActorRef pathfinder, string databaseFile)
        {
            _answerTo = Context.Parent;
            _pathfinder = pathfinder;
            _databaseFile = databaseFile;
            
            Become(LoadDb);
        }

        void LoadDb()
        {
            Log.Info("LoadDb()");
            
            _pathfinder.Tell(new PathfinderProcess.Call(RpcMessage.LoadDb(_databaseFile), Self));
            Receive<PathfinderProcess.Return>(message =>
            {
                dynamic result = JObject.Parse(message.ResultJson);
                _latestKnownBlock = (ulong)result.blockNumber;
                
                Log.Info("LoadDb() - Success");
                
                _answerTo.Tell(new Done(_latestKnownBlock.Value.ToHexBigInteger()));
                Context.Stop(Self);
            });
        }
        
        public static Props Props(IActorRef pathfinder, string databaseFile) 
            => Akka.Actor.Props.Create<PathfinderInitializer>(pathfinder, databaseFile);
    }
}