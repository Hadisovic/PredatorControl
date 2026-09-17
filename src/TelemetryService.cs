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
        float? GpuPowerW = null,
        float? CpuPowerW = null,
        float? VramUsedGb = null,
        float? VramTotalGb = null,
        float? RamUsedGb = null,
        float? RamTotalGb = null
    );

    [SupportedOSPlatform("windows")]
    public sealed class TelemetryService : IDisposable
    {
        [DllImport("kernel32.dll")]
        private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, nint dwMinimumWorkingSetSize, nint dwMaximumWorkingSetSize);

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

                        int? gpuUsage = nvmlGpuUsage;
                        if (!gpuUsage.HasValue && effectiveGpuTemp.HasValue && effectiveGpuTemp.Value > 0)
                        {
                            if (effectiveGpuPower.HasValue && effectiveGpuPower.Value > 0)
                            {
                                gpuUsage = (int)Math.Clamp(Math.Round((effectiveGpuPower.Value / 140.0) * 100.0), 0, 100);
                            }
                            else
                            {
                                int baseline = 40;
                                int maxTarget = 86;
                                double ratio = (effectiveGpuTemp.Value - baseline) / (double)(maxTarget - baseline);
                                gpuUsage = (int)Math.Clamp(Math.Round(ratio * 100.0), 0, 99);
                            }
                        }

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
                            ramTotalGb
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