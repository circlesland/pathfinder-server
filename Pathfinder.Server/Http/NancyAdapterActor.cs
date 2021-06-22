using System;
using System.Collections.Concurrent;
using System.Threading;
using Akka.Actor;
using Pathfinder.Server.Actors.Pathfinder;

namespace Pathfinder.Server.Http
{
    /// <summary>
    /// Waits for responses of all kind on the bus and wakes up the waiting request when a response for it arrives.
    /// </summary>
    public class NancyAdapterActor : ReceiveActor, ILogReceive
    {
        public NancyAdapterActor(
            ConcurrentDictionary<uint, AutoResetEvent> resetEvents, 
            ConcurrentDictionary<uint, PathfinderProcess.Return> responses)
        {
            Receive<PathfinderProcess.Return>(message =>
            {
                if (!resetEvents.TryRemove(message.Id, out var resetEvent))
                {
                    return;
                }

                responses.TryAdd(message.Id, message);
                resetEvent.Set();
            });
        }
    }
}