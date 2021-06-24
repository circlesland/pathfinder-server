using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using Akka.Actor;
using Nancy;
using Nancy.Responses;
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
            Get("/flow/{from}/{to}/{value}", async args =>
            {
                if (NancyAdapterActor == null)
                {
                    throw new Exception("NancyAdapterActor == null");
                }
                
                var from = args.from;
                var to = args.to;
                var value = args.value;

                var query = new PathfinderProcess.Call(RpcMessage.Flow(from, to, value), NancyAdapterActor);
                Program.ServerActor.Tell(query);
                    
                var resetEvent = new AutoResetEvent(false);
                _resetEvents.TryAdd(query.RpcMessage.Id, resetEvent);
                resetEvent.WaitOne(TimeSpan.FromSeconds(30));
                    
                if (_responses.TryRemove(query.RpcMessage.Id, out var returnValue))
                {
                    var re = new TextResponse(HttpStatusCode.OK, returnValue.ResultJson, Encoding.UTF8);
                    re.ContentType = "application/json";
                    return re;
                }
                
                return Response.AsJson(new
                {
                    error = "timeout"
                });
            });
            
            Get("/adjacencies/{of}", async args =>
            {
                if (NancyAdapterActor == null)
                {
                    throw new Exception("NancyAdapterActor == null");
                }
                
                var of = args.of;

                var query = new PathfinderProcess.Call(RpcMessage.Adjacencies(of), NancyAdapterActor);
                Program.ServerActor.Tell(query);
                    
                var resetEvent = new AutoResetEvent(false);
                _resetEvents.TryAdd(query.RpcMessage.Id, resetEvent);
                resetEvent.WaitOne(TimeSpan.FromSeconds(30));
                    
                if (_responses.TryRemove(query.RpcMessage.Id, out var returnValue))
                {
                    var re = new TextResponse(HttpStatusCode.OK, returnValue.ResultJson, Encoding.UTF8);
                    re.ContentType = "application/json";
                    return re;
                }
                
                return Response.AsJson(new
                {
                    error = "timeout"
                });
            });
        }
    }
}