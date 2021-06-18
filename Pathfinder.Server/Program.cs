﻿using System;
using System.Threading.Tasks;
using Akka.Actor;

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
            
            using (var system = ActorSystem.Create("system", config))
            {
                var main = system.ActorOf(Actors.Server.Props("https://rpc.circles.land"), "main");
                
                var pathfinder = system.ActorOf(Actors.Pathfinder.Props(
                    "/home/daniel/src/pathfinder/build/pathfinder",
                    "/home/daniel/src/circles-world/PathfinderServer/Pathfinder.Server/db.dat"
                ), "pathfinder");

                Console.ReadLine();
            }
        }
    }
}