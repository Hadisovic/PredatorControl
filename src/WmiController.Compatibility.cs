using System.Management;

namespace PredatorControlApp;

public partial class WmiController
{
    private readonly IHardwareTestIo? _testIo;
    private readonly bool _readOnly;
    private readonly IEnumerable<DeviceProfile>? _testProfiles;
    public WmiController() { }
    public WmiController(bool readOnly) { _readOnly = readOnly; }
    internal WmiController(HardwareDiscovery discovery, IHardwareTestIo io, IEnumerable<DeviceProfile>? profiles = null, bool readOnly = false)
    {
        _testIo = io;
        _readOnly = readOnly;
        _testProfiles = profiles;
        _discovery = discovery;
        _cachedCapabilities = DeviceProfileResolver.Resolve(discovery, profiles);
    }
    private bool IsOnAc => _testIo?.IsOnAc ?? SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
    private HardwareWriteGate? _writeGate;
    private HardwareDiscovery? _discovery;
    private long _compatibilityRevision;
    private int _compatibilityStale;
    private readonly object _refreshLock = new();
    private Task<HardwareCapabilities>? _compatibilityRefresh;
    public event EventHandler? CompatibilityChanged;
    private bool _manualFanSession;
    private int? _latestCpuThermalSample;
    private int? _latestGpuThermalSample;
    private long _latestThermalSampleTick;
    private HardwareWriteGate WriteGate => _writeGate ??= new(() => Capabilities, ServiceStillMatches);
    private bool ServiceStillMatches(string name)
    {
        var expected = Capabilities.Services.GetValueOrDefault(name);
        var live = _testIo != null ? _testIo.ReadServiceEvidence(name) ?? expected : OemServiceManager.ReadServiceEvidence(name, refreshFingerprint: true);
        bool matches = live is { Exists: true, Running: true, Trusted: true } && expected is { InterfaceWorking: true } &&
            live.BinarySha256 == expected.BinarySha256 && live.ProtocolBinarySha256 == expected.ProtocolBinarySha256 && live.ProcessId == expected.ProcessId;
        if (!matches && _testIo == null) RequestCompatibilityRefresh();
        return matches;
    }
    public HardwareOperationResult? LastOperation => WriteGate.LastOperation;
    private HardwareOperationResult Write(HardwareFeature feature, bool valid, Func<HardwareOperationResult> command)
    {
        if (_readOnly)
        {
            var denied = new HardwareOperationResult("failed", $"{feature}: diagnostics mode does not dispatch hardware writes.", "read-only-diagnostics", feature);
            WriteGate.RecordResult(denied);
            return denied;
        }
        return WriteGate.Execute(feature, valid && !_disposed, command);
    }
    private static HardwareOperationResult Acknowledged(bool ok) => ok
        ? new("unverified", "Command acknowledged; firmware state readback is unavailable.")
        : new("failed", "Hardware command failed. State and saved preference were not confirmed.");

    public HardwareCapabilities RefreshCompatibility()
    {
        if (_testIo != null)
        {
            lock (HardwareWriteGate.CommandLock)
            lock (_probeLock)
            {
                _keyboardControl = null;
                _cachedCapabilities = DeviceProfileResolver.Resolve(_discovery!, _testProfiles) with { Revision = Interlocked.Increment(ref _compatibilityRevision) };
                Interlocked.Exchange(ref _compatibilityStale, 0);
                return _cachedCapabilities;
            }
        }
        Interlocked.Exchange(ref _compatibilityStale, 1);
        HardwareCapabilities refreshed;
        lock (HardwareWriteGate.CommandLock)
        {
            if (_disposed) return HardwareCapabilities.None with { IsCurrent = false };
            _keyboardControl = null;
            lock (_lightingLock) _pendingLightingAction = null;
            lock (_lock)
            {
                _cachedObj?.Dispose();
                _cachedObj = null;
            }
            // Refresh does not restore settings or send firmware writes.
            try { refreshed = ProbeCapabilities(); }
            catch { refreshed = HardwareCapabilities.None with { Explanation = "Hardware discovery failed. Refresh compatibility to retry." }; }
            lock (_probeLock)
            {
                _cachedCapabilities = refreshed = refreshed with { Revision = Interlocked.Increment(ref _compatibilityRevision) };
                Interlocked.Exchange(ref _compatibilityStale, 0);
            }
        }
        CompatibilityChanged?.Invoke(this, EventArgs.Empty);
        return refreshed;
    }

    public Task<HardwareCapabilities> RefreshCompatibilityAsync()
    {
        lock (_refreshLock)
        {
            if (_compatibilityRefresh is { IsCompleted: false }) return _compatibilityRefresh;
            Interlocked.Exchange(ref _compatibilityStale, 1); // Revoke before scheduling the worker.
            return _compatibilityRefresh = Task.Run(RefreshCompatibility);
        }
    }

    public void RequestCompatibilityRefresh() => _ = RefreshCompatibilityAsync();

    public void InvalidateCompatibility()
    {
        Interlocked.Exchange(ref _compatibilityStale, 1);
        CompatibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshServiceAvailability()
    {
        if (_testIo != null || _discovery == null) return;
        var services = OemServiceManager.HardwareServices.ToDictionary(n => n, n => OemServiceManager.ReadServiceEvidence(n));
        bool changed;
        lock (_probeLock)
        {
            if (_disposed || _discovery == null) return;
            changed = services.Any(s => !_discovery.Services.TryGetValue(s.Key, out var old) ||
                old.Exists != s.Value.Exists || old.Running != s.Value.Running || old.Trusted != s.Value.Trusted ||
                old.Version != s.Value.Version ||
                old.BinarySha256 != s.Value.BinarySha256 || old.ProtocolBinarySha256 != s.Value.ProtocolBinarySha256 || old.ProcessId != s.Value.ProcessId);
        }
        // Never retain a previous process's capability readbacks after service recovery.
        if (changed) RefreshCompatibilityAsync().GetAwaiter().GetResult();
    }

    private static bool IsNitroOperationModeModel(DeviceIdentity identity) =>
        identity.IsAcer && (identity.NormalizedModel is "AN515-46" or "AN515-47" or "AN515-58" or "AN517-42" or "AN517-43" or "AN517-55" ||
        DynamicDeviceProfileCatalog.TryGetModel(identity.NormalizedModel) is { MachineType: 2 });

    public void ObserveTelemetry(TelemetrySnapshot sample)
    {
        lock (_probeLock)
        {
            _latestCpuThermalSample = sample.CpuTemp;
            _latestGpuThermalSample = sample.GpuTemp;
            _latestThermalSampleTick = Environment.TickCount64;
        }
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

    private bool TryPrepareReviewedFanSpeeds(int requestedCpu, int requestedGpu, out byte cpuSpeed, out byte gpuSpeed)
    {
        cpuSpeed = gpuSpeed = 0;
        int? cpuTemp, gpuTemp;
        long sampleTick;
        lock (_probeLock)
        {
            cpuTemp = _latestCpuThermalSample;
            gpuTemp = _latestGpuThermalSample;
            sampleTick = _latestThermalSampleTick;
        }
        if (FanCurveSafety.MustRecover(cpuTemp, gpuTemp, Environment.TickCount64 - sampleTick) ||
            !VerifiedAcerProtocol.TryGetCpuGpuFans(Capabilities.FanChannels, out var cpuFan, out var gpuFan))
            return false;

        int floor = FanCurveSafety.FloorSpeed(cpuTemp, gpuTemp);
        int cpu = Math.Max(Math.Max(requestedCpu, 10), floor);
        int gpu = Math.Max(Math.Max(requestedGpu, 10), floor);
        cpu = Math.Max(cpu, cpuFan.Minimum);
        gpu = Math.Max(gpu, gpuFan.Minimum);
        if (cpu > cpuFan.Maximum || gpu > gpuFan.Maximum) return false;
        cpuSpeed = (byte)cpu;
        gpuSpeed = (byte)gpu;
        return true;
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
        var services = OemServiceManager.HardwareServices.ToDictionary(n => n, n => OemServiceManager.ReadServiceEvidence(n, refreshFingerprint: true));
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
        bool predatorCatalogModel = identity.IsAcer && DynamicDeviceProfileCatalog.TryGetModel(identity.NormalizedModel) is { MachineType: 0 };
        var predatorModes = predatorCatalogModel && methods.Contains("GetGamingMiscSetting") && methods.Contains("SetGamingMiscSetting")
            ? ParsePredatorOperationModeCapabilities(ReadGamingMiscSetting(MiscSubfunctionOperationModeCap)) : Array.Empty<PowerMode>();
        byte? predatorDirectMode = predatorCatalogModel && methods.Contains("GetGamingMiscSetting") && methods.Contains("SetGamingMiscSetting")
            ? ParsePredatorOperationMode(ReadGamingMiscSetting(NitroSense3Protocol.MiscSubfunctionOperationMode)) ?? (predatorModes.Count > 0 ? (byte)1 : null) : null;
        if (predatorModes.Count > 0 && predatorDirectMode.HasValue && !predatorModes.Any(m => m.Value == predatorDirectMode.Value))
            predatorModes = Array.Empty<PowerMode>();
        services["PSSvc"] = services["PSSvc"] with { InterfaceWorking = coolBoost.HasValue || pipeOpMode.HasValue };
        if (coolBoost.HasValue) _coolBoost = coolBoost.Value;
        if (directCoolBoost.HasValue) _coolBoost = directCoolBoost.Value;
        int mux = 0;
        if (identity.IsAcer && services["AASSvc"] is { Running: true, Trusted: true })
        {
            try { mux = AcerAgentClient.GetGpuModeCapabilityAsync().GetAwaiter().GetResult(); } catch { }
            services["AASSvc"] = services["AASSvc"] with { InterfaceWorking = mux > 0 };
        }
        var protocols = new List<ProtocolEvidence>();
        OemKeyboardInfo? detectedKeyboard = null;
        if (identity.IsAcer && VerifiedAcerProtocol.ReviewedService(services["AASSvc"]))
        {
            detectedKeyboard = VerifiedAcerProtocol.ParseKeyboard(AcerAgentClient.QueryCapabilityAsync("SUPPORT_KEYBOARD_INFO").GetAwaiter().GetResult());
            var modes = VerifiedAcerProtocol.ParseModes(AcerAgentClient.QueryCapabilityAsync("SUPPORT_OPERATING_MODE_CAPABILITY").GetAwaiter().GetResult());
            byte? current = VerifiedAcerProtocol.ParseCurrentMode(AcerAgentClient.GetDeviceDataAsync("OPERATING_MODE").GetAwaiter().GetResult());
            if (!current.HasValue || !modes.Any(m => m.Value == current)) modes = [];
            var fans = VerifiedAcerProtocol.ParseFans(AcerAgentClient.QueryCapabilityAsync("SUPPORT_FAN_CONTROL").GetAwaiter().GetResult());
            if (!VerifiedAcerProtocol.ParseFanState(AcerAgentClient.GetDeviceDataAsync("FAN_CONTROL").GetAwaiter().GetResult(), fans).HasValue) fans = [];
            bool? boostReady = VerifiedAcerProtocol.ParseStatus(AcerAgentClient.GetDeviceDataAsync("BATTERY_BOOST").GetAwaiter().GetResult(), "BATTERY_BOOST");
            bool? adaptorReady = VerifiedAcerProtocol.ParseStatus(AcerAgentClient.GetDeviceDataAsync("ADAPTOR_STATUS").GetAwaiter().GetResult(), "ADAPTOR_STATUS") ?? IsOnAc;
            if (modes.Count > 0 || fans.Count > 0)
            {
                services["AASSvc"] = services["AASSvc"] with { InterfaceWorking = true };
                protocols.Add(new(VerifiedAcerProtocol.Family, "AASSvc", services["AASSvc"].ProtocolBinarySha256!, modes, current, fans, boostReady, adaptorReady));
            }
        }
        var warnings = new List<string>();
        if (_wmiDiscoveryFailure != null) warnings.Add(_wmiDiscoveryFailure);
        if (identity.IsAcer && services["AASSvc"].Exists)
        {
            if (!services["AASSvc"].Trusted) warnings.Add("Installed OEM service failed publisher or protected-path verification. It cannot authorize hardware controls.");
            else if (!VerifiedAcerProtocol.ReviewedService(services["AASSvc"])) warnings.Add("Installed OEM service fingerprint is outside the reviewed protocol catalog. Only independent verified model bindings remain available.");
            else if (protocols.Count == 0) warnings.Add("Reviewed OEM service is installed, but valid capability readbacks are unavailable. Authentication, encryption or service recovery may be required; no protocol-family writes are authorized.");
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
            NitroOperationMode = pipeOpMode ?? directOpMode ?? (nitroOpModeModel ? NitroSense3Protocol.ReadRegistryOperationMode() : null),
            NitroOperationModeReadable = pipeOpMode.HasValue,
            NitroDirectOperationModeReadable = directOpMode.HasValue,
            NitroBatteryBoostReady = batteryBoost,
            PredatorOperationMode = predatorDirectMode,
            PredatorDirectOperationModeReadable = predatorDirectMode.HasValue,
            PredatorOperationModes = predatorModes,
            Services = services, GpuModeMask = mux, Protocols = protocols, DetectedKeyboard = detectedKeyboard,
            Warnings = warnings.AsReadOnly()
        };
        return DeviceProfileResolver.Resolve(_discovery);
    }

    internal const uint MiscSubfunctionOperationModeCap = 0x0A;

    internal static byte? ParsePredatorOperationMode(ulong? reply) =>
        reply.HasValue && (reply.Value & 0xFF) == 0 && (byte)((reply.Value >> 8) & 0xFF) is byte m && m is 0 or 1 or 4 or 5 or 6 ? m : null;

    internal static IReadOnlyList<PowerMode> ParsePredatorOperationModeCapabilities(ulong? reply)
    {
        if (!reply.HasValue || (reply.Value & 0xFF) != 0) return Array.Empty<PowerMode>();
        byte mask = (byte)((reply.Value >> 8) & 0xFF);
        var modes = new List<PowerMode>();
        foreach (var mode in DynamicDeviceProfileCatalog.PredatorStandardModes)
            if ((mask & (1 << mode.Value)) != 0) modes.Add(mode);
        return modes.Count > 0 ? Array.AsReadOnly(modes.ToArray()) : Array.Empty<PowerMode>();
    }

    internal ulong? ReadGamingProfileRaw(ulong command = 0UL)
    {
        lock (_lock)
        {
            try
            {
                var obj = GetWmiObject();
                if (obj == null) return null;
                using var inParams = obj.GetMethodParameters("GetGamingProfile");
                inParams["gmInput"] = command;
                using var outParams = obj.InvokeMethod("GetGamingProfile", inParams, new InvokeMethodOptions { Timeout = TimeSpan.FromSeconds(2) });
                if (outParams?["gmOutput"] == null) return null;
                return Convert.ToUInt64(outParams["gmOutput"]);
            }
            catch { InvalidateCache(); return null; }
        }
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
            "SetGamingProfile" => (
                ((input <= 255 || ((input >> 24) == 1 && (input & 0xFFFFFF) <= 255))
                    && Capabilities.AllowsPower((byte)(input & 0xFF), IsOnAc)
                    && HardwareWriteGate.InScope(HardwareFeature.Power, "acer-wmi-profile")) ||
                ((input == 0x10UL || input == ((1UL << 48) | 0x10UL))
                    && HardwareWriteGate.InScope(HardwareFeature.LcdOverdrive, "acer-wmi-lcd"))
            ),
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
                0x06 or 0x106 => HardwareWriteGate.InScope(HardwareFeature.BootSound, "acer-wmi-misc"),
                0x0C or 0x0D => HardwareWriteGate.InScope(HardwareFeature.UsbCharging, "acer-wmi-misc"),
                _ when (input & 0xFFUL) == 0x02 && ((input >> 8) & 0xFFUL) is >= 1 and <= 3 =>
                    HardwareWriteGate.InScope(HardwareFeature.GpuMux, "acer-agent"),
                _ when (input & 0xFFUL) == NitroSense3Protocol.MiscSubfunctionOperationMode && (input >> 16) == 0 =>
                    Capabilities.AllowsPower((byte)((input >> 8) & 0xFF), IsOnAc) &&
                    ((NitroSense3Protocol.IsSupportedOperationMode((byte)((input >> 8) & 0xFF)) &&
                      HardwareWriteGate.InScope(HardwareFeature.Power, NitroSense3Protocol.OperationModeWmiBackend)) ||
                     (((input >> 8) & 0xFFUL) is 0 or 1 or 4 or 5 or 6 &&
                      HardwareWriteGate.InScope(HardwareFeature.Power, DeviceProfileResolver.PredatorWmiOperationModeBackend))),
                _ => false
            },
            _ => false
        };
    }
}
