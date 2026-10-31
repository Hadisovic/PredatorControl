using System.Management;

namespace PredatorControlApp;

public partial class WmiController
{
    private readonly IHardwareTestIo? _testIo;
    public WmiController() { }
    internal WmiController(HardwareDiscovery discovery, IHardwareTestIo io, IEnumerable<DeviceProfile>? profiles = null)
    {
        _testIo = io;
        _discovery = discovery;
        _cachedCapabilities = DeviceProfileResolver.Resolve(discovery, profiles);
    }
    private bool IsOnAc => _testIo?.IsOnAc ?? SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
    private HardwareWriteGate? _writeGate;
    private HardwareDiscovery? _discovery;
    private bool _manualFanSession;
    private HardwareWriteGate WriteGate => _writeGate ??= new(() => Capabilities,
        name => _testIo != null ? Capabilities.Services.GetValueOrDefault(name) is { Exists: true, Running: true, Trusted: true, InterfaceWorking: true } : OemServiceManager.ReadServiceEvidence(name) is { Exists: true, Running: true, Trusted: true });
    public HardwareOperationResult? LastOperation => WriteGate.LastOperation;
    private HardwareOperationResult Write(HardwareFeature feature, bool valid, Func<HardwareOperationResult> command) =>
        WriteGate.Execute(feature, valid && !_disposed, command);
    private static HardwareOperationResult Acknowledged(bool ok) => ok
        ? new("unverified", "Command acknowledged; firmware state readback is unavailable.")
        : new("failed", "Hardware command failed. State and saved preference were not confirmed.");

    public HardwareCapabilities RefreshCompatibility()
    {
        if (_testIo != null) return Capabilities;
        // Called explicitly after service repair, never from the telemetry poll loop.
        lock (HardwareWriteGate.CommandLock)
        lock (_probeLock)
        {
            _discovery = null;
            _cachedCapabilities = null;
            _keyboardControl = null;
            return Capabilities;
        }
    }

    public void RefreshServiceAvailability()
    {
        if (_testIo != null || _discovery == null) return;
        var services = OemServiceManager.HardwareServices.ToDictionary(n => n, OemServiceManager.ReadServiceEvidence);
        lock (HardwareWriteGate.CommandLock)
        lock (_probeLock)
        {
            if (_disposed || _discovery == null) return;
            bool changed = services.Any(s => !_discovery.Services.TryGetValue(s.Key, out var old) ||
                old.Exists != s.Value.Exists || old.Running != s.Value.Running || old.Trusted != s.Value.Trusted);
            if (!changed) return;
            bool? coolBoost = _discovery.Identity.NormalizedModel == "AN515-54" &&
                _discovery.Identity.BoardProduct.Equals("Octavia_CFS", StringComparison.OrdinalIgnoreCase) && services["PSSvc"] is { Running: true, Trusted: true }
                ? NitroSense3Protocol.CoolBoostState(NitroSense3Protocol.Exchange(20, 519, false).GetAwaiter().GetResult()) : null;
            byte? opMode = IsNitroOperationModeModel(_discovery.Identity) && services["PSSvc"] is { Running: true, Trusted: true }
                ? NitroSense3Protocol.OperationModeState(NitroSense3Protocol.Exchange(NitroSense3Protocol.CmdWmiGetGamingMiscSetting, NitroSense3Protocol.MiscSubfunctionOperationMode, false).GetAwaiter().GetResult()) : null;
            services["PSSvc"] = services["PSSvc"] with { InterfaceWorking = coolBoost.HasValue || opMode.HasValue };
            if (coolBoost.HasValue) _coolBoost = coolBoost.Value;
            int mux = services["AASSvc"] is { Running: true, Trusted: true }
                ? AcerAgentClient.GetGpuModeCapabilityAsync().GetAwaiter().GetResult() : 0;
            services["AASSvc"] = services["AASSvc"] with { InterfaceWorking = mux > 0 };
            _discovery = _discovery with
            {
                Services = services, GpuModeMask = mux,
                NitroOperationMode = opMode ?? _discovery.NitroOperationMode,
                NitroOperationModeReadable = opMode.HasValue || _discovery.NitroOperationModeReadable
            };
            _cachedCapabilities = DeviceProfileResolver.Resolve(_discovery) with { TelemetryChannels = _cachedCapabilities?.TelemetryChannels ?? new Dictionary<string, TelemetryChannelEvidence>() };
        }
    }

    private static bool IsNitroOperationModeModel(DeviceIdentity identity) =>
        identity.IsAcer && identity.NormalizedModel is "AN515-46" or "AN515-47" or "AN515-58" or "AN517-42" or "AN517-43" or "AN517-55";

    public void ObserveTelemetry(TelemetrySnapshot sample)
    {
        var channels = new (string Name, bool Available, string Source)[]
        {
            ("cpuTemp", sample.CpuTemp.HasValue, "profile-mapped Acer WMI"), ("gpuTemp", sample.GpuTemp.HasValue, "profile-mapped Acer WMI / trusted NVML"),
            ("cpuFanRpm", sample.CpuFanRpm.HasValue, "profile-mapped Acer WMI"), ("gpuFanRpm", sample.GpuFanRpm.HasValue, "profile-mapped Acer WMI"),
            ("auxFanRpm", sample.AuxFanRpm.HasValue, "profile-mapped Acer WMI"), ("cpuUsage", sample.CpuUsage.HasValue, "Windows GetSystemTimes"),
            ("gpuUsage", sample.GpuUsage.HasValue, "trusted NVML"), ("gpuPowerW", sample.GpuPowerW.HasValue, "trusted NVML / profile-mapped Acer WMI"),
            ("batteryMeasurements", sample.BatteryPercent.HasValue, "Windows PowerStatus / BatteryHealthMonitor")
        };
        lock (_probeLock)
        {
            var caps = _cachedCapabilities;
            if (caps == null || _disposed) return;
            if (channels.All(c => caps.TelemetryChannels.TryGetValue(c.Name, out var previous) &&
                previous.Status == (c.Available ? CapabilityStatus.Supported : CapabilityStatus.Unavailable))) return;
            _cachedCapabilities = caps with
            {
                TelemetryChannels = new System.Collections.ObjectModel.ReadOnlyDictionary<string, TelemetryChannelEvidence>(channels.ToDictionary(c => c.Name,
                    c => new TelemetryChannelEvidence(c.Available ? CapabilityStatus.Supported : CapabilityStatus.Unavailable, c.Source,
                        c.Available ? "Plausible sample observed; read-only." : "No current sample. Display N/A; this does not authorize any writes.")))
            };
        }
    }

    private HardwareCapabilities ProbeCapabilities()
    {
        var methods = new HashSet<string>(StringComparer.Ordinal);
        var obj = GetWmiObject();
        if (obj != null)
        {
            try
            {
                using var cls = new ManagementClass(@"root\WMI", _discoveredClassName, null);
                foreach (MethodData method in cls.Methods) methods.Add(method.Name);
            }
            catch { }
        }
        var identity = GetSystemIdentity();
        bool nitro3 = identity.NormalizedModel == "AN515-54" && identity.BoardProduct.Equals("Octavia_CFS", StringComparison.OrdinalIgnoreCase);
        bool nitroOpModeModel = IsNitroOperationModeModel(identity);
        bool keyboard = identity.IsAcer && new KeyboardFirmwareTransport(nitro3).TryRead(out _);
        bool? batteryLimit = null;
        bool battery = identity.IsAcer && TryReadBatteryLimit(out batteryLimit);
        try
        {
            using var cls = new ManagementClass(@"root\WMI", "BatteryControl", null);
            foreach (MethodData method in cls.Methods) methods.Add("BatteryControl." + method.Name);
        }
        catch { }
        ulong? sensorInfo = identity.IsAcer && methods.Contains("GetGamingSysInfo") ? ReadGamingSystemInfo(0) : null;
        var services = OemServiceManager.HardwareServices.ToDictionary(n => n, OemServiceManager.ReadServiceEvidence);
        bool? directCoolBoost = nitro3 ? NitroSense3Protocol.CoolBoostState(NitroSense3Protocol.ExchangeDirect(20, 519, false)) : null;
        if (directCoolBoost.HasValue)
        {
            try { using var cls = new ManagementClass(@"root\WMI", "APGeAction", null); if (cls.Methods["SetFunction"] != null) methods.Add("APGeAction.SetFunction"); }
            catch { directCoolBoost = null; }
        }
        bool? coolBoost = nitro3 && services["PSSvc"] is { Running: true, Trusted: true }
            ? NitroSense3Protocol.CoolBoostState(NitroSense3Protocol.Exchange(20, 519, false).GetAwaiter().GetResult()) : null;
        byte? directOpMode = nitroOpModeModel && methods.Contains("GetGamingMiscSetting") && methods.Contains("SetGamingMiscSetting")
            ? NitroSense3Protocol.OperationModeState(ReadGamingMiscSetting(NitroSense3Protocol.MiscSubfunctionOperationMode)) : null;
        byte? pipeOpMode = nitroOpModeModel && services["PSSvc"] is { Running: true, Trusted: true }
            ? NitroSense3Protocol.OperationModeState(NitroSense3Protocol.Exchange(NitroSense3Protocol.CmdWmiGetGamingMiscSetting, NitroSense3Protocol.MiscSubfunctionOperationMode, false).GetAwaiter().GetResult()) : null;
        bool? batteryBoost = nitroOpModeModel && methods.Contains("GetGamingSysInfo")
            ? NitroSense3Protocol.BatteryBoostReady(ReadGamingSystemInfo(0x0002)) : null;
        services["PSSvc"] = services["PSSvc"] with { InterfaceWorking = coolBoost.HasValue || pipeOpMode.HasValue };
        if (coolBoost.HasValue) _coolBoost = coolBoost.Value;
        if (directCoolBoost.HasValue) _coolBoost = directCoolBoost.Value;
        int mux = 0;
        if (identity.IsAcer && services["AASSvc"] is { Running: true, Trusted: true })
        {
            try { mux = AcerAgentClient.GetGpuModeCapabilityAsync().GetAwaiter().GetResult(); } catch { }
            services["AASSvc"] = services["AASSvc"] with { InterfaceWorking = mux > 0 };
        }
        _discovery = new()
        {
            Identity = identity, Methods = methods, WmiClass = _discoveredClassName ?? "",
            KeyboardConfigurationReadable = keyboard, BatteryStatusReadable = battery, BatteryLimitEnabled = batteryLimit,
            WindowsPowerOverlayReadable = TryReadWindowsPowerOverlay(out _),
            WindowsPowerPlans = nitro3 ? WindowsPowerPlans.Read() : new([], null, "This profile does not select Windows power plans."),
            SupportedSensorMask = sensorInfo.HasValue ? (ushort)((sensorInfo.Value >> 24) & 0xFFFF) : null,
            NitroCoolBoostEnabled = directCoolBoost ?? coolBoost,
            NitroDirectCoolBoostReadable = directCoolBoost.HasValue,
            NitroOperationMode = directOpMode ?? pipeOpMode ?? (nitroOpModeModel ? NitroSense3Protocol.ReadRegistryOperationMode() : null),
            NitroOperationModeReadable = pipeOpMode.HasValue,
            NitroDirectOperationModeReadable = directOpMode.HasValue,
            NitroBatteryBoostReady = batteryBoost,
            Services = services, GpuModeMask = mux
        };
        return DeviceProfileResolver.Resolve(_discovery);
    }

    private ulong? ReadGamingMiscSetting(uint command)
    {
        if (_testIo != null) return _testIo.ReadGamingMiscSetting(command);
        lock (_lock)
        {
            try
            {
                var obj = GetWmiObject();
                if (obj == null) return null;
                using var inParams = obj.GetMethodParameters("GetGamingMiscSetting");
                inParams["gmInput"] = command;
                using var outParams = obj.InvokeMethod("GetGamingMiscSetting", inParams, new InvokeMethodOptions { Timeout = TimeSpan.FromSeconds(2) });
                if (outParams?["gmOutput"] == null) return null;
                return Convert.ToUInt64(outParams["gmOutput"]);
            }
            catch { InvalidateCache(); return null; }
        }
    }

    // Read-only BatteryControl protocol; service/registry presence is never evidence of support.
    private static bool TryReadBatteryLimit(out bool? enabled)
    {
        enabled = null;
        try
        {
            using var searcher = new ManagementObjectSearcher(new ManagementScope(@"root\WMI"), new ObjectQuery("SELECT * FROM BatteryControl"),
                new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(2) });
            using var objects = searcher.Get();
            using var iterator = objects.GetEnumerator();
            if (!iterator.MoveNext() || iterator.Current is not ManagementObject obj) return false;
            using (obj)
            using (var args = obj.GetMethodParameters("GetBatteryHealthControlStatus"))
            {
                args["uBatteryNo"] = (byte)1;
                args["uFunctionQuery"] = (byte)1;
                args["uReserved"] = new byte[2];
                using var reply = obj.InvokeMethod("GetBatteryHealthControlStatus", args, new InvokeMethodOptions { Timeout = TimeSpan.FromSeconds(2) });
                return reply != null && TryDecodeBatteryStatus(reply["uReturn"], reply["uFunctionList"], reply["uFunctionStatus"], out enabled);
            }
        }
        catch { return false; }
    }

    // BatteryControl MOF: two-byte return code, function-list byte, five status bytes.
    internal static bool TryDecodeBatteryStatus(object? returnCode, object? functionList, object? functionStatus, out bool? enabled)
    {
        enabled = null;
        if (returnCode is not byte[] { Length: 2 } code || code.Any(b => b != 0) ||
            functionList is not byte list || (list & 1) == 0 || functionStatus is not byte[] { Length: 5 } status) return false;
        enabled = status[0] != 0;
        return true;
    }

    private int? ReadChannel(string channel) => Capabilities.Sensors.TryGetValue(channel, out ulong id)
        ? CompatibilityDiagnostics.Sensor(channel, GetSensorReading(id)) : null;

    // Validate firmware payloads again at the lowest WMI boundary, including internal/background callers.
    private bool AuthorizeWmi(string method, ulong input)
    {
        return method switch
        {
            "SetGamingProfile" => (input <= 255 || ((input >> 24) == 1 && (input & 0xFFFFFF) <= 255))
                && Capabilities.AllowsPower((byte)(input & 0xFF), IsOnAc)
                && HardwareWriteGate.InScope(HardwareFeature.Power, "acer-wmi-profile"),
            "SetGamingFanBehavior" => HardwareWriteGate.InScope(HardwareFeature.FanAuto, "acer-wmi-fans") || HardwareWriteGate.InScope(HardwareFeature.FanMax, "acer-wmi-fans") ||
                HardwareWriteGate.InScope(HardwareFeature.FanManual, "acer-wmi-fans") || HardwareWriteGate.InScope(HardwareFeature.CoolBoost, "acer-wmi-coolboost"),
            "SetGamingFanSpeed" => HardwareWriteGate.InScope(HardwareFeature.FanManual, "acer-wmi-fans") || HardwareWriteGate.InScope(HardwareFeature.AuxFan, "acer-wmi-aux"),
            "SetGamingRgbKb" => HardwareWriteGate.InScope(HardwareFeature.Rgb, "acer-wmi-rgb") ||
                (HardwareWriteGate.InScope(HardwareFeature.WinKey, "acer-wmi-winkey") &&
                 input is 0xFC00000000000003UL or 0xF900000000000303UL),
            "SetGamingMiscSetting" => (input & 0xFFFFFFFFUL) switch
            {
                0x10 => HardwareWriteGate.InScope(HardwareFeature.LcdOverdrive, "acer-wmi-misc"),
                0x0E => HardwareWriteGate.InScope(HardwareFeature.BootSound, "acer-wmi-misc"),
                0x0C or 0x0D => HardwareWriteGate.InScope(HardwareFeature.UsbCharging, "acer-wmi-misc"),
                _ when (input & 0xFFUL) == 0x02 && ((input >> 8) & 0xFFUL) is >= 1 and <= 3 =>
                    HardwareWriteGate.InScope(HardwareFeature.GpuMux, "acer-agent"),
                _ when (input & 0xFFUL) == NitroSense3Protocol.MiscSubfunctionOperationMode && (input >> 16) == 0 =>
                    Capabilities.AllowsPower((byte)((input >> 8) & 0xFF), IsOnAc) &&
                    ((NitroSense3Protocol.IsSupportedOperationMode((byte)((input >> 8) & 0xFF)) &&
                      HardwareWriteGate.InScope(HardwareFeature.Power, NitroSense3Protocol.OperationModeWmiBackend)) ||
                     HardwareWriteGate.InScope(HardwareFeature.Power, "acer-wmi-profile")),
                _ => false
            },
            _ => false
        };
    }
}
