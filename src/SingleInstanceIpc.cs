using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PredatorControlApp
{
    public sealed class SingleInstanceIpc : IDisposable
    {
        private const string MutexName = @"Global\AcerPredatorControl_SingleInstance_Mutex_999";
        private const string PipeName = "AcerPredatorControl_IPC_Pipe";

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);

        internal static bool IsCommandAllowed(string command, bool sameUser, bool sameSession, bool elevated) =>
            sameUser && sameSession && (command is "TOGGLE" or "SHOW" ||
                elevated && command is "CYCLE_MODE" or "MODE" or "EXIT" or "QUIT");

        internal static bool AuthorizeClient(NamedPipeServerStream pipe, string command)
        {
            try
            {
                using var owner = WindowsIdentity.GetCurrent();
                using var current = Process.GetCurrentProcess();
                if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint pid)) return false;
                using var client = Process.GetProcessById(checked((int)pid));
                bool sameSession = client.SessionId == current.SessionId;
                bool allowed = false;
                // Use the pipe's authenticated token, never a caller-supplied PID or secret.
                pipe.RunAsClient(() =>
                {
                    using var identity = WindowsIdentity.GetCurrent(ifImpersonating: true);
                    allowed = identity != null && IsCommandAllowed(command, identity.User == owner.User,
                        sameSession, new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator));
                });
                return allowed;
            }
            catch { return false; }
        }

        private readonly Mutex _mutex;
        private readonly CancellationTokenSource _cts = new();
        private readonly Action<string> _onCommandReceived;
        private bool _disposed;

        private SingleInstanceIpc(Mutex mutex, Action<string> onCommandReceived)
        {
            _mutex = mutex;
            _onCommandReceived = onCommandReceived;
            StartServerLoop();
        }

        public static bool TryAcquireOrSignal(string command, Action<string> onCommandReceived, out SingleInstanceIpc? instance)
        {
            instance = null;
            var mutex = TryAcquireHardwareLock();
            if (mutex == null)
            {
                // Another instance is already running; signal it via named pipe
                SignalRunningInstance(command);
                return false;
            }

            instance = new SingleInstanceIpc(mutex, onCommandReceived);
            return true;
        }

        internal static Mutex? TryAcquireHardwareLock(string name = MutexName)
        {
            try
            {
                var mutex = new Mutex(true, name, out bool created);
                if (created) return mutex;
                mutex.Dispose();
                return null;
            }
            catch (UnauthorizedAccessException) { return null; }
            // Other failures propagate: never grant primary status without a lock.
        }

        public static bool SignalRunningInstance(string command, int timeoutMs = 1200)
        {
            if (string.IsNullOrEmpty(command) || command.Length > 256 || command.IndexOfAny(['\r', '\n']) >= 0 || timeoutMs <= 0) return false;
            try
            {
                using var deadline = new CancellationTokenSource(timeoutMs);
                using var client = new NamedPipeClientStream(".", PipeName,
                    PipeAccessRights.WriteData | PipeAccessRights.ReadAttributes | PipeAccessRights.Synchronize, PipeOptions.Asynchronous,
                    TokenImpersonationLevel.Identification, HandleInheritability.None);
                client.ConnectAsync(deadline.Token).GetAwaiter().GetResult();
                // No drain/flush wait on an untrusted server; cancellation also bounds a stalled write.
                client.WriteAsync(Encoding.UTF8.GetBytes(command + "\n"), deadline.Token).AsTask().GetAwaiter().GetResult();
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static async Task<string?> ReadCommandAsync(Stream stream, CancellationToken token)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, false, 256, leaveOpen: true);
            var command = new StringBuilder(256);
            var ch = new char[1];
            while (await reader.ReadAsync(ch.AsMemory(), token) != 0)
            {
                if (ch[0] == '\n') return command.ToString().TrimEnd('\r');
                if (command.Length >= 256) return null;
                command.Append(ch[0]);
            }
            return command.Length == 0 ? null : command.ToString().TrimEnd('\r');
        }

        private void StartServerLoop()
        {
            var token = _cts.Token;
            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var pipeSecurity = new PipeSecurity();
                        pipeSecurity.SetAccessRuleProtection(true, false);
                        var currentUserSid = WindowsIdentity.GetCurrent().User;
                        if (currentUserSid != null)
                        {
                            pipeSecurity.AddAccessRule(new PipeAccessRule(
                                currentUserSid,
                                PipeAccessRights.ReadData | PipeAccessRights.WriteData | PipeAccessRights.ReadAttributes | PipeAccessRights.Synchronize,
                                AccessControlType.Allow));
                        }

                        using var server = NamedPipeServerStreamAcl.Create(
                            PipeName,
                            PipeDirection.In,
                            1,
                            PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance,
                            0, 0,
                            pipeSecurity);

                        await server.WaitForConnectionAsync(token);

                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                        deadline.CancelAfter(TimeSpan.FromSeconds(5));
                        string? line;
                        try { line = await ReadCommandAsync(server, deadline.Token); }
                        catch (OperationCanceledException) when (!_cts.IsCancellationRequested) { continue; }
                        if (!string.IsNullOrEmpty(line) && AuthorizeClient(server, line))
                        {
                            try { _onCommandReceived(line); } catch { }
                            await Task.Delay(250, token); // Bound UI dispatch from repeated local activation requests.
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                        // Delay briefly on unexpected socket/pipe error before retrying
                        try { await Task.Delay(250, token); } catch { break; }
                    }
                }
            });
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _cts.Cancel();
                _cts.Dispose();
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch { }
                _mutex?.Dispose();
            }
        }
    }
}
