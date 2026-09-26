using System.IO.Pipes;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.ServiceProcess;
using Microsoft.Win32;

namespace PredatorControlApp
{
    public enum AcerChassisFamily
    {
        Predator,
        Nitro,
        GenericAcer
    }

    public record HardwareCapabilities
    {
        public AcerChassisFamily ChassisFamily { get; init; }
        public bool SupportsRgbLighting { get; init; }
        public bool SupportsCoolBoost { get; init; }
        public bool SupportsBatteryControl { get; init; }
        public bool SupportsGpuMux { get; init; }
        public bool SupportsLcdOverdrive { get; init; }
        public int PowerModeCount { get; init; }
        public string[] PowerModeLabels { get; init; } = Array.Empty<string>();
        public byte[] PowerModeValues { get; init; } = Array.Empty<byte>();
    }

    [SupportedOSPlatform("windows")]
    public class WmiController : IDisposable
    {
        private ManagementObject? _cachedObj;
        private readonly object _lock = new();
        private bool _disposed;

        // Consecutive failure tracking — only evict cache after 2+ failures in a row
        private int _consecutiveFailures = 0;
        private const int MaxFailuresBeforeEviction = 2;
        // Pre-warm flag: avoids blocking hot path while cache is being rebuilt
        private volatile bool _cacheRebuilding = false;

        [DllImport("powrprof.dll")]
        private static extern uint PowerSetActiveOverlayScheme(Guid scheme);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool WritePrivateProfileString(string lpAppName, string? lpKeyName, string? lpString, string lpFileName);

        private static readonly Guid OVERLAY_EFFICIENCY = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
        private static readonly Guid OVERLAY_BALANCED = new("00000000-0000-0000-0000-000000000000");
        private static readonly Guid OVERLAY_PERFORMANCE = new("ded574b5-45a0-4f42-8737-46345c09c238");

        private byte _lastR = 0, _lastG = 230, _lastB = 180;
        private byte _brightness = 100;
        private byte _speed = 5;
        private byte _direction = 0;
        private int _lastMode = 0;

        // 4-zone (or 3-zone mapped) keyboard colors
        private Color[] _zoneColors = new Color[4]
        {
            Color.FromArgb(0, 230, 180),
            Color.FromArgb(0, 230, 180),
            Color.FromArgb(0, 230, 180),
            Color.FromArgb(0, 230, 180)
        };

        private byte _customCpuFanSpeed = 50;
        private byte _customGpuFanSpeed = 50;
        private bool _backlight30s = true;
        // Background worker queue for zero-latency instantaneous lighting updates
        private Action? _pendingLightingAction;
        private readonly object _lightingLock = new();
        private bool _isLightingWorkerRunning;


        public byte LastR => _lastR;
        public byte LastG => _lastG;
        public byte LastB => _lastB;
        public byte Brightness => _brightness;
        public byte Speed => _speed;
        public byte Direction => _direction;
        public int LastRgbMode => _lastMode;
        public Color[] ZoneColors => _zoneColors;
        public byte CustomCpuFanSpeed => _customCpuFanSpeed;
        public byte CustomGpuFanSpeed => _customGpuFanSpeed;
        public bool Backlight30s => _backlight30s;

        private static readonly string[] KnownWmiClasses =
        {
            "AcerGamingFunction",
            "Acer_GamingFunction",
            "AcerGamingFunctionV2",
            "Acer_GamingFunction_V2"
        };
        private string? _discoveredClassName;

        public string DiscoveredWmiClass => _discoveredClassName ?? "AcerGamingFunction";

        private ManagementObject? GetWmiObject()
        {
            lock (_lock)
            {
                if (_disposed) return null;
                if (_cachedObj != null) return _cachedObj;
                if (_cacheRebuilding) return null; // Skip rather than block during pre-warm

                try
                {
                    // Direct fast-path if class was already discovered
                    if (_discoveredClassName != null)
                    {
                        try
                        {
                            using var searcher = new ManagementObjectSearcher(@"root\WMI", $"SELECT * FROM {_discoveredClassName}");
                            using var results = searcher.Get();
                            using var enumerator = results.GetEnumerator();
                            if (enumerator.MoveNext() && enumerator.Current is ManagementObject obj)
                            {
                                _cachedObj = obj;
                                _consecutiveFailures = 0;
                                return _cachedObj;
                            }
                        }
                        catch { }
                    }

                    // Probe known classes in order of generation
                    foreach (var className in KnownWmiClasses)
                    {
                        try
                        {
                            using var searcher = new ManagementObjectSearcher(@"root\WMI", $"SELECT * FROM {className}");
                            using var results = searcher.Get();
                            using var enumerator = results.GetEnumerator();
                            if (enumerator.MoveNext() && enumerator.Current is ManagementObject obj)
                            {
                                _cachedObj = obj;
                                _discoveredClassName = className;
                                _consecutiveFailures = 0;
                                break;
                            }
                        }
                        catch { }
                    }
                }
                catch
                {
                    _cachedObj = null;
                }

                return _cachedObj;
            }
        }

        private void InvalidateCache()
        {
            lock (_lock)
            {
                _consecutiveFailures++;

                // Only evict the cache after MaxFailuresBeforeEviction consecutive failures.
                // This prevents a single transient WMI timeout from triggering an expensive
                // 200-400ms cache rebuild on the very next command.
                if (_consecutiveFailures < MaxFailuresBeforeEviction)
                    return;

                _consecutiveFailures = 0;
                try { _cachedObj?.Dispose(); } catch { }
                _cachedObj = null;
            }

            // Pre-warm the cache off the hot path so the next command is fast
            if (!_cacheRebuilding)
            {
                _cacheRebuilding = true;
                Task.Run(() =>
                {
                    try { GetWmiObject(); } catch { }
                    finally { _cacheRebuilding = false; }
                });
            }
        }

        private (bool success, ulong output) SendCommand(string method, ulong input)
        {
            lock (_lock)
            {
                try
                {
                    var obj = GetWmiObject();
                    if (obj == null) return (false, 0);

                    using var inParams = obj.GetMethodParameters(method);
                    inParams["gmInput"] = input;

                    using var outParams = obj.InvokeMethod(method, inParams, null);
                    if (outParams?["gmOutput"] == null) return (false, 0);

                    ulong result = Convert.ToUInt64(outParams["gmOutput"]);
                    bool ok = (result & 0xFF) == 0;

                    if (ok)
                    {
                        _consecutiveFailures = 0;
                        return (true, result);
                    }

                    // EC rejected the command (non-zero status byte) — retry once after 20ms
                    // This handles transient EC-busy states without meaningfully increasing
                    // latency on the success path.
                    Thread.Sleep(20);
                    using var retryIn = obj.GetMethodParameters(method);
                    retryIn["gmInput"] = input;
                    using var retryOut = obj.InvokeMethod(method, retryIn, null);
                    if (retryOut?["gmOutput"] == null) return (false, 0);
                    ulong retryResult = Convert.ToUInt64(retryOut["gmOutput"]);
                    _consecutiveFailures = 0;
                    return ((retryResult & 0xFF) == 0, retryResult);
                }
                catch
                {
                    InvalidateCache();
                    return (false, 0);
                }
            }
        }

        private bool SendLedCommand(byte[] payload)
        {
            lock (_lock)
            {
                try
                {
                    var obj = GetWmiObject();
                    if (obj == null) return false;

                    using var inParams = obj.GetMethodParameters("SetGamingKBBacklight");
                    inParams["gmInput"] = payload;

                    using var outParams = obj.InvokeMethod("SetGamingKBBacklight", inParams, null);
                    if (outParams?["gmOutput"] == null) return false;

                    ulong result = Convert.ToUInt64(outParams["gmOutput"]);
                    bool ok = (result & 0xFF) == 0;

                    if (ok) { _consecutiveFailures = 0; return true; }

                    // Single retry on EC rejection
                    Thread.Sleep(20);
                    using var retryIn = obj.GetMethodParameters("SetGamingKBBacklight");
                    retryIn["gmInput"] = payload;
                    using var retryOut = obj.InvokeMethod("SetGamingKBBacklight", retryIn, null);
                    if (retryOut?["gmOutput"] == null) return false;
                    ulong retryResult = Convert.ToUInt64(retryOut["gmOutput"]);
                    _consecutiveFailures = 0;
                    return (retryResult & 0xFF) == 0;
                }
                catch
                {
                    InvalidateCache();
                    return false;
                }
            }
        }

        private bool SendGamingRgbKbCommand(byte[] payload)
        {
            lock (_lock)
            {
                try
                {
                    var obj = GetWmiObject();
                    if (obj == null) return false;

                    // 1. Primary for modern 4-zone (Helios 16/18, Triton, Nitro 16/17)
                    try
                    {
                        using var inParams = obj.GetMethodParameters("SetGamingRgbKb");
                        if (inParams != null)
                        {
                            if (inParams.Properties["gmInput"]?.IsArray == true)
                            {
                                inParams["gmInput"] = payload;
                            }
                            else
                            {
                                ulong val = 0;
                                for (int i = 0; i < Math.Min(payload.Length, 8); i++)
                                    val |= ((ulong)payload[i]) << (i * 8);
                                inParams["gmInput"] = val;
                            }

                            using var outParams = obj.InvokeMethod("SetGamingRgbKb", inParams, null);
                            if (outParams?["gmOutput"] != null)
                            {
                                ulong result = Convert.ToUInt64(outParams["gmOutput"]);
                                if ((result & 0xFF) == 0) return true;
                            }
                        }
                    }
                    catch { }

                    // 2. Fallback for older generations (Nitro 5 / Helios 300) using SetGamingKBBacklight
                    try
                    {
                        using var bkIn = obj.GetMethodParameters("SetGamingKBBacklight");
                        if (bkIn != null)
                        {
                            ulong val = 0;
                            for (int i = 0; i < Math.Min(payload.Length, 8); i++)
                                val |= ((ulong)payload[i]) << (i * 8);
                            bkIn["gmInput"] = val;
                            using var bkOut = obj.InvokeMethod("SetGamingKBBacklight", bkIn, null);
                            if (bkOut?["gmOutput"] != null)
                            {
                                ulong res = Convert.ToUInt64(bkOut["gmOutput"]);
                                return (res & 0xFF) == 0;
                            }
                        }
                    }
                    catch { }

                    return false;
                }
                catch
                {
                    InvalidateCache();
                    return false;
                }
            }
        }

        private int? GetSensorReading(ulong sensorId)
        {
            lock (_lock)
            {
                try
                {
                    var obj = GetWmiObject();
                    if (obj == null) return null;

                    using var inParams = obj.GetMethodParameters("GetGamingSysInfo");
                    inParams["gmInput"] = (ulong)(0x0001 | (sensorId << 8));

                    using var outParams = obj.InvokeMethod("GetGamingSysInfo", inParams, null);
                    if (outParams?["gmOutput"] == null) return null;

                    ulong raw = Convert.ToUInt64(outParams["gmOutput"]);
                    if ((raw & 0xFF) == 0)
                    {
                        int reading = (int)((raw >> 8) & 0xFFFF);
                        return reading > 0 ? reading : null;
                    }
                }
                catch (ManagementException) { InvalidateCache(); }
                catch (COMException) { InvalidateCache(); }
                catch { }

                return null;
            }
        }

        public (int? cpuTemp, int? gpuTemp, int? cpuFanRpm, int? gpuFanRpm, int? gpuPowerW) GetAllSensors(bool isPluggedIn, bool includeGpu = true)
        {
            int? cpuTemp = CpuTemp;
            int? cpuRpm = CpuFanRpm;
            int? gpuTemp = (isPluggedIn && includeGpu) ? GpuTemp : null;
            int? gpuRpm = (isPluggedIn && includeGpu) ? GpuFanRpm : null;
            int? gpuPower = (isPluggedIn && includeGpu) ? GpuPowerW : null;

            return (cpuTemp, gpuTemp, cpuRpm, gpuRpm, gpuPower);
        }

        public int? CpuTemp => GetSensorReading(0x01);
        public int? GpuTemp => GetSensorReading(0x0A);
        public int? CpuFanRpm => GetSensorReading(0x02);
        public int? GpuFanRpm => GetSensorReading(0x06);
        public int? AuxFanRpm => GetSensorReading(0x07) ?? GetSensorReading(0x0B);
        public bool HasAuxFan => AuxFanRpm.HasValue && AuxFanRpm.Value > 0;
        public int? GpuPowerW => GetSensorReading(0x0D);

        private AcerChassisFamily? _chassisFamily;
        public AcerChassisFamily ChassisFamily
        {
            get
            {
                if (_chassisFamily.HasValue) return _chassisFamily.Value;
                var (_, model, _, _) = GetSystemIdentity();
                if (model.Contains("Nitro", StringComparison.OrdinalIgnoreCase) ||
                    model.StartsWith("AN", StringComparison.OrdinalIgnoreCase))
                {
                    _chassisFamily = AcerChassisFamily.Nitro;
                }
                else if (model.Contains("Predator", StringComparison.OrdinalIgnoreCase) ||
                         model.Contains("Helios", StringComparison.OrdinalIgnoreCase) ||
                         model.Contains("Triton", StringComparison.OrdinalIgnoreCase) ||
                         model.StartsWith("PH", StringComparison.OrdinalIgnoreCase) ||
                         model.StartsWith("PT", StringComparison.OrdinalIgnoreCase))
                {
                    _chassisFamily = AcerChassisFamily.Predator;
                }
                else
                {
                    _chassisFamily = AcerChassisFamily.GenericAcer;
                }
                return _chassisFamily.Value;
            }
        }

        private HardwareCapabilities? _cachedCapabilities;
        public HardwareCapabilities Capabilities => _cachedCapabilities ??= ProbeCapabilities();

        private HardwareCapabilities ProbeCapabilities()
        {
            var chassis = ChassisFamily;

            // 1. RGB Lighting probe (100% generic, zero hardcoding):
            // Predator chassis has multi-zone/per-key RGB.
            // Nitro and generic Acer laptops have 4-zone RGB only if the RGB subsystem is installed
            // (AcerLightingService service/files/registry or physical ITE RGB keyboard controller).
            // Monochrome models (red/blue/white single-zone) do not have this hardware.
            bool rgbSupported = ProbeRgbHardwareSupported(chassis);

            // 2. CoolBoost probe (Signature feature on Acer Nitro models)
            bool coolBoostSupported = chassis == AcerChassisFamily.Nitro;

            // 3. Battery Control probe
            bool batterySupported = IsBatteryControlSupported();

            // 4. Power Modes definition
            int modeCount = chassis == AcerChassisFamily.Nitro ? 3 : 5;
            string[] modeLabels = chassis == AcerChassisFamily.Nitro
                ? new[] { "Quiet", "Default", "Performance" }
                : new[] { "Quiet", "Balanced", "Perf", "Turbo", "Eco" };
            byte[] modeValues = chassis == AcerChassisFamily.Nitro
                ? new byte[] { 0x00, 0x01, 0x04 }
                : new byte[] { 0x00, 0x01, 0x04, 0x05, 0x06 };

            return new HardwareCapabilities
            {
                ChassisFamily = chassis,
                SupportsRgbLighting = rgbSupported,
                SupportsCoolBoost = coolBoostSupported,
                SupportsBatteryControl = batterySupported,
                SupportsGpuMux = false,
                SupportsLcdOverdrive = chassis == AcerChassisFamily.Predator,
                PowerModeCount = modeCount,
                PowerModeLabels = modeLabels,
                PowerModeValues = modeValues
            };
        }

        private static bool ProbeRgbHardwareSupported(AcerChassisFamily chassis)
        {
            if (chassis == AcerChassisFamily.Predator)
                return true;

            try
            {
                // Check 1: Windows Service 'AcerLightingService' exists in SCM
                try
                {
                    using var sc = new ServiceController("AcerLightingService");
                    _ = sc.Status;
                    return true;
                }
                catch { }

                // Check 2: AcerLightingService directory structure on disk
                if (Directory.Exists(@"C:\ProgramData\OEM\AcerLightingService") ||
                    Directory.Exists(@"C:\Program Files\OEM\AcerLightingService") ||
                    Directory.Exists(@"C:\Program Files (x86)\OEM\AcerLightingService"))
                {
                    return true;
                }

                // Check 3: OEM Registry registration for Acer Lighting
                using (var reg = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\OEM\AcerLightingService"))
                {
                    if (reg != null) return true;
                }
                using (var reg = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\OEM\AcerLightingService"))
                {
                    if (reg != null) return true;
                }

                // Check 4: Physical ITE 829X / 891X RGB keyboard controller device
                try
                {
                    using var searcher = new ManagementObjectSearcher(@"root\CIMV2",
                        "SELECT DeviceID FROM Win32_PnPEntity WHERE DeviceID LIKE '%VID_048D&PID_829%' OR DeviceID LIKE '%VID_048D&PID_891%'");
                    using var results = searcher.Get();
                    if (results.Count > 0) return true;
                }
                catch { }
            }
            catch { }

            return false;
        }

        private bool _coolBoost;
        public bool CoolBoost => _coolBoost;

        public bool SetCoolBoost(bool enable)
        {
            _coolBoost = enable;

            // 1. Dual-dispatch via AcerAgentClient (TCP 46933 & named pipes)
            _ = Task.Run(async () =>
            {
                try { await AcerAgentClient.SetCoolBoostAsync(enable); } catch { }
            });

            // 2. Direct named pipe dispatch to NitroSense & SystemMonitoring services
            Task.Run(() =>
            {
                try
                {
                    string[] pipeNames = { "nitrosense_hardware_service_", "systemmonitoring_hardware_service_", "predatorsense_hardware_service_" };
                    string json = enable ? "{\"Function\":\"COOL_BOOST\",\"Parameter\":{\"status\":1}}" : "{\"Function\":\"COOL_BOOST\",\"Parameter\":{\"status\":0}}";
                    byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);
                    byte[] packet = new byte[8 + jsonBytes.Length];
                    System.Text.Encoding.ASCII.GetBytes("ACER").CopyTo(packet, 0);
                    BitConverter.GetBytes((uint)100).CopyTo(packet, 4); // CMD_SET_DEVICE_DATA = 100
                    Buffer.BlockCopy(jsonBytes, 0, packet, 8, jsonBytes.Length);

                    foreach (var pipe in pipeNames)
                    {
                        try
                        {
                            using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut);
                            client.Connect(150);
                            client.Write(packet, 0, packet.Length);
                            client.Flush();
                            break;
                        }
                        catch { }
                    }
                }
                catch { }
            });

            // 3. Direct WMI ACPI dispatch: SetGamingFanBehavior (triggers CoolBoost ceiling in EC)
            ulong flag1 = enable ? 0x01000000UL : 0x00000000UL;
            ulong flag2 = enable ? 0x00000100UL : 0x00000000UL;
            var (ok1, _) = SendCommand("SetGamingFanBehavior", 0x09UL | flag1);
            var (ok2, _) = SendCommand("SetGamingFanBehavior", 0x09UL | flag2);
            return ok1 || ok2;
        }

        public bool SetMonochromeBacklight(byte brightness)
        {
            _brightness = brightness;
            uint hotkeyNum = GetBkHotkeyNumber();
            uint low32 = 0x80002 | (hotkeyNum << 8);
            uint high32 = (_backlight30s ? 0x1E00U : 0x0000U) | brightness;
            ulong uiInput = ((ulong)high32 << 32) | (ulong)low32;

            bool anySuccess = false;

            // 1. Direct WMI SetGamingKBBacklight command
            try
            {
                byte[] ledPayload = new byte[16];
                ledPayload[0] = 0x04;
                ledPayload[2] = brightness;
                ledPayload[4] = 0x01;
                ledPayload[5] = 0xFF; // Full Red channel
                ledPayload[8] = 0x03;
                ledPayload[9] = (byte)(brightness == 0 ? 0 : 1);
                if (SendLedCommand(ledPayload)) anySuccess = true;
            }
            catch { }

            // 2. Named pipe dispatch to hardware services
            string[] pipeNames = { "systemmonitoring_hardware_service_", "nitrosense_hardware_service_", "predatorsense_hardware_service_" };
            byte[] packet = new byte[15];
            BitConverter.GetBytes((ushort)0x1F).CopyTo(packet, 0);
            packet[2] = 1;
            BitConverter.GetBytes((uint)8).CopyTo(packet, 3);
            BitConverter.GetBytes(uiInput).CopyTo(packet, 7);

            foreach (var pipeName in pipeNames)
            {
                try
                {
                    using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
                    pipe.Connect(100);
                    pipe.Write(packet, 0, packet.Length);
                    pipe.Flush();
                    anySuccess = true;
                    break;
                }
                catch { }
            }

            // 3. Direct WMI SetGamingMiscSetting command
            try
            {
                var (ok, _) = SendCommand("SetGamingMiscSetting", uiInput);
                if (ok) anySuccess = true;
            }
            catch { }

            return anySuccess;
        }

        public void SetPowerMode(byte mode)
        {
            // 1. Dual-dispatch to Acer OEM Agent Service (TCP socket & named pipe)
            // This actively communicates with AcerAgentService / AcerHardwareService
            // and sets PL1/PL2 power ceilings, GPU overclocking, and hardware mode state.
            _ = Task.Run(async () =>
            {
                try
                {
                    await AcerAgentClient.SetOperatingModeAsync(mode);
                }
                catch { }
            });

            // 2. Direct ACPI WMI dispatch on root\WMI\AcerGamingFunction
            // Dual-profile dispatch: raw mode byte, configuration packet, and misc setting register 0x0B
            SendCommand("SetGamingProfile", (ulong)mode);
            SendCommand("SetGamingProfile", 0x01000000UL | (ulong)mode);
            SendCommand("SetGamingMiscSetting", (ulong)0x0B | ((ulong)mode << 8));

            // 3. Windows Power Plan Overlay synchronization
            SyncWindowsPowerMode(mode);
        }

        public void SetFanBehavior(byte mode)
        {
            // 1. Direct ACPI WMI Fan Behavior command
            SendCommand("SetGamingFanBehavior", (ulong)(0x09 | ((ulong)mode << 16) | ((ulong)mode << 22)));

            // 2. Dual-dispatch to Acer OEM Agent Service
            // Mode mapping: 0x02 (Max) -> 1, 0x03 (Custom) -> 2, 0x01 (Auto) -> 0
            _ = Task.Run(async () =>
            {
                try
                {
                    int agentFanMode = mode switch
                    {
                        0x02 => 1, // Max
                        0x03 => 2, // Custom
                        _ => 0     // Auto
                    };
                    await AcerAgentClient.SetFanModeAsync(agentFanMode);
                }
                catch { }
            });

            if (mode == 0x03)
                SetFanSpeed(_customCpuFanSpeed, _customGpuFanSpeed);
        }

        public bool SetFanSpeed(byte cpuSpeed, byte gpuSpeed)
        {
            _customCpuFanSpeed = cpuSpeed;
            _customGpuFanSpeed = gpuSpeed;

            _ = Task.Run(async () =>
            {
                try
                {
                    await AcerAgentClient.SetCustomFanSpeedAsync(cpuSpeed, gpuSpeed);
                }
                catch { }
            });

            var (cpuOk, _) = SendCommand("SetGamingFanSpeed", 0x01UL | ((ulong)cpuSpeed << 8));
            var (gpuOk, _) = SendCommand("SetGamingFanSpeed", 0x04UL | ((ulong)gpuSpeed << 8));
            return cpuOk && gpuOk;
        }

        public bool SetCpuFanSpeed(byte speed)
        {
            _customCpuFanSpeed = speed;
            _ = Task.Run(async () =>
            {
                try
                {
                    await AcerAgentClient.SetCustomFanSpeedAsync(_customCpuFanSpeed, _customGpuFanSpeed);
                }
                catch { }
            });
            var (ok, _) = SendCommand("SetGamingFanSpeed", 0x01UL | ((ulong)speed << 8));
            return ok;
        }

        public bool SetGpuFanSpeed(byte speed)
        {
            _customGpuFanSpeed = speed;
            _ = Task.Run(async () =>
            {
                try
                {
                    await AcerAgentClient.SetCustomFanSpeedAsync(_customCpuFanSpeed, _customGpuFanSpeed);
                }
                catch { }
            });
            var (ok, _) = SendCommand("SetGamingFanSpeed", 0x04UL | ((ulong)speed << 8));
            return ok;
        }

        #region Non-blocking Asynchronous RGB Lighting

        private void QueueLightingTask(Action action)
        {
            lock (_lightingLock)
            {
                _pendingLightingAction = action;
                if (_isLightingWorkerRunning) return;
                _isLightingWorkerRunning = true;
            }

            Task.Run(() =>
            {
                while (true)
                {
                    Action? next;
                    lock (_lightingLock)
                    {
                        next = _pendingLightingAction;
                        _pendingLightingAction = null;
                        if (next == null)
                        {
                            _isLightingWorkerRunning = false;
                            break;
                        }
                    }

                    try
                    {
                        next();
                    }
                    catch { }

                    // Inter-action pacing to prevent EC firmware buffer saturation (50Hz)
                    Thread.Sleep(20);
                }
            });
        }


        public void SetRgbMode(int mode, byte r, byte g, byte b, byte brightness, byte speed, byte direction)
        {
            _lastR = r; _lastG = g; _lastB = b;
            _brightness = brightness;
            _speed = speed;
            _direction = direction;
            _lastMode = mode;

            for (int i = 0; i < 4; i++)
                _zoneColors[i] = Color.FromArgb(r, g, b);

            QueueLightingTask(() => ApplyLightingModeCore(mode));
        }

        public void SetSingleZoneColor(int zoneIndex, Color color, int mode = 0)
        {
            if (zoneIndex >= 0 && zoneIndex < 4)
            {
                _zoneColors[zoneIndex] = color;
                _lastMode = mode;
                _lastR = _zoneColors[0].R;
                _lastG = _zoneColors[0].G;
                _lastB = _zoneColors[0].B;
                QueueLightingTask(() =>
                {
                    if (mode == 0) // Static: update specific physical zone
                    {
                        ApplyZoneLightingCore(zoneIndex, color);
                        SyncLightingProfileIni();
                    }
                    else // Animated: color applies to entire keyboard
                    {
                        for (int i = 0; i < 4; i++) _zoneColors[i] = color;
                        _lastR = color.R; _lastG = color.G; _lastB = color.B;
                        ApplyLightingModeCore(mode);
                    }
                });
            }
        }

        public void Set3ZoneCustomColor(int zoneIndex, Color color, int mode = 0)
        {
            _lastMode = mode;
            if (zoneIndex == 0)
            {
                _zoneColors[0] = color;
                _lastR = color.R; _lastG = color.G; _lastB = color.B;
                QueueLightingTask(() =>
                {
                    ApplyZoneLightingCore(0, color);
                    ApplyLightingModeCore(mode);
                });
            }
            else if (zoneIndex == 1)
            {
                // Middle zone covers physical zones 2 and 3 simultaneously
                _zoneColors[1] = color;
                _zoneColors[2] = color;
                QueueLightingTask(() =>
                {
                    ApplyZoneLightingCore(1, color);
                    Thread.Sleep(30);
                    ApplyZoneLightingCore(2, color);
                    ApplyLightingModeCore(mode);
                });
            }
            else if (zoneIndex == 2)
            {
                _zoneColors[3] = color;
                QueueLightingTask(() =>
                {
                    ApplyZoneLightingCore(3, color);
                    ApplyLightingModeCore(mode);
                });
            }
        }

        private void ApplyZoneLightingCore(int zoneIndex, Color color)
        {
            // SetGamingRgbKb zone bitmask: 1 << zoneIndex (1, 2, 4, 8 for physical zones 1, 2, 3, 4)
            ulong mask = 1ul << Math.Clamp(zoneIndex, 0, 3);
            ulong zonePayload = mask
                | ((ulong)color.R << 8)
                | ((ulong)color.G << 16)
                | ((ulong)color.B << 24);
            SendCommand("SetGamingRgbKb", zonePayload);
        }

        public void Set4ZoneColors(Color[] zones, int mode = 0, byte? brightness = null, byte? speed = null)
        {
            if (zones != null && zones.Length >= 4)
            {
                for (int i = 0; i < 4; i++) _zoneColors[i] = zones[i];
                _lastR = zones[0].R; _lastG = zones[0].G; _lastB = zones[0].B;
                _lastMode = mode;
                if (brightness.HasValue) _brightness = brightness.Value;
                if (speed.HasValue) _speed = speed.Value;
                QueueLightingTask(() => Apply4ZoneLightingCore(mode));
            }
        }

        public void Set3ZoneColors(Color zone1, Color zone2, Color zone3, int mode = 0)
        {
            var zones4 = new Color[] { zone1, zone2, zone2, zone3 };
            Set4ZoneColors(zones4, mode);
        }

        public void SetBrightness(byte brightness)
        {
            _brightness = brightness;
            if (!Capabilities.SupportsRgbLighting)
            {
                SetMonochromeBacklight(brightness);
                return;
            }

            QueueLightingTask(() =>
            {
                if (_lastMode == 0)
                {
                    Apply4ZoneLightingCore(0);
                }
                else
                {
                    ApplyLightingModeCore(_lastMode);
                }
            });
            Task.Run(() => { try { SetBacklight30s(_backlight30s); } catch { } });
        }

        public void SetSpeed(byte speed)
        {
            _speed = Math.Clamp(speed, (byte)1, (byte)9);
            QueueLightingTask(() =>
            {
                if (_lastMode == 0)
                {
                    Apply4ZoneLightingCore(0);
                }
                else
                {
                    ApplyLightingModeCore(_lastMode);
                }
            });
        }

        public void SetDirection(byte direction)
        {
            _direction = direction;
            QueueLightingTask(() => ApplyLightingModeCore(_lastMode));
        }

        public void SetKeyboardColor(Color c, int mode = 0)
        {
            _lastR = c.R; _lastG = c.G; _lastB = c.B;
            _lastMode = mode;
            for (int i = 0; i < 4; i++) _zoneColors[i] = c;

            QueueLightingTask(() =>
            {
                byte[] payload = new byte[16];
                payload[0] = (byte)(mode == 8 ? 0 : mode);
                payload[1] = _speed;
                payload[2] = (byte)(mode == 8 ? 0 : _brightness);
                payload[3] = 0;
                payload[4] = (byte)(_direction + 1);
                payload[5] = c.R;
                payload[6] = c.G;
                payload[7] = c.B;
                payload[8] = 0x03;
                payload[9] = (byte)(mode == 8 ? 0 : 1);
                SendLedCommand(payload);

                if (mode == 0)
                {
                    Thread.Sleep(30);
                    for (int z = 0; z < 4; z++)
                    {
                        ApplyZoneLightingCore(z, c);
                        Thread.Sleep(30);
                    }
                }

                SyncLightingProfileIni();
            });
        }

        public void SetStaticColor(byte r, byte g, byte b, byte brightness)
        {
            _brightness = brightness;
            SetKeyboardColor(Color.FromArgb(r, g, b), 0);
        }

        public void SetKeyboardOff()
        {
            _brightness = 0;
            _lastMode = 8;
            if (!Capabilities.SupportsRgbLighting)
            {
                SetMonochromeBacklight(0);
            }
            else
            {
                QueueLightingTask(() => ApplyLightingModeCore(8));
            }
        }

        public void ApplyLightingSynchronous(Color[] zones, int mode = 0, byte brightness = 100, byte speed = 5)
        {
            if (zones != null && zones.Length >= 4)
            {
                for (int i = 0; i < 4; i++) _zoneColors[i] = zones[i];
                _lastR = zones[0].R; _lastG = zones[0].G; _lastB = zones[0].B;
                _lastMode = mode;
                _brightness = brightness;
                _speed = Math.Clamp(speed, (byte)1, (byte)9);
                Apply4ZoneLightingCore(mode);
            }
        }

        private void ApplyLightingModeCore(int mode)
        {
            byte[] payload = new byte[16];
            payload[0] = (byte)(mode == 8 ? 0 : mode);
            payload[1] = _speed;
            payload[2] = (byte)(mode == 8 ? 0 : _brightness);
            payload[3] = 0;
            payload[4] = (byte)(_direction + 1);
            payload[5] = _lastR;
            payload[6] = _lastG;
            payload[7] = _lastB;
            payload[8] = 0x03;
            payload[9] = (byte)(mode == 8 ? 0 : 1);
            SendLedCommand(payload);

            SyncLightingProfileIni();

            _ = Task.Run(async () =>
            {
                try
                {
                    if (mode != 0 && mode != 8)
                    {
                        await AcerAgentClient.SetRgbEffectAsync(mode, Color.FromArgb(_lastR, _lastG, _lastB), _brightness, _speed, _direction);
                    }
                }
                catch { }
            });
        }

        private void Apply4ZoneLightingCore(int mode)
        {
            // 1. Send backlight mode & brightness configuration to controller first
            byte[] payload = new byte[16];
            payload[0] = (byte)(mode == 8 ? 0 : mode);
            payload[1] = _speed;
            payload[2] = (byte)(mode == 8 ? 0 : _brightness);
            payload[3] = 0;
            payload[4] = (byte)(_direction + 1);
            payload[5] = _zoneColors[0].R;
            payload[6] = _zoneColors[0].G;
            payload[7] = _zoneColors[0].B;
            payload[8] = 0x03;
            payload[9] = (byte)(mode == 8 ? 0 : 1);
            SendLedCommand(payload);

            if (mode == 0)
            {
                // 2. In static 4-zone mode, apply individual physical zone colors AFTER SendLedCommand
                // with OEM driver 30ms inter-zone sleep so the EC controller does NOT overwrite per-zone colors
                Thread.Sleep(30);
                for (int z = 0; z < 4; z++)
                {
                    ApplyZoneLightingCore(z, _zoneColors[z]);
                    Thread.Sleep(30);
                }
            }

            SyncLightingProfileIni();
        }

        public void SyncLightingProfileIni()
        {
            try
            {
                string iniPath = @"C:\ProgramData\OEM\AcerLightingService\LightingProfile\LightingProfile.ini";
                if (!File.Exists(iniPath)) return;

                string modeSection = _lastMode switch
                {
                    0 => "AcerECKeyboard Device_STATIC",
                    1 => "AcerECKeyboard Device_BREATHING",
                    2 => "AcerECKeyboard Device_NEON",
                    3 => "AcerECKeyboard Device_WAVE",
                    4 => "AcerECKeyboard Device_SHIFTING",
                    5 => "AcerECKeyboard Device_ZOOM",
                    6 => "AcerECKeyboard Device_METEOR",
                    7 => "AcerECKeyboard Device_TWINKLING",
                    _ => "AcerECKeyboard Device_STATIC"
                };

                // AcerLightingService 1-based indexing: Mode1 is Direct, Mode2 is STATIC, Mode3 is BREATHING, etc.
                // Mode 8 is Off -> ActiveMode 0.
                // Mode 0 (Static) -> ActiveMode 2 (Mode2=STATIC).
                // Mode 1..7 (Animated) -> ActiveMode _lastMode + 2 (3 to 9).
                int activeModeVal = _lastMode switch
                {
                    8 => 0,
                    _ => _lastMode + 2
                };
                WritePrivateProfileString("AcerECKeyboard Device", "ActiveMode", activeModeVal.ToString(), iniPath);

                string colorHex = $"0X{_zoneColors[0].R:x2}{_zoneColors[0].G:x2}{_zoneColors[0].B:x2}";

                // Always ensure STATIC section has valid user color and brightness (never 000000 or uninitialized)
                WritePrivateProfileString("AcerECKeyboard Device_STATIC", "color", colorHex, iniPath);
                WritePrivateProfileString("AcerECKeyboard Device_STATIC", "brightness", _brightness.ToString(), iniPath);
                WritePrivateProfileString("AcerECKeyboard Device_STATIC", "speed", _speed.ToString(), iniPath);

                if (_lastMode != 0 && _lastMode != 8)
                {
                    string animHex = $"0X{_lastR:x2}{_lastG:x2}{_lastB:x2}";
                    WritePrivateProfileString(modeSection, "brightness", _brightness.ToString(), iniPath);
                    WritePrivateProfileString(modeSection, "speed", _speed.ToString(), iniPath);
                    WritePrivateProfileString(modeSection, "color", animHex, iniPath);
                }

                // Neutralize legacy Acer Amber presets in factory default sections so fallback never turns amber
                WritePrivateProfileString("AcerECKeyboard Device_WAVE", "color", "0X00dcff", iniPath);
                WritePrivateProfileString("AcerECKeyboard Device_BREATHING", "color", "0X00dcff", iniPath);
                WritePrivateProfileString("AcerECKeyboard Device_SHIFTING", "color", "0X00dcff", iniPath);

                for (int i = 0; i < 4; i++)
                {
                    Color c = _zoneColors[i];
                    WritePrivateProfileString("AcerECKeyboard Device_PerKeyColor", $"Key{i + 1}", $"0X{c.R:x2}{c.G:x2}{c.B:x2}", iniPath);
                }
            }
            catch { }
        }

        #endregion

        private void SyncWindowsPowerMode(byte acerMode)
        {
            try
            {
                Guid overlay = acerMode switch
                {
                    0x00 or 0x06 => OVERLAY_EFFICIENCY,
                    0x04 or 0x05 => OVERLAY_PERFORMANCE,
                    _ => OVERLAY_BALANCED
                };
                PowerSetActiveOverlayScheme(overlay);
            }
            catch { }
        }

        public bool SetBatteryChargeLimit(bool enable)
        {
            // 1. Primary: BIOS native ACPI WMI BatteryControl class (Predator & modern Nitro)
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM BatteryControl");
                using var results = searcher.Get();
                using var obj = results.Cast<ManagementObject>().FirstOrDefault();
                if (obj != null)
                {
                    using var inParams = obj.GetMethodParameters("SetBatteryHealthControl");
                    inParams["uBatteryNo"] = (byte)1;
                    inParams["uFunctionMask"] = (byte)1;
                    inParams["uFunctionStatus"] = (byte)(enable ? 1 : 0);
                    inParams["uReservedIn"] = new byte[] { 0, 0, 0, 0, 0 };

                    using var outParams = obj.InvokeMethod("SetBatteryHealthControl", inParams, null);
                    if (outParams?["uReturn"] != null)
                    {
                        ushort result = Convert.ToUInt16(outParams["uReturn"]);
                        if (result == 0) return true;
                    }
                }
            }
            catch { }

            // 2. Fallback for Nitro and Acer systems using ASMSvc / AcerCareCenter registry keys
            try
            {
                int stopCharging = enable ? 80 : 100;
                int healthControl = enable ? 1 : 0;
                bool regOk = false;

                string[] paths = {
                    @"SOFTWARE\OEM\AcerCareCenter\Battery",
                    @"SOFTWARE\WOW6432Node\OEM\AcerCareCenter\Battery",
                    @"SOFTWARE\OEM\AcerCareCenter",
                    @"SOFTWARE\WOW6432Node\OEM\AcerCareCenter"
                };

                foreach (var path in paths)
                {
                    try
                    {
                        using var key = Registry.LocalMachine.CreateSubKey(path, true);
                        if (key != null)
                        {
                            key.SetValue("StopCharging", stopCharging, RegistryValueKind.DWord);
                            key.SetValue("HealthControl", healthControl, RegistryValueKind.DWord);
                            key.SetValue("BatteryLimit", healthControl, RegistryValueKind.DWord);
                            key.SetValue("LimitPercent", 80, RegistryValueKind.DWord);
                            regOk = true;
                        }
                    }
                    catch { }
                }

                // If ASMSvc, AcerServiceSvc, or AcerDeviceEnablingService exists, ensure started
                string[] svcs = { "ASMSvc", "AcerServiceSvc", "AcerDeviceEnablingServiceV2", "AcerDeviceEnablingService" };
                foreach (var svc in svcs)
                {
                    try
                    {
                        using var sc = new ServiceController(svc);
                        if (sc.Status == ServiceControllerStatus.Stopped || sc.Status == ServiceControllerStatus.Paused)
                        {
                            sc.Start();
                        }
                    }
                    catch { }
                }

                if (regOk) return true;
            }
            catch { }

            return false;
        }

        public bool IsBatteryControlSupported()
        {
            try
            {
                // Check 1: BIOS native ACPI WMI BatteryControl class (Predator & modern Nitro)
                using (var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM BatteryControl"))
                using (var results = searcher.Get())
                {
                    if (results.Count > 0) return true;
                }

                // Check 2: Acer OEM battery service or registry exists (Nitro / Acer Care Center)
                string[] svcs = { "ASMSvc", "AcerServiceSvc", "AcerDeviceEnablingServiceV2", "AcerDeviceEnablingService" };
                foreach (var svc in svcs)
                {
                    try
                    {
                        using var sc = new ServiceController(svc);
                        _ = sc.Status;
                        return true;
                    }
                    catch { }
                }

                using (var reg = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\OEM\AcerCareCenter"))
                {
                    if (reg != null) return true;
                }

                // Check 3: Running on genuine Acer gaming chassis (Nitro / Predator)
                if (ChassisFamily is AcerChassisFamily.Nitro or AcerChassisFamily.Predator)
                    return true;
            }
            catch { }

            return false;
        }

        #region Hardware Features (GPU MUX, LCD Overdrive, Backlight Sleep, Windows Key Lock)

        /// <summary>
        /// Queries the current GPU MUX Working Mode via Acer OEM Agent Service (Port 46933).
        /// Returns 0 = Optimus (Hybrid), 1 = Discrete GPU, 2 = Auto / Advanced Optimus.
        /// </summary>
        public async Task<int?> GetGpuModeAsync()
        {
            return await AcerAgentClient.GetGpuModeAsync();
        }

        /// <summary>
        /// Queries supported GPU MUX capabilities bitmask:
        /// Bit 0 (1): Optimus
        /// Bit 1 (2): Discrete GPU
        /// Bit 2 (4): Auto / Advanced Optimus
        /// </summary>
        public async Task<int> GetGpuModeCapabilityAsync()
        {
            return await AcerAgentClient.GetGpuModeCapabilityAsync();
        }

        /// <summary>
        /// Sets the GPU MUX Working Mode:
        /// 0 = Optimus (Dynamic switching / Hybrid)
        /// 1 = Discrete (NVIDIA GPU Only / Direct display connection)
        /// 2 = Auto (Advanced Optimus)
        /// Dual-dispatches to Acer OEM Agent Service (runs as LocalSystem) and direct ACPI WMI fallback.
        /// Note: Hardware MUX switch requires a system reboot to take effect in firmware.
        /// </summary>
        public async Task<bool> SetGpuModeAsync(int mode)
        {
            // 1. Primary: OEM Agent Service (syncs registry, state, and handles permissions)
            bool agentOk = await AcerAgentClient.SetGpuModeAsync(mode);

            // 2. Direct ACPI WMI fallback (Feature ID 0x02, value = mode + 1)
            // Reverse-engineered from AcerAgentService RVA 0x3DD90:
            // Optimus (mode 0) -> 0x0102
            // Discrete (mode 1) -> 0x0202
            // Auto (mode 2)     -> 0x0302
            ulong wmiPayload = (ulong)0x02 | (((ulong)mode + 1) << 8);
            var (wmiOk, _) = SendCommand("SetGamingMiscSetting", wmiPayload);

            return agentOk || wmiOk;
        }

        public bool SetLcdOverdrive(bool enable)
        {
            // Reverse-engineered from AcerAgentService.exe / AcerHardwareService.exe:
            // SetGamingMiscSetting takes 64-bit value where bits [31:0] = 0x10 (Feature ID for LCD Overdrive)
            // and bits [63:32] = 1 (enable) or 0 (disable).
            ulong payload = ((enable ? 1UL : 0UL) << 32) | 0x10UL;
            var (ok, _) = SendCommand("SetGamingMiscSetting", payload);
            return ok;
        }

        private static uint GetBkHotkeyNumber()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\OEM\AcerAgentService");
                if (key?.GetValue("BK_Hotkey_Number") is int val) return (uint)val;
                if (key?.GetValue("BK_Hotkey_Number") is long val64) return (uint)val64;
            }
            catch { }
            return 132; // Default for Predator Neo/Helios (0x84)
        }

        public bool GetBacklight30s()
        {
            uint hotkeyNum = GetBkHotkeyNumber();
            uint low32_get = 0x80001 | (hotkeyNum << 8);

            // 1. Hardware Service named pipes (kSvcCmdWMIGetFunction = 0x1E)
            string[] pipeNames = { "systemmonitoring_hardware_service_", "predatorsense_hardware_service_" };
            byte[] packet = new byte[11];
            BitConverter.GetBytes((ushort)0x1E).CopyTo(packet, 0);
            packet[2] = 1;
            BitConverter.GetBytes((uint)4).CopyTo(packet, 3);
            BitConverter.GetBytes(low32_get).CopyTo(packet, 7);

            foreach (var pipeName in pipeNames)
            {
                try
                {
                    using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
                    pipe.Connect(150);
                    pipe.Write(packet, 0, packet.Length);
                    pipe.Flush();
                    byte[] resp = new byte[32];
                    int read = pipe.Read(resp, 0, resp.Length);
                    if (read >= 13)
                    {
                        ulong val = BitConverter.ToUInt64(resp, 5);
                        uint high32 = (uint)(val >> 32);
                        byte timeoutSec = (byte)((high32 >> 8) & 0xFF);
                        _backlight30s = timeoutSec > 0;
                        return _backlight30s;
                    }
                }
                catch { }
            }

            // 2. Direct WMI APGeAction.GetFunction
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM APGeAction");
                using var results = searcher.Get();
                using var enumerator = results.GetEnumerator();
                if (enumerator.MoveNext() && enumerator.Current is ManagementObject obj)
                {
                    using var inParams = obj.GetMethodParameters("GetFunction");
                    inParams["uiInput"] = low32_get;
                    using var outParams = obj.InvokeMethod("GetFunction", inParams, null);
                    if (outParams?["uiOutput"] != null)
                    {
                        ulong val = Convert.ToUInt64(outParams["uiOutput"]);
                        uint high32 = (uint)(val >> 32);
                        byte timeoutSec = (byte)((high32 >> 8) & 0xFF);
                        _backlight30s = timeoutSec > 0;
                        return _backlight30s;
                    }
                }
            }
            catch { }

            // 3. Direct WMI AcerGenericMethod.GetFunction
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM AcerGenericMethod");
                using var results = searcher.Get();
                using var enumerator = results.GetEnumerator();
                if (enumerator.MoveNext() && enumerator.Current is ManagementObject obj)
                {
                    using var inParams = obj.GetMethodParameters("GetFunction");
                    inParams["uiInput"] = low32_get;
                    using var outParams = obj.InvokeMethod("GetFunction", inParams, null);
                    if (outParams?["uiOutput"] != null)
                    {
                        ulong val = Convert.ToUInt64(outParams["uiOutput"]);
                        uint high32 = (uint)(val >> 32);
                        byte timeoutSec = (byte)((high32 >> 8) & 0xFF);
                        _backlight30s = timeoutSec > 0;
                        return _backlight30s;
                    }
                }
            }
            catch { }

            return _backlight30s;
        }

        public bool SetBacklight30s(bool enable)
        {
            // Reverse-Engineered from AcerAgentService.exe (0x14003DFE0 - 0x14003E0D1) & AcerHardwareService.exe:
            // low32  = 0x80002 | (BK_Hotkey_Number << 8)  => 0x88402 (on BK_Hotkey_Number = 132 / 0x84)
            // high32 = (enable ? 0x1E00 : 0x0000) | brightness (0x1E = 30 seconds idle timeout!)
            // uiInput = ((ulong)high32 << 32) | (ulong)low32
            //
            // We dispatch across all supported communication paths:
            // 1. Hardware Service Named Pipe (kSvcCmdWMISetFunction = 0x1F) - exact method used by official PredatorSense
            // 2. Direct WMI APGeAction.SetFunction
            // 3. Direct WMI AcerGenericMethod.SetFunction
            // 4. Direct WMI AcerGamingFunction.SetGamingMiscSetting
            _backlight30s = enable;
            uint hotkeyNum = GetBkHotkeyNumber();

            uint low32 = 0x80002 | (hotkeyNum << 8);
            uint high32 = (enable ? 0x1E00U : 0x0000U) | (_brightness > 0 ? _brightness : (byte)0x64);
            ulong uiInput = ((ulong)high32 << 32) | (ulong)low32;

            bool anySuccess = false;

            // Path 1: Hardware Service Named Pipe
            string[] pipeNames = { "systemmonitoring_hardware_service_", "predatorsense_hardware_service_" };
            byte[] packet = new byte[15];
            BitConverter.GetBytes((ushort)0x1F).CopyTo(packet, 0); // kSvcCmdWMISetFunction
            packet[2] = 1;                                         // 1 argument
            BitConverter.GetBytes((uint)8).CopyTo(packet, 3);      // argument size (8 bytes)
            BitConverter.GetBytes(uiInput).CopyTo(packet, 7);      // payload uint64

            foreach (var pipeName in pipeNames)
            {
                try
                {
                    using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
                    pipe.Connect(150);
                    pipe.Write(packet, 0, packet.Length);
                    pipe.Flush();
                    byte[] resp = new byte[16];
                    int read = pipe.Read(resp, 0, resp.Length);
                    if (read >= 7 && resp[read - 1] == 0)
                    {
                        anySuccess = true;
                        break;
                    }
                }
                catch { }
            }

            // Path 2: Direct WMI APGeAction.SetFunction
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM APGeAction");
                using var results = searcher.Get();
                using var enumerator = results.GetEnumerator();
                if (enumerator.MoveNext() && enumerator.Current is ManagementObject obj)
                {
                    using var inParams = obj.GetMethodParameters("SetFunction");
                    inParams["uiInput"] = uiInput;
                    using var outParams = obj.InvokeMethod("SetFunction", inParams, null);
                    if (outParams?["uiOutput"] != null) anySuccess = true;
                }
            }
            catch { }

            // Path 3: Direct WMI AcerGenericMethod.SetFunction
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM AcerGenericMethod");
                using var results = searcher.Get();
                using var enumerator = results.GetEnumerator();
                if (enumerator.MoveNext() && enumerator.Current is ManagementObject obj)
                {
                    using var inParams = obj.GetMethodParameters("SetFunction");
                    inParams["uiInput"] = uiInput;
                    using var outParams = obj.InvokeMethod("SetFunction", inParams, null);
                    if (outParams?["uiOutput"] != null) anySuccess = true;
                }
            }
            catch { }

            // Path 4: Direct WMI AcerGamingFunction.SetGamingMiscSetting
            try
            {
                var (ok, _) = SendCommand("SetGamingMiscSetting", uiInput);
                if (ok) anySuccess = true;
            }
            catch { }

            return anySuccess;
        }

        public bool SetWinKeyLock(bool lockKeys)
        {
            byte val = (byte)(lockKeys ? 0 : 3);
            byte[] kbPayload = new byte[8] { 0x03, val, 0x00, 0x00, 0x00, 0x00, 0x00, (byte)(0xFC - val) };
            return SendGamingRgbKbCommand(kbPayload);
        }

        #endregion

        #region Hardware Capability & Diagnostic Probe

        private static (string Manufacturer, string Model, string BiosVersion, bool IsAcerGaming)? _cachedSystemIdentity;
        private static readonly object _identityLock = new();

        public static (string Manufacturer, string Model, string BiosVersion, bool IsAcerGaming) GetSystemIdentity()
        {
            if (_cachedSystemIdentity.HasValue) return _cachedSystemIdentity.Value;

            lock (_identityLock)
            {
                if (_cachedSystemIdentity.HasValue) return _cachedSystemIdentity.Value;

                string manufacturer = "Acer", model = "Unknown", bios = "Unknown";
                bool isGaming = false;
                try
                {
                    using var csSearcher = new ManagementObjectSearcher(@"root\CIMV2", "SELECT Manufacturer, Model FROM Win32_ComputerSystem");
                    using var csResults = csSearcher.Get();
                    foreach (ManagementObject mo in csResults)
                    {
                        using (mo)
                        {
                            manufacturer = mo["Manufacturer"]?.ToString()?.Trim() ?? manufacturer;
                            model = mo["Model"]?.ToString()?.Trim() ?? model;
                            break;
                        }
                    }

                    using var biosSearcher = new ManagementObjectSearcher(@"root\CIMV2", "SELECT SMBIOSBIOSVersion FROM Win32_BIOS");
                    using var biosResults = biosSearcher.Get();
                    foreach (ManagementObject mo in biosResults)
                    {
                        using (mo)
                        {
                            bios = mo["SMBIOSBIOSVersion"]?.ToString()?.Trim() ?? bios;
                            break;
                        }
                    }

                    isGaming = manufacturer.Contains("Acer", StringComparison.OrdinalIgnoreCase) &&
                        (model.Contains("Predator", StringComparison.OrdinalIgnoreCase) ||
                         model.Contains("Helios", StringComparison.OrdinalIgnoreCase) ||
                         model.Contains("Triton", StringComparison.OrdinalIgnoreCase) ||
                         model.Contains("Nitro", StringComparison.OrdinalIgnoreCase) ||
                         model.Contains("PH", StringComparison.OrdinalIgnoreCase) ||
                         model.Contains("PT", StringComparison.OrdinalIgnoreCase) ||
                         model.Contains("AN", StringComparison.OrdinalIgnoreCase));
                }
                catch { }

                _cachedSystemIdentity = (manufacturer, model, bios, isGaming);
                return _cachedSystemIdentity.Value;
            }
        }

        public string ExportDiagnosticReport()
        {
            var (mfg, model, bios, isGaming) = GetSystemIdentity();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"TimestampUtc\": \"{DateTime.UtcNow:O}\",");
            sb.AppendLine($"  \"Manufacturer\": \"{mfg}\",");
            sb.AppendLine($"  \"Model\": \"{model}\",");
            sb.AppendLine($"  \"BiosVersion\": \"{bios}\",");
            sb.AppendLine($"  \"IsAcerGamingChassis\": {isGaming.ToString().ToLowerInvariant()},");
            sb.AppendLine($"  \"DiscoveredWmiClass\": \"{DiscoveredWmiClass}\",");

            // Probe available Acer WMI classes
            var availableClasses = new List<string>();
            foreach (var cls in KnownWmiClasses)
            {
                try
                {
                    using var s = new ManagementObjectSearcher(@"root\WMI", $"SELECT * FROM {cls}");
                    using var res = s.Get();
                    using var en = res.GetEnumerator();
                    if (en.MoveNext()) availableClasses.Add(cls);
                }
                catch { }
            }
            sb.AppendLine($"  \"AvailableGamingWmiClasses\": [ {string.Join(", ", availableClasses.Select(c => $"\"{c}\""))} ],");

            // Check AASSvc Named Pipe
            bool pipeAvailable = false;
            try
            {
                using var pipe = new NamedPipeClientStream(".", "AcerAgentPipe", PipeDirection.InOut);
                pipe.Connect(100);
                pipeAvailable = pipe.IsConnected;
            }
            catch { }
            sb.AppendLine($"  \"AcerAgentPipeAvailable\": {pipeAvailable.ToString().ToLowerInvariant()},");

            // Fan readings check
            int? cpuRpm = CpuFanRpm;
            int? gpuRpm = GpuFanRpm;
            int? auxRpm = AuxFanRpm;
            sb.AppendLine($"  \"ChassisFamily\": \"{ChassisFamily}\",");
            sb.AppendLine($"  \"SupportsRgbLighting\": {Capabilities.SupportsRgbLighting.ToString().ToLowerInvariant()},");
            sb.AppendLine($"  \"SupportsCoolBoost\": {Capabilities.SupportsCoolBoost.ToString().ToLowerInvariant()},");
            sb.AppendLine($"  \"SupportsBatteryControl\": {Capabilities.SupportsBatteryControl.ToString().ToLowerInvariant()},");
            sb.AppendLine($"  \"CpuFanRpm\": {(cpuRpm.HasValue ? cpuRpm.Value.ToString() : "null")},");
            sb.AppendLine($"  \"GpuFanRpm\": {(gpuRpm.HasValue ? gpuRpm.Value.ToString() : "null")},");
            sb.AppendLine($"  \"HasAuxFan\": {HasAuxFan.ToString().ToLowerInvariant()},");
            sb.AppendLine($"  \"AuxFanRpm\": {(auxRpm.HasValue ? auxRpm.Value.ToString() : "null")}");
            sb.AppendLine("}");

            return sb.ToString();
        }

        #endregion

        public void Dispose()
        {
            lock (_lock)
            {
                if (!_disposed)
                {
                    _disposed = true;
                    lock (_lightingLock)
                    {
                        _pendingLightingAction = null;
                    }
                    _cachedObj?.Dispose();
                    _cachedObj = null;
                }
            }
        }
    }
}
