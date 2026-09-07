using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PredatorControlApp
{
    public record TelemetrySnapshot(
        int? CpuTemp,
        int? GpuTemp,
        int? CpuFanRpm,
        int? GpuFanRpm,
        PowerLineStatus PowerLine
    );

    [SupportedOSPlatform("windows")]
    public sealed class TelemetryService : IDisposable
    {
        [DllImport("kernel32.dll")]
        private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, nint dwMinimumWorkingSetSize, nint dwMaximumWorkingSetSize);

        private readonly WmiController _wmi;
        private readonly Action<TelemetrySnapshot> _onSnapshot;
        private readonly CancellationTokenSource _cts = new();

        private volatile int _pollIntervalMs = 1000;
        private volatile bool _isFastPolling = true;
        // Sensor mask: when false, GPU sensor reads are skipped entirely.
        // Set to false by Form1 when the window is hidden and no external display
        // panel is visible — reduces EC accesses by 50% in background mode.
        private volatile bool _includeGpu = true;
        private bool _disposed;

        public TelemetryService(WmiController wmi, Action<TelemetrySnapshot> onSnapshot)
        {
            _wmi = wmi;
            _onSnapshot = onSnapshot;
            StartWorkerLoop();
        }

        public void SetPollingState(bool isFast)
        {
            _isFastPolling = isFast;
            _pollIntervalMs = isFast ? 1000 : 10000;

            if (!isFast)
            {
                TrimWorkingSet();
            }
        }

        /// <summary>
        /// Controls whether GPU temperature and fan RPM are read each poll cycle.
        /// Set false when the window is hidden to halve background EC bus activity.
        /// </summary>
        public void SetSensorMask(bool includeGpu)
        {
            _includeGpu = includeGpu;
        }

        public static void TrimWorkingSet()
        {
            try
            {
                GC.Collect(2, GCCollectionMode.Optimized, false, false);
                GC.WaitForPendingFinalizers();
                using var process = Process.GetCurrentProcess();
                SetProcessWorkingSetSize(process.Handle, -1, -1);
            }
            catch { }
        }

        private void StartWorkerLoop()
        {
            Task.Run(async () =>
            {
                int idleTicks = 0;
                while (!_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var powerLine = SystemInformation.PowerStatus.PowerLineStatus;
                        bool isPluggedIn = powerLine == PowerLineStatus.Online;

                        var (cpuTemp, gpuTemp, cpuRpm, gpuRpm) = _wmi.GetAllSensors(isPluggedIn, _includeGpu);

                        var snapshot = new TelemetrySnapshot(cpuTemp, gpuTemp, cpuRpm, gpuRpm, powerLine);

                        try
                        {
                            _onSnapshot(snapshot);
                        }
                        catch { }

                        if (!_isFastPolling)
                        {
                            if (++idleTicks >= 6) // Every ~60s while running in background
                            {
                                idleTicks = 0;
                                TrimWorkingSet();
                            }
                        }
                        else
                        {
                            idleTicks = 0;
                        }

                        await Task.Delay(_pollIntervalMs, _cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Program.Report(ex, false);
                        try { await Task.Delay(2000, _cts.Token); } catch { break; }
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
            }
        }
    }
}