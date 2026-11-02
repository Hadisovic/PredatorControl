using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace PredatorControlApp
{
    public sealed class SingleInstanceIpc : IDisposable
    {
        private const string MutexName = @"Global\AcerPredatorControl_SingleInstance_Mutex_999";
        private const string PipeName = "AcerPredatorControl_IPC_Pipe";

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
            Mutex? mutex = null;
            bool isFirstInstance = false;

            try
            {
                mutex = new Mutex(true, MutexName, out isFirstInstance);
            }
            catch (AbandonedMutexException)
            {
                isFirstInstance = true;
            }
            catch (UnauthorizedAccessException)
            {
                // Access denied on existing mutex: never grant primary status to a secondary process
                isFirstInstance = false;
            }
            catch
            {
                mutex?.Dispose();
                throw; // Never start hardware control without an established single-instance lock.
            }

            if (!isFirstInstance)
            {
                // Another instance is already running; signal it via named pipe
                SignalRunningInstance(command);
                mutex?.Dispose();
                return false;
            }

            instance = new SingleInstanceIpc(mutex!, onCommandReceived);
            return true;
        }

        public static bool SignalRunningInstance(string command, int timeoutMs = 1200)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                client.Connect(timeoutMs);
                using var writer = new StreamWriter(client, Encoding.UTF8, bufferSize: 256, leaveOpen: false);
                writer.WriteLine(command);
                writer.Flush();
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
                        var currentUserSid = WindowsIdentity.GetCurrent().User;
                        if (currentUserSid != null)
                        {
                            pipeSecurity.AddAccessRule(new PipeAccessRule(
                                currentUserSid,
                                PipeAccessRights.ReadWrite,
                                AccessControlType.Allow));
                        }

                        using var server = NamedPipeServerStreamAcl.Create(
                            PipeName,
                            PipeDirection.In,
                            1,
                            PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous,
                            0, 0,
                            pipeSecurity);

                        await server.WaitForConnectionAsync(token);

                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                        deadline.CancelAfter(TimeSpan.FromSeconds(5));
                        string? line;
                        try { line = await ReadCommandAsync(server, deadline.Token); }
                        catch (OperationCanceledException) when (!_cts.IsCancellationRequested) { continue; }
                        if (!string.IsNullOrEmpty(line))
                        {
                            try { _onCommandReceived(line); } catch { }
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
