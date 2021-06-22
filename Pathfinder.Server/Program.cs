using System;
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
                
            _host = new NancyHost(new Uri($"http://127.0.0.1:9999"));
            _host.Start();
                
            Console.ReadLine();
        }
    }
}