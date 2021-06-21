using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;

namespace Pathfinder.Server.Actors.Feed
{
    public class TestFeed : ReceiveActor
    {
        private ILoggingAdapter Log { get; } = Context.GetLogger();

        protected override void PreStart() => Log.Info($"TestFeedHost started.");
        protected override void PostStop() => Log.Info($"TestFeedHost stopped.");

        private IActorRef _feed;

        public TestFeed(IActorRef consumer)
        {
            var c = 0;

            async Task<FactoryResult> Factory(IEnumerable<HelloBase> handshake)
            {
                if (c < 100)
                {
                    c++;
                    return new FactoryResult(DateTime.Now);
                }

                c = 0;
                
                Context.System.Scheduler.ScheduleTellOnce(
                    TimeSpan.FromSeconds(1),
                    _feed,
                    new Feed<DateTime>.Continue(),
                    _feed);

                return new FactoryResult();
            }

            _feed = Context.ActorOf(Feed<DateTime>.Props(
                consumer,
                Factory,
                FeedMode.Both,
                TimeSpan.FromSeconds(2.5),
                TimeSpan.FromSeconds(60)
            ), "Feed");
        }

        public static Props Props(IActorRef consumer)
            => Akka.Actor.Props.Create<TestFeed>(consumer);
    }
}