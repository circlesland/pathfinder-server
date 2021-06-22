using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using Akka.Actor;
using Akka.Event;
using Newtonsoft.Json.Linq;
using Pathfinder.Server.Actors.System;

namespace Pathfinder.Server.Actors.Pathfinder
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
            public readonly uint Id;
            public readonly string ResultJson;

            public Return(uint id, string resultJson)
            {
                Id = id;
                ResultJson = resultJson;
            }
        }

        #endregion

        private ILoggingAdapter Log { get; } = Context.GetLogger();

        protected override void PostStop() => Log.Info("PathfinderProcess stopped");
        protected override void PreStart() => Log.Info("PathfinderProcess started.");

        private readonly IActorRef _processWrapper;
        
        private List<string> _callLog;

        protected override SupervisorStrategy SupervisorStrategy()
        {
            // If anything within the PathfinderProcess dies, let the whole thing die
            return new AllForOneStrategy(0, 0, ex => Directive.Escalate);
        }

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
        private IActorRef? _lastCallSender;

        void OnCall(Call message)
        {
            _lastCall = message;
            _lastCallSender = Sender;

            var rpcMessageJson = _lastCall.RpcMessage.ToString();
            _callLog = new List<string> {rpcMessageJson};

            BecomeStacked(Calling);

            _processWrapper.Tell(new ProcessWrapper.StdIn(rpcMessageJson));
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

                (_lastCall.AnswerTo ?? _lastCallSender).Tell(new Return(result.id, message.ResultJson));
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
                Log.Debug($"Ready: ProcessWrapper StdErr: {message.Data}");
            });
            
            Receive<ProcessWrapper.Exited>(message =>
            {
                var errorMessage = $"Process exited unexpected with code {message.ExitCode}.";
                Log.Error(errorMessage);
                throw new Exception(errorMessage);
            });
        }

        void Calling()
        {
            Receive<ProcessWrapper.StdOut>(message =>
            {
                _callLog.Add(message.Data);
                
                var jobject = TryParse(message.Data);
                if (_lastCall == null)
                {
                    throw new Exception("Actor is in 'Calling' state but the '_lastCall' is not set.");
                }

                if (jobject == null)
                {
                    var msg = $"Calling '{_lastCall.RpcMessage.Cmd}': StdOut: {message.Data}";
                    Log.Warning(msg);
                    return;
                }

                dynamic djobject = jobject;
                if (djobject.id == null)
                {
                    var msg =
                        $"Calling '{_lastCall.RpcMessage.Cmd}': Received a JSON formatted string without 'id' field on StdOut: {message.Data}";
                    Log.Warning(msg);
                    return;
                }

                if (djobject.id != _lastCall.RpcMessage.Id)
                {
                    var errorMessage =
                        $"Calling '{_lastCall.RpcMessage.Cmd}': Received a JSON formatted string on StdOut which 'id' field doesn't match the currently called id '{_lastCall.RpcMessage.Id}': {message.Data}";
                    Log.Error(errorMessage);
                    throw new Exception(errorMessage);
                }

                if (djobject.error != null)
                {
                    // TODO: Decide if the error in pathfinder is fatal or not (are any rpc errors fatal? Will data corrupt?)
                    //throw new Exception($"The '{_lastCall.RpcMessage.Cmd}' call failed: {message.Data}");
                    Log.Error($"The '{_lastCall.RpcMessage.Cmd}' call failed with result: {message.Data}. Log follows:" 
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, _callLog));
                }

                Log.Info("Calling '{0}': Return: {1}", _lastCall.RpcMessage.Cmd, message.Data);
                (_lastCall.AnswerTo ?? _lastCallSender).Tell(new Return((uint)djobject.id, message.Data));

                UnbecomeStacked();

                _lastCall = null;
            });
            Receive<ProcessWrapper.StdErr>(message =>
            {
                _callLog.Add(message.Data);
                
                if (_lastCall == null)
                {
                    throw new Exception("Actor is in 'Calling' state but the '_lastCall' is not set.");
                }

                var msg = $"Calling '{_lastCall.RpcMessage.Cmd}': StdErr: {message.Data}";
                Log.Debug(msg);
            });
            Receive<ProcessWrapper.Exited>(message =>
            {
                if (_lastCall == null)
                {
                    throw new Exception("Actor is in 'Calling' state but the '_lastCall' is not set.");
                }

                var errorMessage =
                    $"Calling '{_lastCall.RpcMessage.Cmd}': Process exited unexpected with code {message.ExitCode}.";
                
                _callLog.Add(errorMessage);
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
            catch (Exception)
            {
                Log.Warning("Received data that appeared to be JSON but which is not deserializable: {0}", data);
                return null;
            }
        }

        public static Props Props(string executable)
            => Akka.Actor.Props.Create<PathfinderProcess>(executable);
    }
}