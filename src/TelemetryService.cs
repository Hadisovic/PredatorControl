using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PredatorControlApp
{
    public readonly record struct TelemetrySnapshot(
        int? CpuTemp,
        int? GpuTemp,
        int? CpuFanRpm,
        int? GpuFanRpm,
        PowerLineStatus PowerLine,
        int? CpuUsage = null,
        int? GpuUsage = null,
        float? BatteryPercent = null,
        bool IsCharging = false,
        float? GpuPowerW = null,
        float? CpuPowerW = null,
        float? VramUsedGb = null,
        float? VramTotalGb = null,
        float? RamUsedGb = null,
        float? RamTotalGb = null,
        int? AuxFanRpm = null
    );

    [SupportedOSPlatform("windows")]
    public sealed class TelemetryService : IDisposable
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public static MEMORYSTATUSEX Create()
            {
                var result = new MEMORYSTATUSEX();
                result.dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
                return result;
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        private static (float? ramUsedGb, float? ramTotalGb, int? ramPercent) GetMemoryStatus()
        {
            try
            {
                var mem = MEMORYSTATUSEX.Create();
                if (GlobalMemoryStatusEx(ref mem))
                {
                    float totalGb = mem.ullTotalPhys / (1024f * 1024f * 1024f);
                    float availGb = mem.ullAvailPhys / (1024f * 1024f * 1024f);
                    float usedGb = Math.Max(0, totalGb - availGb);
                    int percent = (int)Math.Clamp(mem.dwMemoryLoad, 0, 100);
                    return (usedGb, totalGb, percent);
                }
            }
            catch { }
            return (null, null, null);
        }

        private static PerformanceCounter? _cpuPowerCounter;
        private static bool _cpuPowerInitAttempted;

        private static float? GetCpuPower()
        {
            if (!_cpuPowerInitAttempted)
            {
                _cpuPowerInitAttempted = true;
                try
                {
                    _cpuPowerCounter = new PerformanceCounter("Energy Meter", "Power", "rapl_package0_pkg", true);
                    _cpuPowerCounter.NextValue(); // Prime the initial reading
                }
                catch
                {
                    try
                    {
                        var cat = new PerformanceCounterCategory("Energy Meter");
                        var instances = cat.GetInstanceNames();
                        var pkgInst = instances.FirstOrDefault(i => i.Contains("pkg", StringComparison.OrdinalIgnoreCase));
                        if (pkgInst != null)
                        {
                            _cpuPowerCounter = new PerformanceCounter("Energy Meter", "Power", pkgInst, true);
                            _cpuPowerCounter.NextValue();
                        }
                    }
                    catch
                    {
                        _cpuPowerCounter = null;
                    }
                }
            }

            if (_cpuPowerCounter != null)
            {
                try
                {
                    float mw = _cpuPowerCounter.NextValue();
                    if (mw > 0)
                    {
                        return mw / 1000.0f; // mW to Watts
                    }
                }
                catch { }
            }

            return null;
        }

        private readonly WmiController _wmi;
        private readonly Action<TelemetrySnapshot> _onSnapshot;
        private readonly CancellationTokenSource _cts = new();
        private Task _worker = Task.CompletedTask;

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
            // Capture the token up front so the worker never touches _cts after Dispose().
            var token = _cts.Token;
            _worker = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var power = SystemInformation.PowerStatus;
                        var powerLine = power.PowerLineStatus;
                        bool isPluggedIn = powerLine == PowerLineStatus.Online;
                        float batPercent = (float)Math.Round(power.BatteryLifePercent * 100f);
                        bool isCharging = (power.BatteryChargeStatus & BatteryChargeStatus.Charging) != 0;

                        var (cpuTemp, gpuTemp, cpuRpm, gpuRpm, auxRpm, gpuPower) = _wmi.GetAllSensors(isPluggedIn, _includeGpu);

                        int? cpuUsage = CalculateCpuUsage();

                        // Query native NVIDIA NVML for real-time discrete GPU telemetry
                        float? dgpuPowerW = null;
                        int? nvmlGpuUsage = null;
                        int? nvmlGpuTemp = null;
                        float? vramUsedGb = null;
                        float? vramTotalGb = null;

                        if (_includeGpu && NvmlGpuMonitor.IsAvailable)
                        {
                            var nvml = NvmlGpuMonitor.Query();
                            if (nvml.powerW.HasValue && nvml.powerW.Value > 0)
                            {
                                dgpuPowerW = nvml.powerW.Value;
                            }
                            if (nvml.usagePercent.HasValue)
                            {
                                nvmlGpuUsage = nvml.usagePercent.Value;
                            }
                            if (nvml.tempC.HasValue && nvml.tempC.Value > 0)
                            {
                                nvmlGpuTemp = nvml.tempC.Value;
                            }
                            vramUsedGb = nvml.vramUsedGb;
                            vramTotalGb = nvml.vramTotalGb;
                        }

                        float? effectiveGpuPower = dgpuPowerW ?? (gpuPower.HasValue && gpuPower.Value > 0 ? (float)gpuPower.Value : null);
                        int? effectiveGpuTemp = (gpuTemp.HasValue && gpuTemp.Value > 0) ? gpuTemp : nvmlGpuTemp;

                        // Utilization is only reported when a real measurement (NVML) exists.
                        // Power and temperature stay separate readings; they are not turned into a fake percentage.
                        int? gpuUsage = nvmlGpuUsage;

                        float? cpuPowerW = GetCpuPower();
                        var (ramUsedGb, ramTotalGb, _) = GetMemoryStatus();

                        var snapshot = new TelemetrySnapshot(
                            cpuTemp,
                            effectiveGpuTemp,
                            cpuRpm,
                            gpuRpm,
                            powerLine,
                            cpuUsage,
                            gpuUsage,
                            batPercent,
                            isCharging,
                            effectiveGpuPower,
                            cpuPowerW,
                            vramUsedGb,
                            vramTotalGb,
                            ramUsedGb,
                            ramTotalGb,
                            auxRpm
                        );

                        // Don't publish after shutdown has begun.
                        if (token.IsCancellationRequested) break;
                        try
                        {
                            _onSnapshot(snapshot);
                        }
                        catch { }

                        await Task.Delay(_pollIntervalMs, token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Program.Report(ex, false);
                        try { await Task.Delay(2000, token); } catch { break; }
                    }
                }
            }, token);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _cts.Cancel(); } catch { }

            // Wait briefly for the worker to leave its loop before releasing the token source.
            // Never block the UI thread indefinitely: a stuck WMI call must not hang shutdown.
            bool stopped = false;
            try { stopped = _worker.Wait(TimeSpan.FromSeconds(3)); }
            catch (AggregateException) { stopped = true; } // worker faulted/cancelled == finished
            catch { }

            if (stopped)
            {
                _cts.Dispose();
            }
            else
            {
                // Worker still busy in a sensor read; dispose once it actually finishes.
                _worker.ContinueWith(_ => { try { _cts.Dispose(); } catch { } }, TaskScheduler.Default);
            }
        }
    }
}