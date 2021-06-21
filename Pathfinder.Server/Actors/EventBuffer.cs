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
                :base(key, value)
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
        
        protected override void PreStart() => Log.Info($"EventBuffer<{typeof(TKey).Name}, {typeof(TValue).Name}> started.");
        protected override void PostStop() => Log.Info($"EventBuffer<{typeof(TKey).Name}, {typeof(TValue).Name}> stopped.");
        
        private readonly SortedList<TKey, TValue> _buffer = new();
        
        public delegate TKey GetKey(TValue log);
        private readonly GetKey _keyExtractor;

        private IActorRef? _feedActor;
        
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
                    if (_buffer.ContainsKey(key))
                    {
                        Log.Warning($"Received duplicate '{typeof(TValue).Name}' with key '{key}' from '{Sender}'. Ignoring it ..");
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
                    var currentKey = removeAndFindNext.CurrentKey == null
                        ? _buffer.First().Key
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
                    var currentKey = findNext.CurrentKey == null
                        ? _buffer.First().Key
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
                    break;
                case Buffer.Unroll unroll:
                    foreach (var item in _buffer)
                    {
                        unroll.To.Tell(item.Value);
                    }
                    Log.Info($"{Sender} unrolled the contents of the buffer ({_buffer.Count} items) to {unroll.To}.");
                    _buffer.Clear();
                    break;
                case Buffer.Publish:
                    foreach (var item in _buffer)
                    {
                        Context.System.EventStream.Publish(item.Value);
                    }
                    Log.Info($"{Sender} published the contents of the buffer ({_buffer.Count} items) to the EventStream.");
                    _buffer.Clear();
                    break;
                case Buffer.Feed {FeedMode: FeedMode.Finite} finiteFeed:
                    if (_feedActor != null)
                    {
                        throw new InvalidOperationException("There is already an open feed.");
                    }
                    IActorRef self = Self;
                    TKey? lastKey = default; // TODO: This variable is updated from a different actor. Should be o.k. tough because its local and no one else uses it
                    _feedActor = Context.ActorOf(Feed<TValue>.Props(
                        finiteFeed.To,
                        async (handshake) =>
                        {
                            // TODO: Don't use Ask. Look for a way to safe some messages..
                            var item = await self.Ask<RemoveAndFindNextResponse>(new RemoveAndFindNext(lastKey));
                            if (item.Eof || item.Value == null || item.Key == null)
                            {
                                // Send EOF
                                return new FactoryResult();
                            }

                            // Send item
                            lastKey = item.Key;
                            return new FactoryResult(item.Value);
                        }, 
                        FeedMode.Finite,
                        TimeSpan.FromSeconds(2) /* TODO: Make timeouts configurable */,
                        null));
                    
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
            }
        }
        
        public static Props Props(GetKey keyExtractor)
            => Akka.Actor.Props.Create<EventBuffer<TKey, TValue>>(keyExtractor);
    }
}