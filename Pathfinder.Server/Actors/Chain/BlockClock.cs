using Akka.Actor;
using Nethereum.Hex.HexTypes;
using Nethereum.Web3;
using Pathfinder.Server.Actors.MessageContracts;
using Pathfinder.Server.Actors.System;

namespace Pathfinder.Server.Actors.Chain
{
    public class BlockClock : LoggingReceiveActor
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

        private readonly string _rpcGateway;
        private HexBigInteger? _lastBlock;

        public BlockClock(string rpcGateway)
        {
            _rpcGateway = rpcGateway;
            Context.System.EventStream.Subscribe(Self, typeof(RealTimeClock.SecondElapsed));

            Receive<RealTimeClock.SecondElapsed>(_ =>
            {
                new Web3(_rpcGateway).Eth.Blocks.GetBlockNumber.SendRequestAsync().PipeTo(Self);
            });
            
            Receive<HexBigInteger>(currentBlock =>
            {
                if (_lastBlock != currentBlock)
                {
                    Log.Info($"New block: {currentBlock}");
                    Context.System.EventStream.Publish(new NextBlock(currentBlock));
                }

                _lastBlock = currentBlock;
            });

            Receive<Status.Failure>(failure => Log.Error(failure.Cause, "Couldn't get the current block."));
        }

        public static Props Props(string rpcGateway)
            => Akka.Actor.Props.Create<BlockClock>(rpcGateway);
    }
}