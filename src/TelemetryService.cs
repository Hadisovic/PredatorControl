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
        PowerLineStatus PowerLine,
        int? CpuUsage = null,
        int? GpuUsage = null,
        float? BatteryPercent = null,
        bool IsCharging = false,
        float? GpuPowerW = null
    );

    [SupportedOSPlatform("windows")]
    public sealed class TelemetryService : IDisposable
    {
        [DllImport("kernel32.dll")]
        private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, nint dwMinimumWorkingSetSize, nint dwMaximumWorkingSetSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

        private readonly WmiController _wmi;
        private readonly Action<TelemetrySnapshot> _onSnapshot;
        private readonly CancellationTokenSource _cts = new();

        private volatile int _pollIntervalMs = 1000;
        private volatile bool _isFastPolling = true;
        private volatile bool _includeGpu = true;
        private volatile bool _isOverlayActive = false;
        private bool _disposed;

        private long _prevIdleTime;
        private long _prevKernelTime;
        private long _prevUserTime;

        public TelemetryService(WmiController wmi, Action<TelemetrySnapshot> onSnapshot)
        {
            _wmi = wmi;
            _onSnapshot = onSnapshot;
            StartWorkerLoop();
        }

        public void SetPollingState(bool isFast)
        {
            _isFastPolling = isFast;
            UpdatePollInterval();

            if (!isFast && !_isOverlayActive)
            {
                TrimWorkingSet();
            }
        }

        public void SetOverlayActive(bool active)
        {
            _isOverlayActive = active;
            if (active) _includeGpu = true;
            UpdatePollInterval();
        }

        private void UpdatePollInterval()
        {
            if (_isOverlayActive)
                _pollIntervalMs = 500;
            else if (_isFastPolling)
                _pollIntervalMs = 1000;
            else
                _pollIntervalMs = 10000;
        }

        public void SetSensorMask(bool includeGpu)
        {
            if (_isOverlayActive)
            {
                _includeGpu = true;
                return;
            }
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

        private int? CalculateCpuUsage()
        {
            if (!GetSystemTimes(out long idleTime, out long kernelTime, out long userTime))
                return null;

            if (_prevKernelTime == 0 && _prevUserTime == 0)
            {
                _prevIdleTime = idleTime;
                _prevKernelTime = kernelTime;
                _prevUserTime = userTime;
                return null;
            }

            long usr = userTime - _prevUserTime;
            long ker = kernelTime - _prevKernelTime;
            long idl = idleTime - _prevIdleTime;

            _prevIdleTime = idleTime;
            _prevKernelTime = kernelTime;
            _prevUserTime = userTime;

            long sys = ker + usr;
            if (sys <= 0) return null;

            int usage = (int)Math.Clamp(((sys - idl) * 100.0) / sys, 0, 100);
            return usage;
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
                        var power = SystemInformation.PowerStatus;
                        var powerLine = power.PowerLineStatus;
                        bool isPluggedIn = powerLine == PowerLineStatus.Online;
                        float batPercent = (float)Math.Round(power.BatteryLifePercent * 100f);
                        bool isCharging = (power.BatteryChargeStatus & BatteryChargeStatus.Charging) != 0;

                        var (cpuTemp, gpuTemp, cpuRpm, gpuRpm, gpuPower) = _wmi.GetAllSensors(isPluggedIn, _includeGpu);

                        int? cpuUsage = CalculateCpuUsage();

                        // If GPU is active (reporting temp or RPM), estimate GPU usage based on temps and power
                        // or calculate from performance counters
                        int? gpuUsage = null;
                        if (gpuTemp.HasValue && gpuTemp.Value > 0)
                        {
                            if (gpuPower.HasValue && gpuPower.Value > 0)
                            {
                                // Typical Acer TGP is 140W max on RTX 4060/4070/4080
                                gpuUsage = (int)Math.Clamp(Math.Round((gpuPower.Value / 140.0) * 100.0), 0, 100);
                            }
                            else
                            {
                                // Proportional estimate when GPU is warm and active
                                int baseline = 40;
                                int maxTarget = 86;
                                double ratio = (gpuTemp.Value - baseline) / (double)(maxTarget - baseline);
                                gpuUsage = (int)Math.Clamp(Math.Round(ratio * 100.0), 0, 99);
                            }
                        }

                        var snapshot = new TelemetrySnapshot(
                            cpuTemp,
                            gpuTemp,
                            cpuRpm,
                            gpuRpm,
                            powerLine,
                            cpuUsage,
                            gpuUsage,
                            batPercent,
                            isCharging,
                            gpuPower.HasValue ? (float)gpuPower.Value : null
                        );

                        try
                        {
                            _onSnapshot(snapshot);
                        }
                        catch { }

                        if (!_isFastPolling && !_isOverlayActive)
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