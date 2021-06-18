using Akka.Actor;
using Akka.Event;
using Nethereum.Hex.HexTypes;
using Nethereum.Web3;

namespace Pathfinder.Server.Actors
{
    public class BlockClock : UntypedActor
    {
        #region Messages

        public class NextBlock : Clock.Elapsed
        {
            public readonly HexBigInteger BlockNo;
            public NextBlock(HexBigInteger blockNo)
            {
                BlockNo = blockNo;
            }
        }

        #endregion

        private ILoggingAdapter Log { get; } = Context.GetLogger();

        protected override void PreStart() => Log.Info($"BlockClock started.");
        protected override void PostStop() => Log.Info($"BlockClock stopped.");

        private readonly string _rpcGateway;
        private HexBigInteger _lastBlock;
        
        public BlockClock(string rpcGateway)
        {
            _rpcGateway = rpcGateway;
            Context.System.EventStream.Subscribe(Self, typeof(RealTimeClock.SecondElapsed));
        }

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case RealTimeClock.SecondElapsed:
                    new Web3(_rpcGateway).Eth.Blocks.GetBlockNumber.SendRequestAsync().PipeTo(Self);
                    break;
                case HexBigInteger currentBlock:
                    if (_lastBlock != currentBlock)
                    {
                        Log.Info($"New block: {currentBlock}");
                        Context.System.EventStream.Publish(new NextBlock(currentBlock));
                    }
                    _lastBlock = currentBlock;
                    break;
            }
        }

        public static Props Props(string rpcGateway)
            => Akka.Actor.Props.Create<BlockClock>(rpcGateway);
    }
}