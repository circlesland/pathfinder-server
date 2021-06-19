using System;
using System.Collections.Immutable;
using System.IO;
using Akka.Actor;
using Akka.Event;
using Nethereum.Hex.HexTypes;
using Newtonsoft.Json.Linq;

namespace Pathfinder.Server.Actors
{
    public class PathfinderProcess : ReceiveActor
    {
        #region Messages
        
        public sealed class StopPathfinder
        {
            public readonly TimeSpan? KillAfter;
            
            public StopPathfinder(TimeSpan? killAfter)
            {
                KillAfter = killAfter;
            }
        }
        
        public sealed class Call
        {
            public readonly RpcMessage RpcMessage;
            public readonly IActorRef? AnswerTo;
            
            public Call(RpcMessage rpcMessage, IActorRef? answerTo)
            {
                RpcMessage = rpcMessage;
                AnswerTo = answerTo;
            }
        }
        
        public sealed class Return
        {
            public readonly RpcMessage RpcMessage;
            public readonly string ResultJson;
            
            public Return(RpcMessage rpcMessage, string resultJson)
            {
                RpcMessage = rpcMessage;
                ResultJson = resultJson;
            }
        }
        
        #endregion

        private ILoggingAdapter Log { get; } = Context.GetLogger();
        
        protected override void PostStop() => Log.Info("Pathfinder stopped");
        protected override void PreStart() => Log.Info("Pathfinder started.");

        private readonly IActorRef _processWrapper;
        private HexBigInteger _latestLoadedBlock;
        
        public PathfinderProcess(string executable)
        {
            if (!File.Exists(executable))
            {
                throw new FileNotFoundException($"Couldn't find the pathfinder executable at '{executable}'.");
            }
            
            _processWrapper = Context.ActorOf(ProcessWrapper.Props(
                executable,
                new[] {"--json"}.ToImmutableArray(),
                null,
                Self
            ));
            
            Become(Ready);
        }
        

        private Call? _lastCall;

        void OnCall(Call message)
        {
            _lastCall = message;
            
            BecomeStacked(Calling);
            
            _processWrapper.Tell(new ProcessWrapper.StdIn(_lastCall.RpcMessage.ToString()));
        }
        
        void Ready()
        {
            Receive<Call>(OnCall);
            Receive<Return>(message =>
            {
                if (_lastCall == null)
                {
                    throw new Exception("_lastCall == null");
                }
                
                dynamic result = JObject.Parse(message.ResultJson);
                if (result.error != null)
                {
                    throw new Exception($"The '{_lastCall.RpcMessage.Cmd}' call failed: {message.ResultJson}");
                }
                
                _lastCall.AnswerTo.Tell(new Return(_lastCall.RpcMessage, message.ResultJson));
                _lastCall = null;
                Become(Ready);
            });
            
            Receive<StopPathfinder>(message =>
            {
                if (message.KillAfter != null)
                {
                    SetReceiveTimeout(message.KillAfter);
                }
                
                _processWrapper.Tell(new ProcessWrapper.StopProcess());
                Become(Stopping);
            });
            
            Receive<ProcessWrapper.StdErr>(message =>
            {
                Log.Info("ProcessWrapper StdErr: {0}", message.Data);
            });
        }

        void Calling()
        {
            Receive<ProcessWrapper.StdOut>(message =>
            {
                var jobject = TryParse(message.Data);
                if (_lastCall == null)
                {
                    throw new Exception("Actor is in 'Calling' state but the '_lastCall' is not set.");
                }
                if (jobject == null)
                {
                    Log.Warning("Calling '{0}': StdOut: {1}", _lastCall.RpcMessage.Cmd, message.Data);
                    return;
                }
                dynamic djobject = jobject;
                if (djobject.id == null)
                {
                    Log.Warning("Calling '{0}': Received a JSON formatted string without 'id' field on StdOut: {1}",_lastCall.RpcMessage.Cmd, message.Data);
                    return;
                }
                if (djobject.id != _lastCall.RpcMessage.Id)
                {
                    var errorMessage = $"Calling '{_lastCall.RpcMessage.Cmd}': Received a JSON formatted string on StdOut which 'id' field doesn't match the currently called id '{_lastCall.RpcMessage.Id}': {message.Data}";
                    Log.Error(errorMessage);
                    throw new Exception(errorMessage);
                }
                if (djobject.error != null)
                {
                    throw new Exception($"The '{_lastCall.RpcMessage.Cmd}' call failed: {message.Data}");
                }
                Log.Info("Calling '{0}': Return: {1}", _lastCall.RpcMessage.Cmd, message.Data);
                _lastCall.AnswerTo?.Tell(new Return(_lastCall.RpcMessage, message.Data));
                
                UnbecomeStacked();
                
                _lastCall = null;
            });
            Receive<ProcessWrapper.StdErr>(message =>
            {
                Log.Info("Calling '{0}': StdErr: {1}", _lastCall.RpcMessage.Cmd, message.Data);
            });
            Receive<ProcessWrapper.Exited>(message =>
            {
                var errorMessage = string.Format("Calling '{0}': Process exited with code {1}.",
                    _lastCall.RpcMessage.Cmd, message.ExitCode);
                
                Log.Error(errorMessage);
                throw new Exception(errorMessage);
            });
        }

        void Stopping()
        {
            Receive<ProcessWrapper.Exited>(message =>
            {
                Log.Info("Process exited with code {0}. Stopping Self ..", message.ExitCode);
                Context.Stop(Self);
            });
            Receive<ReceiveTimeout>(_ =>
            {
                Log.Info("The process didn't exit in a timely fashion. Trying to kill it ..");
                SetReceiveTimeout(null);
                
                _processWrapper.Tell(new ProcessWrapper.KillProcess());
            });
        }

        JObject? TryParse(string data)
        {
            if (!data.StartsWith("{") || !data.EndsWith("}"))
            {
                return null;
            }

            try
            {
                return JObject.Parse(data);
            }
            catch (Exception e)
            {
                Log.Warning("Received data that appeared to be JSON but which is not deserializable: {0}", data);
                return null;
            }
        }

        public static Props Props(string executable) 
            => Akka.Actor.Props.Create<PathfinderProcess>(executable);
    }
}