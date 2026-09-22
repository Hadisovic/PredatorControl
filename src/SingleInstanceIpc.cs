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
            catch
            {
                // If mutex creation fails (e.g. permission restriction), treat as first instance
                isFirstInstance = true;
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

        private void StartServerLoop()
        {
            Task.Run(async () =>
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var pipeSecurity = new PipeSecurity();
                        pipeSecurity.AddAccessRule(new PipeAccessRule(
                            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                            PipeAccessRights.ReadWrite,
                            AccessControlType.Allow));

                        using var server = NamedPipeServerStreamAcl.Create(
                            PipeName,
                            PipeDirection.In,
                            1,
                            PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous,
                            0, 0,
                            pipeSecurity);

                        await server.WaitForConnectionAsync(_cts.Token);

                        using var reader = new StreamReader(server, Encoding.UTF8);
                        string? line = await reader.ReadLineAsync(_cts.Token);
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
                        try { await Task.Delay(250, _cts.Token); } catch { break; }
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
                _mutex.Dispose();
            }
        }
    }
}
