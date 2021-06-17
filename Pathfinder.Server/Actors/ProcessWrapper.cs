using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Akka.Actor;
using Akka.Event;

namespace Pathfinder.Server.Actors
{
    public class ProcessWrapper : ReceiveActor
    {
        #region Messages
        
        public abstract class ControlMessage
        {
        }
        public abstract class ControlCommand : ControlMessage
        {
        }
        public abstract class ControlEvent : ControlMessage
        {
        }
        
        public sealed class StopProcess : ControlCommand
        {
        }
        public sealed class KillProcess : ControlCommand
        {
        }
        public sealed class Exited : ControlEvent
        {
            public readonly int ExitCode;

            public Exited(int exitCode)
            {
                ExitCode = exitCode;
            }
        }
        
        public abstract class IoMessage
        {
            public string Data { get; }

            public IoMessage(string data)
            {
                Data = data;
            }
        }
        
        public abstract class IoCommand : IoMessage
        {
            protected IoCommand(string data) : base(data)
            {
            }
        }
        
        public abstract class IoEvent : IoMessage
        {
            protected IoEvent(string data) : base(data)
            {
            }
        }

        public sealed class StdIn : IoCommand
        {
            public StdIn(string data) : base(data)
            {
            }
        }
        
        public sealed class StdOut : IoEvent
        {
            public StdOut(string data) : base(data)
            {
            }
        }
        
        public sealed class StdErr : IoEvent
        {
            public StdErr(string data) : base(data)
            {
            }
        }
        
        #endregion

        private ILoggingAdapter Log { get; } = Context.GetLogger();
        
        protected override void PreStart() => Log.Info($"ProcessWrapper started. Running '{_executable}' with args '{string.Join(" ", _arguments)}'");
        protected override void PostStop()
        {
            Log.Info($"ProcessWrapper stopped. Cleaning up ..");
            if (!_process.HasExited)
            {
                Log.Warning($"ProcessWrapper stopped but the wrapped process still exists. Killing it ..");
                _process.Kill();
                Log.Warning($"ProcessWrapper stopped but the wrapped process still exists. Killing it .. Killed.");
                _process.Dispose();
            }
        }

        readonly string _executable;
        readonly ImmutableArray<string> _arguments;
        private readonly Process _process;
        
        public ProcessWrapper(
            string executable, 
            ImmutableArray<string> args, 
            string? workingDirectory, 
            IActorRef? outputReceiver)
        {
            _executable = executable;
            _arguments = args;
            
            var processStartInfo = new ProcessStartInfo(_executable);
            if (workingDirectory != null)
            {
                processStartInfo.WorkingDirectory = workingDirectory;
            }

            _arguments.ToList().ForEach(arg => processStartInfo.ArgumentList.Add(arg));

            processStartInfo.RedirectStandardInput = true;
            processStartInfo.RedirectStandardOutput = true;
            processStartInfo.RedirectStandardError = true;
            
            _process = Process.Start(processStartInfo) 
                       ?? throw new Exception("The process cannot be started. Process.Start() returned null.");
            
            Log.Info($"Started process '{_process.Id}'.");
            
            _process.OutputDataReceived += (s, e) =>
            {
                if (e.Data == null)
                    return;
                
                if (outputReceiver == null)
                    Log.Debug("StdOut: {0}", e.Data);
                else
                    outputReceiver.Tell(new StdOut(e.Data));
            };
            
            _process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data == null)
                    return;
                
                if (outputReceiver == null)
                    Log.Debug("StdErr: {0}", e.Data);
                else
                    outputReceiver.Tell(new StdErr(e.Data));
            };
            
            _process.Exited += (s, e) =>
            {
                if (outputReceiver == null)
                    Log.Info($"The process exited with code '{_process.ExitCode}'.");
                else
                    outputReceiver.Tell(new Exited(_process.ExitCode));
                
                Context.Stop(Self);
            };

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            Become(Running);
        }

        void Running()
        {
            Receive<StdIn>(message =>
            {
                _process.StandardInput.WriteLine(message.Data);
            });
            
            Receive<StopProcess>(_ =>
            {
                _process.Close();
                Become(Stopped);
            });
        }

        void Stopped()
        {
            Receive<KillProcess>(message =>
            {
                _process.Kill(true);
            });
        }
        
        public static Props Props(
            string executable, 
            ImmutableArray<string> args,
            string? workingDirectory,
            IActorRef outputReceiver) 
            => Akka.Actor.Props.Create<ProcessWrapper>(
                executable,
                args,
                workingDirectory,
                outputReceiver);
    }
}