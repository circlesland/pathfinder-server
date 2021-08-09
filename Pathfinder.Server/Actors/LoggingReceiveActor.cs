using Akka.Actor;
using Akka.Event;

namespace Pathfinder.Server.Actors
{
    public class LoggingReceiveActor : ReceiveActor
    {
        protected ILoggingAdapter Log { get; } = Context.GetLogger();
        
        protected override void PreStart() => Log.Info($"{GetType().Name} started.");
        protected override void PostStop() => Log.Info($"{GetType().Name} stopped.");
    }
}