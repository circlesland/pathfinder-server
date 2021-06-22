using System;
using System.Threading.Tasks;
using Akka.Actor;

namespace Pathfinder.Server
{
    class Program
    {
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

            using (var system = ActorSystem.Create("system"/*, config*/))
            {
                system.ActorOf(Actors.Server.Props(), "main");
                Console.ReadLine();
            }
        }
    }
}