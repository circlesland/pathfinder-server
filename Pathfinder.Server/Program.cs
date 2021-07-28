using System;
using System.Data;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Nancy.Hosting.Self;
using Pathfinder.Server.Http;

namespace Pathfinder.Server
{
    class Program
    {
        private static Timer t;
        private static NancyHost? _host;
        public static IActorRef? ServerActor;
        static int i = 0;

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
            
            // ServerActor = system.ActorOf(Actors.Server.Props(ApiNancyModule.NancyAdapterActor), "main");

            t = new Timer(async (_) =>
            {
                try
                {
                    var serverActor = ServerActor;
                    ServerActor = null;
                    
                    system.Log.Warning("Restarting... Downloading snapshot..");
                    await DownloadSnapshot();
                    system.Log.Warning("Restarting... Downloaded snapshot.");

                    if (serverActor != null)
                    {
                        system.Log.Warning("Restarting... Stopping ServerActor ..");
                        await serverActor.GracefulStop(TimeSpan.FromMinutes(1));
                        system.Log.Warning("Restarting... Stopped ServerActor.");
                    }

                    system.Log.Warning("Restarting... Starting ServerActor...");
                    ServerActor = system.ActorOf(Actors.Server.Props(ApiNancyModule.NancyAdapterActor), "main" + (i++));
                    system.Log.Warning("Restarting... Started ServerActor.");
                }
                catch (Exception e)
                {
                    system.Log.Error("Abfuck:", e);
                }
            }, null, TimeSpan.FromMinutes(0), TimeSpan.FromMinutes(5));

            var exitTrigger = new CancellationTokenSource();
            AppDomain.CurrentDomain.ProcessExit += (s, e) => { exitTrigger.Cancel(); };

            await Task.Delay(-1, exitTrigger.Token);
        }
        
        public static async Task DownloadSnapshot()
        {
            string fileName = Guid.NewGuid().ToString("N");
            WebClient webClient = new ();
            // Downloads the resource with the specified URI to a local file.
            await webClient.DownloadFileTaskAsync("https://chriseth.github.io/pathfinder/db.dat", fileName);
            if (File.Exists("db.dat") && File.Exists(fileName))
            {
                File.Delete("db.dat");
            }

            if (File.Exists(fileName)) {
                File.Move(fileName, "db.dat");
            }
        }
    }
}