using System;
using System.Threading.Tasks;
using Akka.Actor;
using Pathfinder.Server.Actors.Feed;

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
                /*
                var testConsumer = system.ActorOf(Consumer<DateTime>.Props(async (handshake, payload) =>
                {
                    //system.Log.Info(payload.ToString());
                    return true;
                }, FeedMode.Infinite), "TestConsumer");
                
                var testFeed = system.ActorOf(TestFeed.Props(testConsumer), "TestFeed");
*/
                system.ActorOf(Actors.Server.Props(), "main");
                Console.ReadLine();
            }
        }
    }
}