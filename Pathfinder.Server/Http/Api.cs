using System;
using System.Collections.Concurrent;
using System.Threading;
using Akka.Actor;
using Nancy;
using Pathfinder.Server.Actors.Pathfinder;

namespace Pathfinder.Server.Http
{
    public class ApiNancyModule : NancyModule
    {
        public static readonly ConcurrentDictionary<uint, AutoResetEvent> _resetEvents = new();
        public static readonly ConcurrentDictionary<uint, PathfinderProcess.Return> _responses = new();

        public static IActorRef? NancyAdapterActor;
        
        private static uint _id;
        
        public ApiNancyModule()
        {
            // 127.0.0.1:9999/flow/0xDE374ece6fA50e781E81Aac78e811b33D16912c7/0x50958e084D5E9E890ECc0A98e4d5EA50962681F0/100
            Get("/flow/{from}/{to}/{value}", async x =>
            {
                if (NancyAdapterActor == null)
                {
                    throw new Exception("NancyAdapterActor == null");
                }
                
                var from = x.from;
                var to = x.to;
                var value = x.value;

                var query = new PathfinderProcess.Call(RpcMessage.Flow(from, to, value), NancyAdapterActor);
                Program.ServerActor.Tell(query);
                    
                var resetEvent = new AutoResetEvent(false);
                _resetEvents.TryAdd(query.RpcMessage.Id, resetEvent);
                resetEvent.WaitOne(TimeSpan.FromSeconds(30));
                    
                if (_responses.TryRemove(query.RpcMessage.Id, out var returnValue))
                {
                    return Response.AsJson(returnValue.ResultJson);
                }
                
                return Response.AsJson(new
                {
                    error = "timeout"
                });
            });
  
        }
    }
}