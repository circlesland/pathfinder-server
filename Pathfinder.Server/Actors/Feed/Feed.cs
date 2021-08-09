using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;

namespace Pathfinder.Server.Actors.Feed
{
    public class Feed<TPayload> : LoggingReceiveActor
    {
        protected override void PreStart() => Log.Info($"Feed<{typeof(TPayload).Name}> started.");
        protected override void PostStop() => Log.Info($"Feed<{typeof(TPayload).Name}> stopped.");

        #region Messages

        public sealed class Hello : HelloBase
        {
            public Hello(FeedSide side, FeedMode supportedFeedModes) : base(side, supportedFeedModes)
            {
            }
        }

        public sealed class Goodbye : GoodbyeBase
        {
            public Goodbye(GoodbyeReason reason) : base(reason)
            {
            }
        }

        /// <summary>
        /// When this message is sent to an 'Infinite' Feed it will resume to call the factory
        /// until the factory signals EOF again.
        /// </summary>
        public sealed class Continue
        {
        }

        private sealed class WaitForContinueTimeout
        {
        }

        private sealed class ConnectTimeout
        {
        }

        #endregion

        private readonly IActorRef _consumer;
        private readonly List<HelloBase> _handshake = new();
        private readonly Func<IEnumerable<HelloBase>, Task<FactoryResult>> _itemFactory;

        private ulong _processedItemCount;
        private DateTime? _start;
        private DateTime? _now;

        private readonly FeedMode _supportedFeedModes;
        private FeedMode? _negotiatedFeedMode;

        /// <summary>
        /// When the Feed is in 'Infinite' mode then this property defines when
        /// the Feed should timeout when no 'Continue' message arrives.
        /// </summary>
        private readonly TimeSpan? _continueTimeout;

        private readonly TimeSpan? _connectTimeout;

        public Feed(
            IActorRef consumer,
            Func<IEnumerable<HelloBase>, Task<FactoryResult>> itemFactory,
            FeedMode supportedFeedModes,
            TimeSpan? connectTimeout,
            TimeSpan? continueTimeout)
        {
            _consumer = consumer;
            _itemFactory = itemFactory;
            _supportedFeedModes = supportedFeedModes;
            _connectTimeout = connectTimeout;
            _continueTimeout = continueTimeout;

            if (_continueTimeout != null && !_supportedFeedModes.HasFlag(FeedMode.Infinite))
            {
                throw new ArgumentException(
                    $"The {nameof(_continueTimeout)} parameter can only be used with 'Infinite' Feeds.",
                    nameof(_continueTimeout));
            }

            var hello = new Hello(FeedSide.Sender, _supportedFeedModes);

            _consumer.Tell(hello);
            _handshake.Add(hello);

            var modeString = _supportedFeedModes.HasFlag(FeedMode.Finite) ? "Finite" : "";
            modeString += _supportedFeedModes.HasFlag(FeedMode.Infinite)
                ? (modeString.Length > 0 ? " + " : "") + "Infinite"
                : "";

            Log.Info($"Offered {modeString} mode(s) to Consumer {_consumer}. Waiting for response ..");
            Become(Connecting);
        }

        void Connecting()
        {
            Log.Info(
                $"Connecting to Consumer ({_consumer}) .. Timeout in: {_connectTimeout?.ToString() ?? "no timeout"}");

            var cancelable = _connectTimeout.HasValue
                ? Context.System.Scheduler.ScheduleTellOnceCancelable(
                    _connectTimeout.Value, Self, new ConnectTimeout(), Self)
                : null;

            Receive<ConnectTimeout>(message
                => throw new TimeoutException(
                    $"The Feed's attempt to connect to Consumer {_consumer} timed out after {_connectTimeout}."));

            Receive<Consumer<TPayload>.Hello>(message =>
            {
                cancelable?.Cancel();

                if (message.Side != FeedSide.Receiver)
                {
                    throw new Exception(
                        $"The supposed Consumer ({Sender}) answered with a value other than 'FeedSide.Receiver'.");
                }

                if (!_supportedFeedModes.HasFlag(message.Mode))
                {
                    throw new Exception($"The Consumer ({Sender}) didn't chose one supported FeedMode.");
                }

                var modeString = message.Mode.HasFlag(FeedMode.Finite) ? "Finite" :
                    _supportedFeedModes.HasFlag(FeedMode.Infinite) ? "Infinite" : "";
                _negotiatedFeedMode = message.Mode;

                Log.Info($"The Consumer ({Sender}) chose the {modeString} mode.");

                _handshake.Add(message);
                _start = DateTime.Now;
                Become(Connected);
            });

            Receive<Consumer<TPayload>.Goodbye>(message =>
            {
                Log.Warning($"The consumer ({Sender}) closed the connection. Reason: {Enum.GetName(message.Reason)}");
                Become(Closed);
            });
        }

        void Connected()
        {
            Log.Debug("Connected.");

            Receive<Consumer<TPayload>.Next>(message =>
            {
                Log.Debug(
                    $"The consumer ({_consumer}) requested the next element. Calling the {nameof(_itemFactory)} and going to WaitForFactory() ..");
                var factoryTask = _itemFactory(_handshake);
                factoryTask.PipeTo(Self);

                Become(WaitForFactory);
            });

            Receive<Consumer<TPayload>.Goodbye>(message =>
            {
                Log.Warning(
                    $"The consumer ({_consumer}) closed the connection. Reason: {Enum.GetName(message.Reason)}");
                Become(Closed);
            });
        }

        void WaitForFactory()
        {
            Log.Debug("WaitForFactory.");

            Receive<FactoryResult>(message =>
            {
                if (message.Eof)
                {
                    var isInfinite = _negotiatedFeedMode.HasValue &&
                                     _negotiatedFeedMode.Value.HasFlag(FeedMode.Infinite);

                    if (isInfinite)
                    {
                        Log.Info(
                            $"Received EOF item from factory (FeedMode.Infinite). Waiting for Continue message ..");

                        var runtime = _now - _start;
                        Log.Info(runtime != null
                            ? $"Stats: Processed {_processedItemCount} items in {runtime}. Throughput: {_processedItemCount / runtime.Value.TotalSeconds}/s."
                            : "Stats: No items processed so far.");

                        Become(WaitForContinue);
                    }
                    else
                    {
                        Log.Info(
                            $"Received EOF item from factory (FeedMode.Finite). Sending Goodbye(Eof) to the consumer ..");
                        Become(Eof);
                    }
                }
                else
                {
                    Log.Debug($"Received '{typeof(TPayload).Name}' item from factory. Sending it to the consumer ..");
                    _consumer.Tell(message.Payload);
                    _processedItemCount++;
                    _now = DateTime.Now;

                    Become(Connected);
                }
            });
            Receive<Status.Failure>(message =>
            {
                Log.Error(message.Cause, $"{nameof(_itemFactory)} failed to create the next item.");
                throw message.Cause;
            });
            Receive<Consumer<TPayload>.Goodbye>(message =>
            {
                Log.Warning(
                    $"The consumer ({_consumer}) closed the connection while the Feed was waiting for the {nameof(_itemFactory)}. Reason: {Enum.GetName(message.Reason)}");
                Become(Closed);
            });
        }

        void WaitForContinue()
        {
            var logStr = _continueTimeout != null
                ? " Timeout after: " + _continueTimeout.Value
                : "";
            Log.Info($"WaitForContinue.{logStr}");

            var cancelable = _continueTimeout.HasValue
                ? Context.System.Scheduler.ScheduleTellOnceCancelable(
                    _continueTimeout.Value, Self, new WaitForContinueTimeout(), Self)
                : null;

            Receive<Continue>(message =>
            {
                cancelable?.Cancel();

                Log.Info(
                    $"{Sender} sent a Continue event. Calling the {nameof(_itemFactory)} and going to WaitForFactory() ..");
                var factoryTask = _itemFactory(_handshake);
                factoryTask.PipeTo(Self);

                Become(WaitForFactory);
            });

            Receive<WaitForContinueTimeout>(message
                =>
            {
                _consumer?.Tell(new Goodbye(GoodbyeReason.Error));
                throw new TimeoutException(
                    $"The (Infinite) Feed timeout out after waiting for a Continue event for {_continueTimeout!.Value}");
            });
            
            Receive<Consumer<TPayload>.Goodbye>(message =>
            {
                cancelable?.Cancel();

                Log.Warning(
                    $"The consumer ({_consumer}) closed the connection while the (Infinite) Feed was waiting for a Continue event. Reason: {Enum.GetName(message.Reason)}");
                Become(Closed);
            });
        }

        void Eof()
        {
            Log.Debug("Eof.");
            _consumer.Tell(new Goodbye(GoodbyeReason.Eof));
            Become(Closed);
        }

        void Closed()
        {
            var runtime = _now - _start;
            Log.Info(runtime != null
                ? $"Closed. Processed {_processedItemCount} items in {runtime}. Throughput: {_processedItemCount / runtime.Value.TotalSeconds}/s."
                : "Closed without any processed items.");

            Context.Stop(Self);
        }

        public static Props Props(
            IActorRef consumer,
            Func<IEnumerable<HelloBase>, Task<FactoryResult>> itemFactory,
            FeedMode supportedFeedModes,
            TimeSpan? connectTimeout,
            TimeSpan? continueTimeout)
            => Akka.Actor.Props.Create<Feed<TPayload>>(
                consumer,
                itemFactory,
                supportedFeedModes,
                connectTimeout,
                continueTimeout);
    }
}