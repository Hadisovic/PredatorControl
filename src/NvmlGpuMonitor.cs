// ====================================================================================================
// PREDATOR CONTROL · NVIDIA MANAGEMENT LIBRARY (NVML) C-API BRIDGE
// File: src/NvmlGpuMonitor.cs
//
// 📖 C++ / SYSTEMS DEVELOPER ROSETTA STONE:
// - C++ Equivalent : Direct dynamic linking to NVIDIA's native C-API (%WINDIR%\System32\nvml.dll).
// - Subsystem Role : Queries live GPU wattage, core temperatures, and utilization directly from NVIDIA drivers.
// - Security Model : Qualified LoadLibraryEx with LOAD_LIBRARY_SEARCH_SYSTEM32 to prevent DLL search-order hijacking.
//
// 🔄 C# -> C++ TYPE TRANSLATION REFERENCE:
// - [DllImport]                      => extern "C" __declspec(dllimport) or GetProcAddress
// - IntPtr device                    => nvmlDevice_t (opaque pointer to GPU handle)
// - out uint powerMilliWatts         => uint32_t* (pass by pointer/reference)
// - nvmlReturn_t (int)               => 0 = NVML_SUCCESS
// ====================================================================================================

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Diagnostics;

namespace PredatorControlApp
{
    [SupportedOSPlatform("windows")]
    public static class NvmlGpuMonitor
    {
        private const string NvmlDll = "nvml.dll";

        private const uint LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string lpLibFileName, IntPtr hFile, uint dwFlags);

        [DllImport(NvmlDll, EntryPoint = "nvmlInit_v2")]
        private static extern int nvmlInit_v2();

        [DllImport(NvmlDll, EntryPoint = "nvmlShutdown")]
        private static extern int nvmlShutdown();

        [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
        private static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);

        [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetPowerUsage")]
        private static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint powerMilliWatts);

        [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetTemperature")]
        private static extern int nvmlDeviceGetTemperature(IntPtr device, int sensorType, out uint tempC);

        [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetClockInfo")]
        private static extern int nvmlDeviceGetClockInfo(IntPtr device, int clockType, out uint clockMhz);

        private const int NvmlClockGraphics = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct NvmlUtilization
        {
            public uint gpu;
            public uint memory;
        }

        [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetUtilizationRates")]
        private static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out NvmlUtilization utilization);

        [StructLayout(LayoutKind.Sequential)]
        private struct NvmlMemory
        {
            public ulong total;
            public ulong free;
            public ulong used;
        }

        [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetMemoryInfo")]
        private static extern int nvmlDeviceGetMemoryInfo(IntPtr device, out NvmlMemory memory);

        private static readonly object _lock = new();
        private static bool _initialized;
        private static IntPtr _library;
        private static long _retryAfter;
        private static IntPtr _deviceHandle = IntPtr.Zero;

        public static bool IsAvailable
        {
            get
            {
                EnsureInitialized();
                return _initialized && _deviceHandle != IntPtr.Zero;
            }
        }

        private static void EnsureInitialized()
        {
            lock (_lock)
            {
                if (_initialized || Stopwatch.GetTimestamp() < _retryAfter) return;
                _retryAfter = Stopwatch.GetTimestamp() + 30 * Stopwatch.Frequency;

                try
                {
                    // Load nvml.dll strictly via fully-qualified System32 path with LOAD_LIBRARY_SEARCH_SYSTEM32
                    string sys32Nvml = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvml.dll");
                    if (_library == IntPtr.Zero)
                        _library = LoadLibraryEx(sys32Nvml, IntPtr.Zero, LOAD_LIBRARY_SEARCH_SYSTEM32);
                    if (_library == IntPtr.Zero) return;

                    int rc = nvmlInit_v2();
                    if (rc != 0) // NVML_SUCCESS = 0
                    {
                        return;
                    }

                    rc = nvmlDeviceGetHandleByIndex_v2(0, out _deviceHandle);
                    if (rc != 0 || _deviceHandle == IntPtr.Zero)
                    {
                        nvmlShutdown();
                        _deviceHandle = IntPtr.Zero;
                        return;
                    }

                    _initialized = true;
                }
                catch
                {
                    _deviceHandle = IntPtr.Zero;
                }
            }
        }

        public static (float? powerW, int? usagePercent, int? tempC, float? vramUsedGb, float? vramTotalGb, int? clockMhz) Query()
        {
            if (!IsAvailable)
            {
                return (null, null, null, null, null, null);
            }

            lock (_lock)
            {
                if (!_initialized || _deviceHandle == IntPtr.Zero) return (null, null, null, null, null, null);
                try
                {
                    float? powerW = null;
                    int powerStatus = nvmlDeviceGetPowerUsage(_deviceHandle, out uint mw);
                    if (powerStatus == 0 && mw is > 0 and <= 1000000)
                    {
                        powerW = mw / 1000.0f;
                    }

                    int? usage = null;
                    int usageStatus = nvmlDeviceGetUtilizationRates(_deviceHandle, out var util);
                    if (usageStatus == 0 && util.gpu <= 100)
                    {
                        usage = (int)util.gpu;
                    }

                    int? temp = null;
                    int tempStatus = nvmlDeviceGetTemperature(_deviceHandle, 0, out uint t);
                    if (tempStatus == 0 && t is >= 1 and <= 125)
                    {
                        temp = (int)t;
                    }

                    int? clockMhz = null;
                    int clockStatus = nvmlDeviceGetClockInfo(_deviceHandle, NvmlClockGraphics, out uint clock);
                    if (clockStatus == 0 && clock is >= 100 and <= 10000)
                    {
                        clockMhz = (int)clock;
                    }

                    float? vramUsed = null;
                    float? vramTotal = null;
                    int memoryStatus = nvmlDeviceGetMemoryInfo(_deviceHandle, out var mem);
                    if (memoryStatus == 0 && mem.total > 0 && mem.used <= mem.total)
                    {
                        vramUsed = mem.used / (1024f * 1024f * 1024f);
                        vramTotal = mem.total / (1024f * 1024f * 1024f);
                    }

                    if (NeedsReconnect(powerStatus) || NeedsReconnect(usageStatus) || NeedsReconnect(tempStatus) ||
                        NeedsReconnect(clockStatus) || NeedsReconnect(memoryStatus))
                    {
                        ReconnectLater();
                        return (null, null, null, null, null, null);
                    }
                    return (powerW, usage, temp, vramUsed, vramTotal, clockMhz);
                }
                catch
                {
                    ReconnectLater();
                    return (null, null, null, null, null, null);
                }
            }
        }

        // NVML: uninitialized, invalid argument (stale handle), GPU lost, reset required, unknown.
        // Not-supported channels are left missing and do not repeatedly initialize the driver.
        internal static bool NeedsReconnect(int status) => status is 1 or 2 or 15 or 16 or 999;

        private static void ReconnectLater()
        {
            if (_initialized) try { nvmlShutdown(); } catch { }
            _initialized = false;
            _deviceHandle = IntPtr.Zero;
            _retryAfter = Stopwatch.GetTimestamp() + 5 * Stopwatch.Frequency;
        }

        public static void Shutdown()
        {
            lock (_lock)
            {
                if (_initialized)
                {
                    try { nvmlShutdown(); } catch { }
                    _initialized = false;
                    _deviceHandle = IntPtr.Zero;
                }
                _retryAfter = 0;
            }
        }
    }
}
