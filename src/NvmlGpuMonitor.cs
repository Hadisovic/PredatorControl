using System.Runtime.InteropServices;
using System.Runtime.Versioning;

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
        private static bool _initFailed;
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
            if (_initialized || _initFailed) return;

            lock (_lock)
            {
                if (_initialized || _initFailed) return;

                try
                {
                    // Load nvml.dll strictly via fully-qualified System32 path with LOAD_LIBRARY_SEARCH_SYSTEM32
                    string sys32Nvml = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvml.dll");
                    IntPtr hLib = LoadLibraryEx(sys32Nvml, IntPtr.Zero, LOAD_LIBRARY_SEARCH_SYSTEM32);
                    if (hLib == IntPtr.Zero)
                    {
                        _initFailed = true;
                        return;
                    }

                    int rc = nvmlInit_v2();
                    if (rc != 0) // NVML_SUCCESS = 0
                    {
                        _initFailed = true;
                        return;
                    }

                    rc = nvmlDeviceGetHandleByIndex_v2(0, out _deviceHandle);
                    if (rc != 0 || _deviceHandle == IntPtr.Zero)
                    {
                        _initFailed = true;
                        return;
                    }

                    _initialized = true;
                }
                catch
                {
                    _initFailed = true;
                }
            }
        }

        public static (float? powerW, int? usagePercent, int? tempC, float? vramUsedGb, float? vramTotalGb) Query()
        {
            if (!IsAvailable)
            {
                return (null, null, null, null, null);
            }

            lock (_lock)
            {
                try
                {
                    float? powerW = null;
                    if (nvmlDeviceGetPowerUsage(_deviceHandle, out uint mw) == 0 && mw > 0)
                    {
                        powerW = mw / 1000.0f;
                    }

                    int? usage = null;
                    if (nvmlDeviceGetUtilizationRates(_deviceHandle, out var util) == 0)
                    {
                        usage = (int)Math.Clamp(util.gpu, 0, 100);
                    }

                    int? temp = null;
                    if (nvmlDeviceGetTemperature(_deviceHandle, 0, out uint t) == 0 && t > 0)
                    {
                        temp = (int)t;
                    }

                    float? vramUsed = null;
                    float? vramTotal = null;
                    if (nvmlDeviceGetMemoryInfo(_deviceHandle, out var mem) == 0 && mem.total > 0)
                    {
                        vramUsed = mem.used / (1024f * 1024f * 1024f);
                        vramTotal = mem.total / (1024f * 1024f * 1024f);
                    }

                    return (powerW, usage, temp, vramUsed, vramTotal);
                }
                catch
                {
                    return (null, null, null, null, null);
                }
            }
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
            }
        }
    }
}
