using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Event;
using Pathfinder.Server.Actors.Feed;
using Buffer = Pathfinder.Server.Actors.MessageContracts.Buffer;

namespace Pathfinder.Server.Actors
{
    public class EventBuffer<TKey, TValue> : UntypedActor
        where TKey : IComparable
    {
        #region Messages

        public sealed class GetStats
        {
        }

        public sealed class GetStatsResult
        {
            public readonly TKey? MinKey;
            public readonly TKey? MaxKey;
            public readonly long Items;

            public GetStatsResult(
                TKey? minKey,
                TKey? maxKey,
                long items)
            {
                MinKey = minKey;
                MaxKey = maxKey;
                Items = items;
            }
        }

        private class FindNext
        {
            public readonly TKey? CurrentKey;

            public FindNext(TKey? currentKey)
            {
                CurrentKey = currentKey;
            }
        }

        private class FindNextResponse
        {
            public readonly bool Eof;
            public readonly TKey? Key;
            public readonly TValue? Value;

            public FindNextResponse()
            {
                Eof = true;
            }

            public FindNextResponse(TKey key, TValue value)
            {
                Key = key;
                Value = value;
            }
        }

        private class RemoveAndFindNext : FindNext
        {
            public RemoveAndFindNext(TKey? currentKey) : base(currentKey)
            {
            }
        }

        private class RemoveAndFindNextResponse : FindNextResponse
        {
            public readonly bool Removed;

            public RemoveAndFindNextResponse(bool removed, TKey key, TValue value)
                : base(key, value)
            {
                Removed = removed;
            }

            public RemoveAndFindNextResponse(bool removed)
                : base()
            {
                Removed = removed;
            }
        }

        #endregion

        private ILoggingAdapter Log { get; } = Context.GetLogger();

        protected override void PreStart() =>
            Log.Info($"EventBuffer<{typeof(TKey).Name}, {typeof(TValue).Name}> started.");

        protected override void PostStop() =>
            Log.Info($"EventBuffer<{typeof(TKey).Name}, {typeof(TValue).Name}> stopped.");

        private readonly SortedList<TKey, TValue> _buffer = new();

        public delegate TKey GetKey(TValue log);

        private readonly GetKey _keyExtractor;

        private IActorRef? _feedActor;
        
        // Contains the key value up to which point a
        // currently running feed will return items
        // from the buffer.
        // This is used as a boundary to ensure that no new items
        // with lower keys than the max. key at the time
        // when the feed started are added.
        // If items with a lower key than this will be added
        // the buffer fails,
        private TKey? _feedingUpToKey;

        public EventBuffer(GetKey keyExtractor)
        {
            _keyExtractor = keyExtractor;
        }

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                // Check if the message is a collectible event
                case TValue item:
                    var key = _keyExtractor(item);
                    if (_feedingUpToKey != null && key.CompareTo(_feedingUpToKey) <= 0)
                    {
                        throw new Exception($"A new {typeof(TValue).Name} item was added to the {nameof(EventBuffer<TKey,TValue>)} while a feed was active, The added item has a smaller or equal key than the max. feed boundary ({_feedingUpToKey}).");
                    }
                    if (_buffer.ContainsKey(key))
                    {
                        Log.Warning(
                            $"Received duplicate '{typeof(TValue).Name}' with key '{key}' from '{Sender}'. Ignoring it ..");
                        return;
                    }

                    _buffer.Add(key, item);
                    break;

                // Check if the message is a command
                case GetStats:
                    if (_buffer.Count == 0)
                    {
                        Sender.Tell(new GetStatsResult(
                            default,
                            default,
                            0));

                        return;
                    }

                    Sender.Tell(new GetStatsResult(
                        _buffer.First().Key,
                        _buffer.Last().Key, // TODO: Find a better way for _buffer.Last() if this is called often
                        _buffer.Count));
                    break;
                case RemoveAndFindNext removeAndFindNext:
                {
                    if (_buffer.Count == 0)
                    {
                        Sender.Tell(new RemoveAndFindNextResponse(false));
                        return;
                    }
                    
                    var currentKey = removeAndFindNext.CurrentKey == null
                        ? _buffer.FirstOrDefault().Key
                        : removeAndFindNext.CurrentKey;

                    var idx = _buffer.IndexOfKey(currentKey);
                    var removed = false;
                    try
                    {
                        var t = _buffer.Values[idx + 1];
                        removed = _buffer.Remove(currentKey);
                        Sender.Tell(new RemoveAndFindNextResponse(removed, _keyExtractor(t), t));
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        // There is no "next" element in the buffer. Send EOF.
                        Sender.Tell(new RemoveAndFindNextResponse(removed));
                    }
                }
                    break;
                case FindNext findNext:
                {
                    if (_buffer.Count == 0)
                    {
                        Sender.Tell(new FindNextResponse());
                        return;
                    }
                    
                    var currentKey = findNext.CurrentKey == null
                        ? _buffer.FirstOrDefault().Key
                        : findNext.CurrentKey;

                    var idx = _buffer.IndexOfKey(currentKey);
                    try
                    {
                        var t = _buffer.Values[idx + 1];
                        Sender.Tell(new FindNextResponse(_keyExtractor(t), t));
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        // There is no "next" element in the buffer. Send EOF.
                        Sender.Tell(new FindNextResponse());
                    }
                }
                    break;
                case Buffer.DumpToActor unroll:
                    foreach (var item in _buffer)
                    {
                        unroll.To.Tell(item.Value);
                    }

                    Log.Info($"{Sender} unrolled the contents of the buffer ({_buffer.Count} items) to {unroll.To}.");
                    _buffer.Clear();
                    break;
                case Buffer.DumpToStream:
                    foreach (var item in _buffer)
                    {
                        Context.System.EventStream.Publish(item.Value);
                    }

                    Log.Info($"{Sender} published the contents of the buffer ({_buffer.Count} items) to the EventStream.");
                    _buffer.Clear();
                    break;
                case Buffer.FeedToActor {FeedMode: FeedMode.Finite} finiteFeed:
                    if (_feedActor != null)
                    {
                        throw new InvalidOperationException("There is already an open feed.");
                    }

                    IActorRef self = Self;
                    
                    // Track which was the last processed key. This is required as argument to "FindNext".
                    TKey? lastProcessedKey = default; // TODO: This variable is updated from a different actor.
                    
                    // Stop at the last key (as of now). This ensures that the caller gets a predictable
                    // amount of events.
                    if (_buffer.Values.Count > 0)
                    {
                        _feedingUpToKey = _keyExtractor(_buffer.Values.Last());
                    }

                    _feedActor = Context.ActorOf(Feed<TValue>.Props(
                        finiteFeed.To,
                        async (handshake) =>
                        {
                            // TODO: Don't use Ask. Look for a way to safe some messages..
                            var item = finiteFeed.Consume
                                ? await self.Ask<RemoveAndFindNextResponse>(new RemoveAndFindNext(lastProcessedKey))
                                : await self.Ask<FindNextResponse>(new FindNext(lastProcessedKey));
                            
                            if (item.Eof || item.Value == null || item.Key == null)
                            {
                                // Send EOF
                                return new FactoryResult();
                            }

                            if (_feedingUpToKey != null && item.Key.CompareTo(_feedingUpToKey) > 0)
                            {
                                // Send EOF
                                return new FactoryResult();
                            }

                            // Send item
                            lastProcessedKey = item.Key;
                            return new FactoryResult(item.Value);
                        },
                        FeedMode.Finite,
                        TimeSpan.FromSeconds(2) /* TODO: Make timeouts configurable */,
                        null));

                    Context.Watch(_feedActor);
                    // TODO:
                    // Unrolls the buffer one by one using a Feed.
                    // This allows new events to come in while the
                    // buffer is emptied.
                    // If more new events stream in than can be
                    // consumed, the Feed will never EOF.
                    // If the buffer is emptied completely the Feed EOFs.
                    // !! There can be only one active feed per buffer at a time
                    // !! When there is an active feed then no events that happened
                    //    prior to the last streamed event can be added. Doing so
                    //    will cause the buffer to crash.
                    break;
                case Terminated terminated:
                    if (_feedActor != null && _feedActor.Equals(terminated.ActorRef))
                    {
                        _feedActor = null;
                        _feedingUpToKey = default;
                    }
                    break;
            }
        }

        public static Props Props(GetKey keyExtractor)
            => Akka.Actor.Props.Create<EventBuffer<TKey, TValue>>(keyExtractor);
    }
}