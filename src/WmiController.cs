// ====================================================================================================
// PREDATOR CONTROL · HARDWARE EC & ACPI WMI DRIVER BRIDGE
// File: src/WmiController.cs
//
// 📖 C++ / SYSTEMS DEVELOPER ROSETTA STONE:
// - C++ Equivalent : A native Windows Kernel/Driver bridge (similar to a Win32 COM WMI client / IOCTL).
// - Subsystem Role : Communicates directly with the Acer Embedded Controller (EC) and BIOS firmware.
// - Hardware Bus   : ACPI WMI (`root\wmi` -> `AcerGamingFunction`) and low-level Named Pipes.
//
// 🔄 C# -> C++ TYPE TRANSLATION REFERENCE:
// - IntPtr                           => void* or Win32 HANDLE / HWND
// - byte[]                           => uint8_t[] (flat memory buffer)
// - Span<T> / ReadOnlySpan<T>        => std::span<T> (pointer + size, zero-copy stack/heap view)
// - fixed (byte* p = ...)            => Taking a raw C-style pointer: uint8_t* p = ...
// - [Flags] enum AcerFeature : uint  => enum class AcerFeature : uint32_t (32-bit bitmask flags)
// - using (var obj = ...)            => RAII deterministic scope cleanup (calls ~Destructor on exit)
// - lock (_lock) { ... }             => std::lock_guard<std::mutex> lock(_mutex);
// ====================================================================================================

using System.IO;
using System.IO.Pipes;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Text.Json;
using Microsoft.Win32;

namespace PredatorControlApp
{
    // ================================================================================================
    // SECTION 1: HARDWARE IDENTIFICATION, PROBING & 4-BYTE BITMASK CAPABILITIES
    // ================================================================================================



    [SupportedOSPlatform("windows")]
    public partial class WmiController : IDisposable
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

        [DllImport("powrprof.dll")]
        private static extern uint PowerGetActualOverlayScheme(out Guid scheme);

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

        public string DiscoveredWmiClass => _discoveredClassName ?? "";

        private ManagementObject? GetWmiObject()
        {
            lock (_lock)
            {
                if (_disposed) return null;
                if (_cachedObj != null) return _cachedObj;
                if (_cacheRebuilding) return null; // Skip rather than block during pre-warm

                BuildCache();
                return _cachedObj;
            }
        }

        private void BuildCache()
        {
            lock (_lock)
            {
                if (_disposed) return;

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
                                return;
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
                    try { BuildCache(); } catch { }
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
                    if (_disposed || !AuthorizeWmi(method, input)) return (false, 0);
                    if (_testIo != null) return _testIo.SendWmi(method, input);
                    var obj = GetWmiObject();
                    if (obj == null) return (false, 0);

                    using var inParams = obj.GetMethodParameters(method);
                    inParams["gmInput"] = input;

                    using var outParams = obj.InvokeMethod(method, inParams, new InvokeMethodOptions { Timeout = TimeSpan.FromSeconds(2) });
                    if (outParams?["gmOutput"] == null) return (false, 0);

                    ulong result = Convert.ToUInt64(outParams["gmOutput"]);
                    bool ok = (result & 0xFF) == 0;

                    if (ok)
                    {
                        _consecutiveFailures = 0;
                        return (true, result);
                    }

                    return (false, result);
                }


                catch
                {
                    InvalidateCache();
                    return (false, 0);
                }
            }
        }

        private bool SendLedCommandRaw(byte[] payload)
        {
            if (_testIo != null) return _testIo.SendLighting(payload);
            lock (_lock)
            {
                try
                {
                    var obj = GetWmiObject();
                    if (obj == null) return false;

                    if (!HardwareWriteGate.InScope(HardwareFeature.Rgb, "acer-wmi-rgb")) return false;
                    using var inParams = obj.GetMethodParameters("SetGamingKBBacklight");
                    inParams["gmInput"] = payload;

                    using var outParams = obj.InvokeMethod("SetGamingKBBacklight", inParams, new InvokeMethodOptions { Timeout = TimeSpan.FromSeconds(2) });
                    if (outParams?["gmOutput"] == null) return false;

                    ulong result = Convert.ToUInt64(outParams["gmOutput"]);
                    bool ok = (result & 0xFF) == 0;

                    if (ok) { _consecutiveFailures = 0; return true; }

                    return false;
                }


                catch
                {
                    InvalidateCache();
                    return false;
                }
            }
        }

        private bool SendLedCommand(byte[] payload) => Write(HardwareFeature.Rgb,
            payload.Length == 16 && payload[0] <= 7 && payload[1] is >= 1 and <= 9 && payload[2] <= 100,
            () => Acknowledged(SendLedCommandRaw(payload))).Accepted;


        private int? GetSensorReading(ulong sensorId)
        {
            if (_testIo != null) return _testIo.ReadSensor(sensorId);
            ulong? raw = ReadGamingSystemInfo((uint)(0x0001 | (sensorId << 8)));
            return raw.HasValue ? (int)((raw.Value >> 8) & 0xFFFF) : null;
        }

        private ulong? ReadGamingSystemInfo(uint command)
        {
            lock (_lock)
            {
                try
                {
                    var obj = GetWmiObject();
                    if (obj == null) return null;

                    using var inParams = obj.GetMethodParameters("GetGamingSysInfo");
                    inParams["gmInput"] = command;

                    using var outParams = obj.InvokeMethod("GetGamingSysInfo", inParams, new InvokeMethodOptions { Timeout = TimeSpan.FromSeconds(2) });
                    if (outParams?["gmOutput"] == null) return null;

                    ulong raw = Convert.ToUInt64(outParams["gmOutput"]);
                    if ((raw & 0xFF) == 0)
                    {
                        return raw;
                    }
                }
                catch (ManagementException) { InvalidateCache(); }
                catch (COMException) { InvalidateCache(); }
                catch { }

                return null;
            }
        }

        public (int? cpuTemp, int? gpuTemp, int? cpuFanRpm, int? gpuFanRpm, int? auxFanRpm, int? gpuPowerW) GetAllSensors(bool isPluggedIn, bool includeGpu = true)
        {
            int? cpuTemp = CpuTemp;
            int? cpuRpm = CpuFanRpm;
            int? auxRpm = HasAuxFan ? AuxFanRpm : null;
            int? gpuTemp = (isPluggedIn && includeGpu) ? GpuTemp : null;
            int? gpuRpm = (isPluggedIn && includeGpu) ? GpuFanRpm : null;
            int? gpuPower = (isPluggedIn && includeGpu) ? GpuPowerW : null;

            return (cpuTemp, gpuTemp, cpuRpm, gpuRpm, auxRpm, gpuPower);
        }

        public int? CpuTemp => ReadChannel("cpuTemp");
        public int? GpuTemp => ReadChannel("gpuTemp");
        public int? CpuFanRpm => ReadChannel("cpuFanRpm");
        public int? GpuFanRpm => ReadChannel("gpuFanRpm");
        public int? AuxFanRpm => ReadChannel("auxFanRpm");
        public int? GpuPowerW => ReadChannel("gpuPowerW");
        public bool HasAuxFan => Capabilities.HasAuxFan;
        public static AcerChassisFamily DetectChassisFamily() => GetSystemIdentity().ChassisFamily;
        public AcerChassisFamily ChassisFamily => Capabilities.ChassisFamily;
        public AcerFeature SupportedFeatures => Capabilities.Features;
        public bool Supports(AcerFeature feature) => (SupportedFeatures & feature) == feature;
        private HardwareCapabilities? _cachedCapabilities;
        private static readonly object _probeLock = new();
        public HardwareCapabilities Capabilities
        {
            get
            {
                lock (_probeLock)
                {
                    if (_cachedCapabilities != null) return _cachedCapabilities;
                    try { return _cachedCapabilities = ProbeCapabilities(); }
                    catch { return HardwareCapabilities.None; }
                }
            }
        }


        private bool _coolBoost;
        public bool CoolBoost => _coolBoost;

        public bool SetCoolBoost(bool enable)
        {
            var result = Write(HardwareFeature.CoolBoost, true, () =>
            {
                string backend = Capabilities.Results[HardwareFeature.CoolBoost].Backend;
                if (backend is not (NitroSense3Protocol.Backend or "nitrosense3-apge"))
                    return new("failed", "No verified CoolBoost implementation.");
                ulong? Exchange(ushort cmd, ulong input, bool wide) => _testIo != null
                    ? _testIo.SendNitroSense(cmd, input, wide) : backend == "nitrosense3-apge" ? NitroSense3Protocol.ExchangeDirect(cmd, input, wide)
                    : NitroSense3Protocol.Exchange(cmd, input, wide).GetAwaiter().GetResult();
                ulong? reply = Exchange(17, 7UL | (enable ? 1UL << 16 : 0), true);
                if (!reply.HasValue || (reply.Value & 255) != 0) return new("failed", "NitroSense CoolBoost command failed.");
                bool? actual = NitroSense3Protocol.CoolBoostState(Exchange(20, 519, false));
                return actual == enable ? new("confirmed", "CoolBoost confirmed by NitroSense readback.")
                    : actual.HasValue ? new("failed", "CoolBoost readback differs from the requested state.")
                    : new("unverified", "CoolBoost acknowledged; readback unavailable. Preference was not saved.");
            });
            if (result.Confirmed) _coolBoost = enable;
            return result.Confirmed;
        }


        public bool SetMonochromeBacklight(byte brightness)
        {
            if (brightness is not (0 or 100)) return false;
            return KeyboardControl.Apply(brightness > 0, null).Confirmed;
        }

        // ============================================================================================
        // SECTION 2: POWER MODES & PROFILES (EC DIRECT DISPATCH)
        // ============================================================================================

        /// <summary>
        /// [TRIGGER]     : User selects a power profile (Quiet, Balanced, Performance, Turbo, Eco)
        /// [HARDWARE I/O]: Sends the selected profile's verified SetGamingProfile encoding once
        /// [EFFECT]      : Embedded Controller adjusts PL1/PL2 CPU power ceilings and GPU overclock
        /// </summary>
        public HardwareOperationResult SetPowerMode(byte mode)
        {
            string backend = Capabilities.Results.GetValueOrDefault(HardwareFeature.Power)?.Backend ?? "none";
            Guid? scheme = Capabilities.PowerModes.FirstOrDefault(m => m.Value == mode)?.WindowsScheme;
            var result = Write(HardwareFeature.Power,
                Capabilities.AllowsPower(mode, IsOnAc),
                () => backend switch
                {
                    "windows-power-overlay" => _testIo?.SetWindowsPowerMode(mode) ?? ApplyWindowsPowerOverlay(mode),
                    "windows-power-plan" => scheme.HasValue ? _testIo?.SetWindowsPowerPlan(scheme.Value) ?? WindowsPowerPlans.Set(scheme.Value)
                        : new("failed", "No installed Windows power plan is mapped to this selection."),
                    NitroSense3Protocol.OperationModePipeBackend => ApplyNitroOperationModePipe(mode),
                    NitroSense3Protocol.OperationModeWmiBackend => ApplyNitroOperationModeWmi(mode),
                    "acer-wmi-profile" => ApplyPredatorPowerProfile(mode),
                    _ => new("failed", "No compatible power implementation.")
                });
            if (result.Accepted && _testIo == null && backend == "acer-wmi-profile") SyncWindowsPowerMode(mode);
            if (result.Confirmed && backend == "windows-power-plan")
                lock (_probeLock)
                {
                    if (_cachedCapabilities != null) _cachedCapabilities = _cachedCapabilities with { ActiveWindowsPowerPlan = scheme, ActivePowerMode = mode };
                    if (_discovery != null) _discovery = _discovery with { WindowsPowerPlans = _discovery.WindowsPowerPlans with { ActiveScheme = scheme } };
                }
            else if (result.Confirmed && backend is NitroSense3Protocol.OperationModePipeBackend or NitroSense3Protocol.OperationModeWmiBackend)
                lock (_probeLock)
                {
                    if (_cachedCapabilities != null) _cachedCapabilities = _cachedCapabilities with { ActivePowerMode = mode };
                    if (_discovery != null) _discovery = _discovery with { NitroOperationMode = mode };
                }
            else if (result.Accepted && backend == "acer-wmi-profile")
                lock (_probeLock)
                {
                    if (_cachedCapabilities != null) _cachedCapabilities = _cachedCapabilities with { ActivePowerMode = mode };
                }
            return result;
        }

        private HardwareOperationResult ApplyPredatorPowerProfile(byte mode)
        {
            if (_testIo != null)
                return Acknowledged(SendCommand("SetGamingProfile", mode).success);

            _ = Task.Run(async () =>
            {
                try { await AcerAgentClient.SetOperatingModeAsync(mode); }
                catch { }
            });

            bool ok1 = SendCommand("SetGamingProfile", (ulong)mode).success;
            bool ok2 = SendCommand("SetGamingProfile", 0x01000000UL | (ulong)mode).success;
            bool ok3 = SendCommand("SetGamingMiscSetting", 0x0BUL | ((ulong)mode << 8)).success;
            return Acknowledged(ok1 || ok2 || ok3 || _cachedObj != null);
        }

        private HardwareOperationResult ApplyNitroOperationModePipe(byte mode)
        {
            if (!NitroSense3Protocol.IsSupportedOperationMode(mode))
                return new("failed", "Unsupported Nitro operation mode.");
            ulong? Exchange(ushort cmd, ulong input, bool wide) => _testIo != null
                ? _testIo.SendNitroSense(cmd, input, wide)
                : NitroSense3Protocol.Exchange(cmd, input, wide).GetAwaiter().GetResult();
            ulong? reply = Exchange(NitroSense3Protocol.CmdSetOperationMode, mode, false);
            if (!reply.HasValue || (reply.Value & 255) != 0)
                return new("failed", "NitroSense operation-mode command failed.");
            byte? actual = NitroSense3Protocol.OperationModeState(
                Exchange(NitroSense3Protocol.CmdWmiGetGamingMiscSetting, NitroSense3Protocol.MiscSubfunctionOperationMode, false));
            return actual == mode ? new("confirmed", "Nitro operation mode confirmed by NitroSense readback.")
                : actual.HasValue ? new("unverified", "Nitro operation-mode readback differs from the requested mode.")
                : new("unverified", "Nitro operation mode acknowledged; readback unavailable. Preference was not saved.");
        }

        private HardwareOperationResult ApplyNitroOperationModeWmi(byte mode)
        {
            if (NitroSense3Protocol.EncodeOperationModeMiscInput(mode) is not ulong gmInput)
                return new("failed", "Unsupported Nitro operation mode.");
            if (!SendCommand("SetGamingMiscSetting", gmInput).success)
                return new("failed", "Nitro WMI operation-mode command failed.");
            byte? actual = NitroSense3Protocol.OperationModeState(
                ReadGamingMiscSetting(NitroSense3Protocol.MiscSubfunctionOperationMode));
            return actual == mode ? new("confirmed", "Nitro operation mode confirmed by WMI readback.")
                : actual.HasValue ? new("unverified", "Nitro operation-mode WMI readback differs from the requested mode.")
                : new("unverified", "Nitro operation mode acknowledged; WMI readback unavailable. Preference was not saved.");
        }

        public byte? ReadPowerMode()
        {
            var caps = Capabilities;
            if (!caps.CanWrite(HardwareFeature.Power)) return null;
            string backend = caps.Results[HardwareFeature.Power].Backend;
            byte? actual = backend switch
            {
                "windows-power-plan" => (_testIo != null ? _testIo.ReadActiveWindowsPowerPlan() ?? caps.ActiveWindowsPowerPlan : WindowsPowerPlans.Read().ActiveScheme) is Guid activeGuid
                    ? caps.PowerModes.FirstOrDefault(m => m.WindowsScheme == activeGuid)?.Value
                    : null,
                NitroSense3Protocol.OperationModePipeBackend => NitroSense3Protocol.OperationModeState(
                    _testIo != null
                        ? _testIo.SendNitroSense(NitroSense3Protocol.CmdWmiGetGamingMiscSetting, NitroSense3Protocol.MiscSubfunctionOperationMode, false)
                        : NitroSense3Protocol.Exchange(NitroSense3Protocol.CmdWmiGetGamingMiscSetting, NitroSense3Protocol.MiscSubfunctionOperationMode, false).GetAwaiter().GetResult()),
                NitroSense3Protocol.OperationModeWmiBackend => NitroSense3Protocol.OperationModeState(
                    ReadGamingMiscSetting(NitroSense3Protocol.MiscSubfunctionOperationMode)),
                _ => caps.ActivePowerMode
            };
            if (actual.HasValue && caps.PowerModes.Any(m => m.Value == actual.Value))
            {
                lock (_probeLock)
                {
                    if (_cachedCapabilities != null) _cachedCapabilities = _cachedCapabilities with { ActivePowerMode = actual };
                }
                return actual;
            }
            return null;
        }

        public HardwareOperationResult RestoreSavedPowerMode(int? savedMode, bool applyWhenAlreadyActive = false)
        {
            var caps = Capabilities;
            if (!caps.CanWrite(HardwareFeature.Power))
                return new("failed", "Power control path is not verified for this laptop.");
            byte? current = ReadPowerMode();
            if (!savedMode.HasValue)
                return new("skipped", "No saved power preference to restore.");
            if (savedMode.Value is < 0 or > 255 || !caps.PowerModes.Any(m => m.Value == (byte)savedMode.Value))
                return new("failed", "Saved power profile is not supported by this model.");
            byte target = (byte)savedMode.Value;
            if (!caps.AllowsPower(target, IsOnAc))
                return new("failed", "Saved power profile is not available on the current power source.");
            if (!applyWhenAlreadyActive && current.HasValue && current.Value == target)
                return new("confirmed", "Saved power profile is already active on hardware.");
            return SetPowerMode(target);
        }


        // ============================================================================================
        // SECTION 3: THERMAL MANAGEMENT, FAN SPEEDS & TACHOMETERS
        // ============================================================================================

        /// <summary>
        /// [TRIGGER]     : User changes fan profile (Auto, Max, Custom)
        /// [HARDWARE I/O]: Calls SetGamingFanBehavior with mode bits and uses the selected compatibility backend
        /// [EFFECT]      : Switches EC between hardware thermal curve (Auto), 100% blast (Max), or manual PWM
        /// </summary>
        public HardwareOperationResult SetFanBehavior(byte mode)
        {
            HardwareFeature feature = mode == 1 ? HardwareFeature.FanAuto : mode == 2 ? HardwareFeature.FanMax : HardwareFeature.FanManual;
            var result = Write(feature, mode is >= 1 and <= 3, () =>
            {
                if (mode == 3) _manualFanSession = true;
                bool accepted = SendCommand("SetGamingFanBehavior", 0x09UL | ((ulong)mode << 16) | ((ulong)mode << 22)).success;
                if (_testIo == null && Capabilities.ChassisFamily == AcerChassisFamily.Predator)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            int agentFanMode = mode switch { 0x02 => 1, 0x03 => 2, _ => 0 };
                            await AcerAgentClient.SetFanModeAsync(agentFanMode);
                        }
                        catch { }
                    });
                    accepted = accepted || _cachedObj != null;
                }
                if (accepted && mode == 3)
                {
                    bool cpuOk = SendCommand("SetGamingFanSpeed", 1UL | ((ulong)_customCpuFanSpeed << 8)).success;
                    bool gpuOk = SendCommand("SetGamingFanSpeed", 4UL | ((ulong)_customGpuFanSpeed << 8)).success;
                    if (_testIo == null && Capabilities.ChassisFamily == AcerChassisFamily.Predator)
                    {
                        byte cpu = _customCpuFanSpeed, gpu = _customGpuFanSpeed;
                        _ = Task.Run(async () =>
                        {
                            try { await AcerAgentClient.SetCustomFanSpeedAsync(cpu, gpu); }
                            catch { }
                        });
                        accepted = (cpuOk && gpuOk) || _cachedObj != null;
                    }
                    else
                    {
                        accepted = cpuOk && gpuOk;
                    }
                }
                return Acknowledged(accepted);
            });
            if (result.Accepted) _manualFanSession = mode == 3;
            if (!result.Accepted && mode == 3) RecoverAutomaticFans();
            return result;
        }

        public bool SetFanSpeed(byte cpuSpeed, byte gpuSpeed)
        {
            var result = Write(HardwareFeature.FanManual, cpuSpeed is >= 10 and <= 100 && gpuSpeed is >= 10 and <= 100, () =>
            {
                _manualFanSession = true; // Also recover if only the first of the two writes succeeds.
                if (_testIo == null && Capabilities.ChassisFamily == AcerChassisFamily.Predator)
                {
                    _ = Task.Run(async () =>
                    {
                        try { await AcerAgentClient.SetCustomFanSpeedAsync(cpuSpeed, gpuSpeed); }
                        catch { }
                    });
                }
                bool cpu = SendCommand("SetGamingFanSpeed", 1UL | ((ulong)cpuSpeed << 8)).success;
                bool gpu = (cpu || (_testIo == null && Capabilities.ChassisFamily == AcerChassisFamily.Predator)) &&
                    SendCommand("SetGamingFanSpeed", 4UL | ((ulong)gpuSpeed << 8)).success;
                bool accepted = (cpu && gpu) || (_testIo == null && Capabilities.ChassisFamily == AcerChassisFamily.Predator && _cachedObj != null);
                return Acknowledged(accepted);
            });
            if (result.Accepted) { _customCpuFanSpeed = cpuSpeed; _customGpuFanSpeed = gpuSpeed; _manualFanSession = true; }
            else if (_manualFanSession) RecoverAutomaticFans();
            return result.Accepted;
        }
        public bool SetCpuFanSpeed(byte speed) => SetFanSpeed(speed, _customGpuFanSpeed);
        public bool SetGpuFanSpeed(byte speed) => SetFanSpeed(_customCpuFanSpeed, speed);
        public bool SetAuxFanSpeed(byte speed) => Write(HardwareFeature.AuxFan, speed is >= 10 and <= 100,
            () => Acknowledged(SendCommand("SetGamingFanSpeed", 2UL | ((ulong)speed << 8)).success)).Accepted;
        public void RecoverAutomaticFans()
        {
            if (_manualFanSession && Capabilities.CanWrite(HardwareFeature.FanAuto)) SetFanBehavior(1);
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
                        lock (HardwareWriteGate.CommandLock) { if (!_disposed) next(); }
                    }
                    catch { }

                    // Inter-action pacing to prevent EC firmware buffer saturation (50Hz)
                    Thread.Sleep(20);
                }
            });
        }


        public void SetRgbMode(int mode, byte r, byte g, byte b, byte brightness, byte speed, byte direction)
        {
            if (!Keyboard.SupportsRgb || Keyboard.Effects?.Contains(mode) != true || brightness > 100 || speed is < 1 or > 9 || direction > 1) return;
            _lastR = r; _lastG = g; _lastB = b;
            _brightness = brightness;
            _speed = speed;
            _direction = direction;
            _lastMode = mode;

            for (int i = 0; i < Keyboard.ZoneCount; i++)
                _zoneColors[i] = Color.FromArgb(r, g, b);

            QueueLightingTask(() => ApplyLightingModeCore(mode));
        }

        public void SetSingleZoneColor(int zoneIndex, Color color, int mode = 0)
        {
            if (!Keyboard.SupportsRgb || Keyboard.Effects?.Contains(mode) != true) return;
            if (zoneIndex >= 0 && zoneIndex < Keyboard.ZoneCount)
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
                        for (int i = 0; i < Keyboard.ZoneCount; i++) _zoneColors[i] = color;
                        _lastR = color.R; _lastG = color.G; _lastB = color.B;
                        ApplyLightingModeCore(mode);
                    }
                });
            }
        }

        public void Set3ZoneCustomColor(int zoneIndex, Color color, int mode = 0)
        {
            if (Keyboard.ZoneCount != 4) return; // This legacy visual mapping covers four physical zones.
            if (!Keyboard.SupportsRgb || Keyboard.Effects?.Contains(mode) != true) return;
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
            if (!Keyboard.SupportsRgb) return;
            // SetGamingRgbKb zone bitmask: 1 << zoneIndex (1, 2, 4, 8 for physical zones 1, 2, 3, 4)
            ulong mask = 1ul << Math.Clamp(zoneIndex, 0, 3);
            ulong zonePayload = mask
                | ((ulong)color.R << 8)
                | ((ulong)color.G << 16)
                | ((ulong)color.B << 24);
            Write(HardwareFeature.Rgb, zoneIndex >= 0 && zoneIndex < Keyboard.ZoneCount, () => Acknowledged(SendCommand("SetGamingRgbKb", zonePayload).success));
        }

        public void Set4ZoneColors(Color[] zones, int mode = 0, byte? brightness = null, byte? speed = null)
        {
            if (!Keyboard.SupportsRgb || Keyboard.Effects?.Contains(mode) != true || brightness > 100 || speed is < 1 or > 9) return;
            if (zones != null && zones.Length >= Keyboard.ZoneCount)
            {
                for (int i = 0; i < Keyboard.ZoneCount; i++) _zoneColors[i] = zones[i];
                _lastR = zones[0].R; _lastG = zones[0].G; _lastB = zones[0].B;
                _lastMode = mode;
                if (brightness.HasValue) _brightness = brightness.Value;
                if (speed.HasValue) _speed = speed.Value;
                QueueLightingTask(() => Apply4ZoneLightingCore(mode));
            }
        }

        public void Set3ZoneColors(Color zone1, Color zone2, Color zone3, int mode = 0)
        {
            if (Keyboard.ZoneCount != 4) return;
            var zones4 = new Color[] { zone1, zone2, zone2, zone3 };
            Set4ZoneColors(zones4, mode);
        }

        public void SetBrightness(byte brightness)
        {
            if (!Keyboard.SupportsBrightness || brightness > 100) return;
            _brightness = brightness;

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

        public void SetSpeed(byte speed)
        {
            if (!Keyboard.SupportsRgb || speed is < 1 or > 9) return;
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
            if (!Keyboard.SupportsRgb || direction > 1) return;
            _direction = direction;
            QueueLightingTask(() => ApplyLightingModeCore(_lastMode));
        }

        public void SetKeyboardColor(Color c, int mode = 0)
        {
            if (!Keyboard.SupportsRgb || Keyboard.Effects?.Contains(mode) != true) return;
            _lastR = c.R; _lastG = c.G; _lastB = c.B;
            _lastMode = mode;
            for (int i = 0; i < Keyboard.ZoneCount; i++) _zoneColors[i] = c;

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
                if (!SendLedCommand(payload)) return;

                if (mode == 0)
                {
                    Thread.Sleep(30);
                    for (int z = 0; z < Keyboard.ZoneCount; z++)
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
            if (!Keyboard.SupportsRgb || Keyboard.Effects?.Contains(8) != true) return; // Binary power uses the explicit keyboard controller.
            _brightness = 0;
            _lastMode = 8;
            QueueLightingTask(() => ApplyLightingModeCore(8));
        }

        public void ApplyLightingSynchronous(Color[] zones, int mode = 0, byte brightness = 100, byte speed = 5)
        {
            if (!Keyboard.SupportsRgb || Keyboard.Effects?.Contains(mode) != true || brightness > 100 || speed is < 1 or > 9) return;
            if (zones != null && zones.Length >= Keyboard.ZoneCount)
            {
                for (int i = 0; i < Keyboard.ZoneCount; i++) _zoneColors[i] = zones[i];
                _lastR = zones[0].R; _lastG = zones[0].G; _lastB = zones[0].B;
                _lastMode = mode;
                _brightness = brightness;
                _speed = Math.Clamp(speed, (byte)1, (byte)9);
                lock (HardwareWriteGate.CommandLock) Apply4ZoneLightingCore(mode);
            }
        }

        private void ApplyLightingModeCore(int mode)
        {
            if (!Keyboard.SupportsRgb || Keyboard.Effects?.Contains(mode) != true) return;
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
            if (!SendLedCommand(payload)) return;

            SyncLightingProfileIni();

        }


        private void Apply4ZoneLightingCore(int mode)
        {
            if (!Keyboard.SupportsRgb || Keyboard.Effects?.Contains(mode) != true) return;
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
            if (!SendLedCommand(payload)) return;

            if (mode == 0)
            {
                // 2. In static 4-zone mode, apply individual physical zone colors AFTER SendLedCommand
                // with OEM driver 30ms inter-zone sleep so the EC controller does NOT overwrite per-zone colors
                Thread.Sleep(30);
                for (int z = 0; z < Keyboard.ZoneCount; z++)
                {
                    ApplyZoneLightingCore(z, _zoneColors[z]);
                    Thread.Sleep(30);
                }
            }

            SyncLightingProfileIni();
        }

        public void SyncLightingProfileIni() { /* Firmware readback is unavailable; do not overwrite OEM profiles. */ }


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

        private static bool TryReadWindowsPowerOverlay(out Guid overlay)
        {
            overlay = default;
            try { return PowerGetActualOverlayScheme(out overlay) == 0; }
            catch { return false; }
        }

        private static HardwareOperationResult ApplyWindowsPowerOverlay(byte mode)
        {
            Guid wanted = mode switch { 0 => OVERLAY_EFFICIENCY, 4 => OVERLAY_PERFORMANCE, _ => OVERLAY_BALANCED };
            if (PowerSetActiveOverlayScheme(wanted) != 0) return new("failed", "Windows rejected the requested power mode.");
            return TryReadWindowsPowerOverlay(out var actual) && actual == wanted
                ? new("confirmed", "Windows power mode confirmed. No Acer thermal-profile command was sent.")
                : new("unverified", "Windows accepted the power mode, but readback did not confirm it.");
        }

        // ============================================================================================
        // SECTION 4: BATTERY HEALTH & 80% CHARGE LIMITER
        // ============================================================================================

        /// <summary>
        /// [TRIGGER]     : User clicks "80% Battery Limit" toggle in UI
        /// [HARDWARE I/O]: Invokes ACPI WMI SetBatteryHealthControl(uFunctionStatus=1) with status readback
        /// [EFFECT]      : Embedded Controller and battery charger IC cap charging circuit at 80% capacity
        /// </summary>
        public bool SetBatteryChargeLimit(bool enable, bool startServices = false)
        {
            HardwareOperationResult Finish(string status, string message)
            {
                lock (_probeLock)
                    if (_cachedCapabilities != null) _cachedCapabilities = _cachedCapabilities with { BatteryLimitEnabled = status == "confirmed" ? enable : null };
                return new(status, message);
            }
            var result = Write(HardwareFeature.BatteryLimit, true, () =>
            {
                if (_testIo != null)
                {
                    if (!_testIo.SetBatteryLimit(enable)) return new("failed", "Battery command rejected.");
                    return _testIo.ReadBatteryLimit() == enable ? Finish("confirmed", "Battery state confirmed.") : Finish("unverified", "Battery state unverified.");
                }
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM BatteryControl");
                using var results = searcher.Get();
                using var iterator = results.GetEnumerator();
                if (!iterator.MoveNext() || iterator.Current is not ManagementObject obj) return new("failed", "Battery interface unavailable.");
                using (obj)
                using (var args = obj.GetMethodParameters("SetBatteryHealthControl"))
                {
                    args["uBatteryNo"] = (byte)1; args["uFunctionMask"] = (byte)1;
                    args["uFunctionStatus"] = (byte)(enable ? 1 : 0); args["uReservedIn"] = new byte[5];
                    using var reply = obj.InvokeMethod("SetBatteryHealthControl", args, new InvokeMethodOptions { Timeout = TimeSpan.FromSeconds(2) });
                    if (reply?["uReturn"] == null || Convert.ToUInt16(reply["uReturn"]) != 0) return new("failed", "Battery command rejected.");
                }
                return TryReadBatteryLimit(out var actual) && actual == enable
                    ? Finish("confirmed", "Charge-limit state confirmed by BatteryControl.")
                    : Finish("unverified", "Battery command acknowledged, but charge-limit state could not be confirmed.");
            });
            return result.Confirmed;
        }
        public bool IsBatteryControlSupported() => Capabilities.SupportsBatteryControl;


        // ============================================================================================
        // SECTION 5: GPU MUX SWITCH, LCD OVERDRIVE, STARTUP SOUND & USB CHARGING
        // ============================================================================================

        #region Hardware Features (GPU MUX, LCD Overdrive, Backlight Sleep, Windows Key Lock)

        /// <summary>
        /// [TRIGGER]     : Dashboard initialization / polling
        /// [HARDWARE I/O]: Queries AASSvc TCP 46933 (packet 101)
        /// [EFFECT]      : Reads active GPU MUX state: 0 = Optimus (Hybrid), 1 = Discrete GPU, 2 = Auto
        /// </summary>
        public async Task<int?> GetGpuModeAsync()
        {
            return _testIo != null ? null : await AcerAgentClient.GetGpuModeAsync();
        }

        /// <summary>
        /// [TRIGGER]     : Probing GPU MUX capability at boot
        /// [HARDWARE I/O]: Queries AASSvc TCP 46933 for supported modes bitmask
        /// [EFFECT]      : Bit 0 = Optimus, Bit 1 = Discrete, Bit 2 = Auto
        /// </summary>
        public Task<int> GetGpuModeCapabilityAsync() => Task.FromResult(Capabilities.GpuModeMask);


        /// <summary>
        /// [TRIGGER]     : User selects GPU Working Mode (Optimus, Discrete, Auto)
        /// [HARDWARE I/O]: Dual-dispatches to Acer OEM Agent Service and ACPI Feature ID 0x02
        /// [EFFECT]      : Writes MUX mode into BIOS NVRAM (takes effect after system reboot)
        /// </summary>
        public Task<bool> SetGpuModeAsync(int mode)
        {
            var result = Write(HardwareFeature.GpuMux, mode is >= 0 and <= 2 && (Capabilities.GpuModeMask & (1 << mode)) != 0, () =>
            {
                bool ok = _testIo?.SetGpuMode(mode) ?? AcerAgentClient.SetGpuModeAsync(mode).GetAwaiter().GetResult();
                if (!ok) return new("failed", "GPU mode request failed.");
                return new("unverified", "GPU mode request acknowledged; active state can only be verified after the operator restarts Windows.");
            });
            return Task.FromResult(result.Accepted);
        }


        /// <summary>
        /// [TRIGGER]     : User toggles LCD Overdrive (3ms response time)
        /// [HARDWARE I/O]: Calls SetGamingMiscSetting with Feature ID 0x10 and state bit
        /// [EFFECT]      : Embedded Controller sets panel overdrive voltage to eliminate ghosting
        /// </summary>
        public bool SetLcdOverdrive(bool enable) => Write(HardwareFeature.LcdOverdrive, true,
            () => Acknowledged(SendCommand("SetGamingMiscSetting", ((enable ? 1UL : 0UL) << 32) | 0x10UL).success)).Accepted;

        public bool SetBootSound(bool enable) => Write(HardwareFeature.BootSound, true,
            () => Acknowledged(SendCommand("SetGamingMiscSetting", ((enable ? 1UL : 0UL) << 32) | 0x0EUL).success)).Accepted;


        public bool GetBootSound()
        {
            if (_testIo != null) return false;
            try
            {
                var task = AcerAgentClient.GetBootSoundAsync();
                if (task.Wait(150) && task.Result.HasValue)
                {
                    return task.Result.Value;
                }
            }
            catch { }

            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PredatorControl");
                if (key?.GetValue("BootSound") is int val) return val == 1;
            }
            catch { }
            return false;
        }

        /// <summary>
        /// [TRIGGER]     : User toggles "Power-off USB Charging" and selects safety cutoff percentage
        /// [HARDWARE I/O]: Calls SetGamingMiscSetting with Feature ID 0x0C (state) and 0x0D (cutoff) and syncs settings.json
        /// [EFFECT]      : Embedded Controller energizes designated high-power USB port while laptop is off/sleeping
        /// </summary>
        public bool SetUsbCharging(bool enable, int cutoffPercent = 10) => Write(HardwareFeature.UsbCharging,
            cutoffPercent is 10 or 20 or 30, () =>
            {
                bool state = SendCommand("SetGamingMiscSetting", ((enable ? 1UL : 0UL) << 32) | 0x0CUL).success;
                bool cutoff = state && SendCommand("SetGamingMiscSetting", ((ulong)cutoffPercent << 32) | 0x0DUL).success;
                return Acknowledged(state && cutoff);
            }).Accepted;


        public bool GetUsbCharging()
        {
            if (_testIo != null) return false;
            try
            {
                string qaSettingsPath = @"C:\ProgramData\Acer\QA\settings.json";
                if (File.Exists(qaSettingsPath))
                {
                    string json = File.ReadAllText(qaSettingsPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("USBChargeSwitch", out var sw))
                    {
                        if (sw.ValueKind == JsonValueKind.True || sw.ValueKind == JsonValueKind.False)
                            return sw.GetBoolean();
                    }
                    if (doc.RootElement.TryGetProperty("USBChargeState", out var st))
                    {
                        return st.GetInt32() > 0;
                    }
                }
            }
            catch { }

            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PredatorControl");
                if (key?.GetValue("UsbCharging") is int val) return val == 1;
            }
            catch { }
            return false;
        }

        public bool GetBacklight30s() => KeyboardControl.State.IdleTimeout ?? true;

        public bool SetBacklight30s(bool enable) => KeyboardControl.Apply(null, enable).Confirmed;

        // Windows key filtering is provided by the existing OS hook; no guessed RGB firmware packet.
        public bool SetWinKeyLock(bool lockKeys) => false;


        #endregion

        #region Hardware Capability & Diagnostic Probe

        private static readonly Lazy<DeviceIdentity> Identity = new(DeviceDetector.Read);
        public static DeviceIdentity GetSystemIdentity() => Identity.Value;

        public string ExportDiagnosticReport()
        {
            var caps = Capabilities;
            var sample = GetAllSensors(IsOnAc);
            return CompatibilityDiagnostics.Export(caps, Updater.CurrentText,
                new { wmiClass = _discovery?.WmiClass, methods = _discovery?.Methods, keyboardReadable = _discovery?.KeyboardConfigurationReadable,
                    batteryStatusReadable = _discovery?.BatteryStatusReadable, windowsPowerOverlayReadable = _discovery?.WindowsPowerOverlayReadable,
                    nitroSense3ReadStatus = _testIo == null ? NitroSense3Protocol.LastReadFailure : null,
                    windowsPowerPlans = _discovery?.WindowsPowerPlans,
                    supportedSensorMask = _discovery?.SupportedSensorMask,
                    sensorReadings = new { sample.cpuTemp, sample.gpuTemp, sample.cpuFanRpm, sample.gpuFanRpm, sample.auxFanRpm, sample.gpuPowerW } },
                LastOperation, revision: Updater.BuildRevision);
        }

        #endregion


        public void Dispose()
        {
            lock (HardwareWriteGate.CommandLock)
            {
                RecoverAutomaticFans();
                lock (_lock)
                {
                    if (_disposed) return;
                    _disposed = true;
                    lock (_lightingLock) _pendingLightingAction = null;
                    _cachedObj?.Dispose();
                    _cachedObj = null;
                }
            }
        }
    }
}
