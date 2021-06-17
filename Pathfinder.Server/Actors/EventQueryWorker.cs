using System;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Common.Logging;
using Nethereum.BlockchainProcessing;
using Nethereum.BlockchainProcessing.LogProcessing;
using Nethereum.BlockchainProcessing.Processor;
using Nethereum.BlockchainProcessing.ProgressRepositories;
using Nethereum.RPC.Eth.Blocks;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Utils;
using Nethereum.Web3;

namespace Pathfinder.Server.Actors
{
    public class EventQueryWorker : ReceiveActor
    {
        #region Messages

        public sealed class Cancel
        {
        }

        public sealed class GetEvents
        {
            public readonly IActorRef RespondTo;
            public readonly string RpcGateway;
            public readonly BigInteger From;
            public readonly BigInteger To;
            public readonly ImmutableArray<object>? Topics;
            public readonly ImmutableArray<string>? Addresses;

            public GetEvents(IActorRef respondTo, string rpcGateway, BigInteger from, BigInteger to, object[]? topics,
                string[]? addresses)
            {
                RespondTo = respondTo;
                RpcGateway = rpcGateway;
                From = from;
                To = to;
                Topics = topics?.ToImmutableArray();
                Addresses = addresses?.ToImmutableArray();
            }
        }

        private sealed class Run
        {
            public readonly Web3? Web3;
            public readonly BigInteger From;
            public readonly BigInteger To;

            public Run(Web3? web3, BigInteger from, BigInteger to)
            {
                Web3 = web3;
                From = from;
                To = to;
            }
        }

        public abstract class ControlEvent
        {
        }

        public sealed class RunFinished : ControlEvent
        {
        }

        public sealed class RunCancelled : ControlEvent
        {
        }

        #endregion

        private CancellationTokenSource? _cancellationToken;
        private BlockchainProcessor? _preparedProcessor;
        private IActorRef? _answerTo;

        private ILoggingAdapter Log { get; } = Context.GetLogger();

        protected override void PreStart() => Log.Info($"EventQueryWorker started.");
        protected override void PostStop() => Log.Info($"EventQueryWorker stopped.");

        public EventQueryWorker(GetEvents message)
        {
            var web3 = new Web3(message.RpcGateway);

            // the number of blocks in a range to process in one batch
            const int defaultBlocksPerBatch = 10;
            const int requestRetryWeight = 0; // see below for retry algorithm
            const int minimumBlockConfirmations = 6;

            var filter = new NewFilterInput
            {
                Address = message.Addresses?.ToArray(),
                Topics = message.Topics?.ToArray()
            };

            // Send all logs to "RespondTo"
            _answerTo = message.RespondTo;
            var logProcessorHandler = new ProcessorHandler<FilterLog>(
                log => _answerTo.Tell(log),
                log => !log.Removed);

            ILog? logger = null;

            var orchestrator = new LogOrchestrator(
                web3.Eth,
                new[] {logProcessorHandler},
                filter,
                defaultBlocksPerBatch,
                requestRetryWeight);

            var progressRepository = new InMemoryBlockchainProgressRepository();
            var waitForBlockConfirmationsStrategy = new WaitStrategy();
            var lastConfirmedBlockNumberService = new LastConfirmedBlockNumberService(
                web3.Eth.Blocks.GetBlockNumber, 
                waitForBlockConfirmationsStrategy, 
                minimumBlockConfirmations);

            _preparedProcessor = new BlockchainProcessor(
                orchestrator, 
                progressRepository,
                lastConfirmedBlockNumberService,
                logger);

            _cancellationToken = new CancellationTokenSource();

            Become(Running);

            Self.Tell(new Run(web3, message.From, message.To));
        }

        void Running()
        {
            Receive<Run>(async message =>
            {
                if (message.Web3 == null || _cancellationToken == null || _preparedProcessor == null)
                {
                    throw new Exception(
                        "message.Web3 == null || _cancellationToken == null || _preparedProcessor == null");
                }

#pragma warning disable 4014
                // Disabled the warning because this can be quite long running and 
                // "awaiting" it would block the actor (a blocked actor couldn't process "Cancel" events).
                _preparedProcessor
                    .ExecuteAsync(message.To, _cancellationToken.Token, message.From)
                    .ContinueWith<ControlEvent>(_ => _cancellationToken.IsCancellationRequested
                        ? new RunCancelled()
                        : new RunFinished())
                    .PipeTo(Self);
#pragma warning restore 4014
            });

            Receive<Cancel>(message =>
            {
                Log.Info("The query will be cancelled ..");
                _cancellationToken?.Cancel();
            });

            Receive<RunFinished>(message =>
            {
                Log.Info("Query finished.");
                _answerTo.Tell(message);
                Context.Stop(Self);
            });

            Receive<RunCancelled>(message =>
            {
                Log.Info("Query cancelled.");
                _answerTo.Tell(message);
                Context.Stop(Self);
            });
        }

        public static Props Props(GetEvents message) => Akka.Actor.Props.Create<EventQueryWorker>(message);
    }
}