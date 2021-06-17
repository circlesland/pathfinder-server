using System;
using System.Threading.Tasks;
using Akka.Actor;
using Nethereum.Web3;

namespace Pathfinder.Server
{
    class Program
    {
        // const string hubAddress = "0x29b9a7fBb8995b2423a71cC17cf9810798F6C543";

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
            
            using (var system = ActorSystem.Create("pathfinder-system", config))
            {
                var pathfinder = system.ActorOf(Actors.Pathfinder.Props(
                    "/home/daniel/src/circles-world/PathfinderServer/PathfinderServer/Server/data/db.dat"
                ), "pathfinder");

                var eventSource = system.ActorOf(Actors.EventSource.Props(
                    "https://rpc.circles.land", 5000, new[]
                    {
                        Web3.Sha3("Signup(address,address)"),
                        Web3.Sha3("OrganizationSignup(address)"),
                        Web3.Sha3("Trust(address,address,uint256)"),
                        Web3.Sha3("Transfer(address,address,uint256)")
                    }, new[] {pathfinder}), "eventSource");

                var result = await Task.Delay(TimeSpan.FromSeconds(5))
                    .ContinueWith((r) => pathfinder.Ask<Actors.Pathfinder.Return>(new Actors.Pathfinder.Call(
                        RpcMessage.Flow("0xDE374ece6fA50e781E81Aac78e811b33D16912c7",
                            "0x3A599ab30A17Bc7527D8BE6D434F3048eA92d5d7", "99999999999"), null)));

                Console.ReadLine();
            }
        }
    }
}