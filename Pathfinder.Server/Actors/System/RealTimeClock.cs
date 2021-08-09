using System;
using Akka.Actor;

namespace Pathfinder.Server.Actors.System
{
    public class RealTimeClock : LoggingReceiveActor
    {
        #region Messages

        private class Tick
        {
            public static readonly Tick Instance = new();

            private Tick()
            {
            }
        }

        public abstract class Elapsed
        {
            public readonly DateTime Now;

            public Elapsed(DateTime now)
            {
                Now = now;
            }
        }

        public sealed class SecondElapsed : Elapsed
        {
            public SecondElapsed(DateTime now) : base(now)
            {
            }
        }

        public sealed class MinuteElapsed : Elapsed
        {
            public MinuteElapsed(DateTime now) : base(now)
            {
            }
        }

        public sealed class HourElapsed : Elapsed
        {
            public HourElapsed(DateTime now) : base(now)
            {
            }
        }

        #endregion

        private int _lastSecond = -1;
        private int _lastMinute = -1;
        private int _lastHour = -1;

        public RealTimeClock()
        {
            Context.System.Scheduler.ScheduleTellRepeatedly(
                TimeSpan.Zero, TimeSpan.FromSeconds(1), Self, Tick.Instance, Self);

            Receive<Tick>(message =>
            {
                var now = DateTime.Now;

                if (_lastSecond > -1)
                {
                    if (_lastSecond != now.Second)
                    {
                        Context.System.EventStream.Publish(new SecondElapsed(now));
                    }

                    if (_lastMinute != now.Minute)
                    {
                        Context.System.EventStream.Publish(new MinuteElapsed(now));
                    }

                    if (_lastHour != now.Hour)
                    {
                        Context.System.EventStream.Publish(new HourElapsed(now));
                    }
                }

                _lastSecond = now.Second;
                _lastMinute = now.Minute;
                _lastHour = now.Hour;
            });
        }

        public static Props Props()
            => Akka.Actor.Props.Create<RealTimeClock>();
    }
}