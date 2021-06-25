using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Nancy.Hosting.Self;
using Pathfinder.Server.Http;

namespace Pathfinder.Server
{
    class Program
    {
        private static NancyHost? _host;
        public static IActorRef? ServerActor;
        
        static async Task Main()
        {
            var httpEndpoint = "http://localhost:7891";
            var listenerUri = new Uri(httpEndpoint);

            Console.WriteLine($"Starting http-server at {listenerUri} ..");
            _host = new NancyHost(listenerUri);
            _host.Start();
            Console.WriteLine($"Http-server running at: {listenerUri}");
            
            var config = @"akka {  
                            stdout-loglevel = DEBUG
                            loglevel = DEBUG
                            actor {                
                                debug {  
                                      receive = on 
                                      autoreceive = on
                                      lifecycle = on
                                      event-stream = on
                                      unhandled = on
                                }
                            }";

            using var system = ActorSystem.Create("system" /*, config*/);

            ApiNancyModule.NancyAdapterActor = system.ActorOf(
                Props.Create<NancyAdapterActor>(ApiNancyModule._resetEvents, ApiNancyModule._responses));
            
            ServerActor = system.ActorOf(Actors.Server.Props(ApiNancyModule.NancyAdapterActor), "main");
            
            var exitTrigger = new CancellationTokenSource();
            AppDomain.CurrentDomain.ProcessExit += (s, e) => 
            {
                exitTrigger.Cancel();
            };

            await Task.Delay(-1, exitTrigger.Token);
        }
    }
}