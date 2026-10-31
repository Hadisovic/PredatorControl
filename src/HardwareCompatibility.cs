using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PredatorControlApp;

public enum AcerChassisFamily { Predator, Nitro, GenericAcer, Unsupported }
public enum DeviceFamily { Unsupported, UnknownAcer, PredatorHelios, PredatorHeliosNeo, PredatorTriton, Nitro5, NitroV, Nitro }
public enum CapabilityStatus { Supported, Unsupported, Unknown, Unavailable }
public enum HardwareFeature { Power, FanAuto, FanMax, FanManual, FanCurve, AuxFan, CoolBoost, KeyboardPower, KeyboardTimeout, Rgb, BatteryLimit, GpuMux, LcdOverdrive, BootSound, UsbCharging, WinKey, PerformanceKey }

[Flags]
public enum AcerFeature : uint
{
    None = 0, CoolBoost = 1, RgbLighting = 2, LcdOverdrive = 4, BootSound = 8,
    UsbCharging = 16, BatteryLimit = 32, AuxFan = 64, GpuMux = 128
}

// Serial is local-only. Neither default JSON serialization nor ToString exports it.
public sealed record DeviceIdentity
{
    public string Manufacturer { get; init; } = "";
    public string Model { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string ProductVersion { get; init; } = "";
    public string ProductVendor { get; init; } = "";
    public string SystemFamily { get; init; } = "";
    public string Sku { get; init; } = "";
    [JsonIgnore] public string SerialNumber { get; init; } = "";
    public string BiosVersion { get; init; } = "";
    public string BiosReleaseDate { get; init; } = "";
    public string BoardManufacturer { get; init; } = "";
    public string BoardProduct { get; init; } = "";
    public string BoardVersion { get; init; } = "";
    public IReadOnlyList<ushort> ChassisTypes { get; init; } = Array.Empty<ushort>();
    public bool IsAcer => AcerName(Manufacturer) && (string.IsNullOrWhiteSpace(ProductVendor) || AcerName(ProductVendor));
    private static bool AcerName(string? value) => Regex.IsMatch(Clean(value), @"^Acer(?: Incorporated| Inc\.?)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public string NormalizedModel
    {
        get
        {
            var models = Regex.Matches(Clean(Model) + " " + Clean(ProductName), @"\b(?:PHN|PH|PT|ANV|AN)\d{2,3}-\d{2}(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                .Select(m => m.Value.ToUpperInvariant()).Distinct().ToArray();
            return models.Length == 1 ? models[0] : ""; // Contradictory fields cannot select firmware.
        }
    }
    public DeviceFamily Family => !IsAcer ? DeviceFamily.Unsupported : NormalizedModel switch
    {
        var m when m.StartsWith("PHN", StringComparison.Ordinal) => DeviceFamily.PredatorHeliosNeo,
        var m when m.StartsWith("PH", StringComparison.Ordinal) => DeviceFamily.PredatorHelios,
        var m when m.StartsWith("PT", StringComparison.Ordinal) => DeviceFamily.PredatorTriton,
        var m when m.StartsWith("ANV", StringComparison.Ordinal) => DeviceFamily.NitroV,
        var m when m.StartsWith("AN5", StringComparison.Ordinal) => DeviceFamily.Nitro5,
        var m when m.StartsWith("AN", StringComparison.Ordinal) => DeviceFamily.Nitro,
        _ => NamedFamily()
    };
    private DeviceFamily NamedFamily()
    {
        string name = Model + " " + ProductName;
        if (Regex.Matches(name, @"\b(?:PHN|PH|PT|ANV|AN)\d{2,3}-\d{2}(?!\d)", RegexOptions.IgnoreCase).Select(m => m.Value.ToUpperInvariant()).Distinct().Count() > 1)
            return DeviceFamily.UnknownAcer;
        // Branding-only classification; exact model is still required for a firmware profile.
        if (Regex.IsMatch(name, @"\bPredator Helios Neo\b", RegexOptions.IgnoreCase)) return DeviceFamily.PredatorHeliosNeo;
        if (Regex.IsMatch(name, @"\bPredator Helios\b", RegexOptions.IgnoreCase)) return DeviceFamily.PredatorHelios;
        if (Regex.IsMatch(name, @"\bPredator Triton\b", RegexOptions.IgnoreCase)) return DeviceFamily.PredatorTriton;
        if (Regex.IsMatch(name, @"\bNitro V\b", RegexOptions.IgnoreCase)) return DeviceFamily.NitroV;
        if (Regex.IsMatch(name, @"\bNitro 5\b", RegexOptions.IgnoreCase)) return DeviceFamily.Nitro5;
        if (Regex.IsMatch(name, @"\bNitro (?:16|17)\b", RegexOptions.IgnoreCase)) return DeviceFamily.Nitro;
        return DeviceFamily.UnknownAcer;
    }
    public AcerChassisFamily ChassisFamily => Family switch
    {
        DeviceFamily.PredatorHelios or DeviceFamily.PredatorHeliosNeo or DeviceFamily.PredatorTriton => AcerChassisFamily.Predator,
        DeviceFamily.Nitro or DeviceFamily.Nitro5 or DeviceFamily.NitroV => AcerChassisFamily.Nitro,
        DeviceFamily.UnknownAcer => AcerChassisFamily.GenericAcer,
        _ => AcerChassisFamily.Unsupported
    };
    public bool IsAcerGaming => ChassisFamily is AcerChassisFamily.Predator or AcerChassisFamily.Nitro;
    [JsonIgnore] public string CompatibilityKey => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join("|", Manufacturer, NormalizedModel, Sku, ProductVersion, BoardProduct, BoardVersion, BiosVersion))));
    [JsonIgnore] public string LocalPreferenceKey => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CompatibilityKey + "|" + SerialNumber)));
    public override string ToString() => $"{Manufacturer} {Model} BIOS {BiosVersion}";
    public static string Clean(object? value)
    {
        string s = Regex.Replace(value?.ToString() ?? "", @"\s+", " ").Trim();
        return s.Any(char.IsControl) || s.Length > 256 || s.Equals("To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("Default string", StringComparison.OrdinalIgnoreCase) || s.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ? "" : s;
    }
}

public static class HardwarePreferencePolicy
{
    public static bool IsHardwareSetting(string name) => name is "Power" or "Fan" or "BatteryLimit" or "CoolBoostEnabled" or
        "BootSound" or "UsbCharging" or "UsbChargingCutoff" or "LcdOverdrive" or "RGB_Mode" or "RGB_R" or "RGB_G" or "RGB_B" or
        "Brightness" or "RGB_Speed" or "FanSpeedCpu" or "FanSpeedGpu" or "FanSpeedAux" or "FanCurveEnabled" or "CpuCurve" or "GpuCurve" or
        "KeyboardLightingConfigured" or "KeyboardPowerEnabled" or "KeyboardIdleTimeout" or "AutoPowerACValue" or "AutoPowerBatteryValue" ||
        name.StartsWith("ZoneColor_", StringComparison.Ordinal);
}

public static class FanCurveSafety
{
    public static bool MustRecover(int? cpuTemp, int? gpuTemp, long sampleAgeMs) =>
        !cpuTemp.HasValue || !gpuTemp.HasValue || sampleAgeMs > 5000 || sampleAgeMs < 0;
}

public sealed record ServiceEvidence(string Name, bool Exists, bool Running, bool Trusted, bool InterfaceWorking = false);
public sealed record CapabilityEvidence(CapabilityStatus Status, string Backend, string Evidence,
    string? RequiredService = null, bool Writable = false, bool CanVerify = false)
{
    public bool CanWrite => Status == CapabilityStatus.Supported && Writable;
}
public sealed record PowerMode(byte Value, string Label, bool OnAc = true, bool OnBattery = true, Guid? WindowsScheme = null);
public sealed record WindowsPowerPlanDiscovery(IReadOnlyList<PowerMode> Modes, Guid? ActiveScheme, string Evidence);
public sealed record TelemetryChannelEvidence(CapabilityStatus Status, string Source, string Evidence);
public sealed record FeatureBinding(HardwareFeature Feature, string Backend, string? Method = null,
    string? Service = null, bool CanVerify = false);

// Reviewed, compiled-in profiles only: never load command definitions from preferences, serials or the network.
public sealed record DeviceProfile(string Id, string Model, string Evidence,
    IReadOnlyList<FeatureBinding> Bindings, IReadOnlyList<PowerMode> PowerModes,
    KeyboardCapabilities Keyboard, IReadOnlyDictionary<string, ulong> Sensors,
    string? Sku = null, string? Board = null, string? Bios = null, IReadOnlySet<HardwareFeature>? UnsupportedFeatures = null)
{
    public bool Matches(DeviceIdentity d) => d.IsAcer && d.NormalizedModel == Model &&
        (Sku == null || Sku.Equals(d.Sku, StringComparison.OrdinalIgnoreCase)) &&
        (Board == null || Board.Equals(d.BoardProduct, StringComparison.OrdinalIgnoreCase)) &&
        (Bios == null || Bios.Equals(d.BiosVersion, StringComparison.OrdinalIgnoreCase));
    public int Specificity => (Sku != null ? 1 : 0) + (Board != null ? 1 : 0) + (Bios != null ? 1 : 0);
}

public sealed record HardwareDiscovery
{
    public DeviceIdentity Identity { get; init; } = new();
    public IReadOnlySet<string> Methods { get; init; } = new HashSet<string>();
    public IReadOnlyDictionary<string, ServiceEvidence> Services { get; init; } = new Dictionary<string, ServiceEvidence>();
    public bool KeyboardConfigurationReadable { get; init; }
    public bool BatteryStatusReadable { get; init; }
    public bool? BatteryLimitEnabled { get; init; }
    public bool WindowsPowerOverlayReadable { get; init; }
    public WindowsPowerPlanDiscovery WindowsPowerPlans { get; init; } = new([], null, "Windows power plans have not been read.");
    public ushort? SupportedSensorMask { get; init; }
    public bool? NitroCoolBoostEnabled { get; init; }
    public bool NitroDirectCoolBoostReadable { get; init; }
    public byte? NitroOperationMode { get; init; }
    public bool NitroOperationModeReadable { get; init; }
    public bool NitroDirectOperationModeReadable { get; init; }
    public bool? NitroBatteryBoostReady { get; init; }
    public int GpuModeMask { get; init; }
    public string WmiClass { get; init; } = "";
}

public record HardwareCapabilities
{
    public DeviceIdentity Identity { get; init; } = new();
    public AcerChassisFamily ChassisFamily { get; init; } = AcerChassisFamily.Unsupported;
    public string ProfileId { get; init; } = "read-only";
    public string Explanation { get; init; } = "No verified profile or operational interface.";
    public AcerFeature Features { get; init; }
    public IReadOnlyDictionary<HardwareFeature, CapabilityEvidence> Results { get; init; } = new Dictionary<HardwareFeature, CapabilityEvidence>();
    public IReadOnlyList<PowerMode> PowerModes { get; init; } = Array.Empty<PowerMode>();
    public KeyboardCapabilities Keyboard { get; init; } = new(KeyboardKind.Unknown, "Keyboard configuration is unknown.");
    public IReadOnlyDictionary<string, ulong> Sensors { get; init; } = new Dictionary<string, ulong>();
    public IReadOnlyDictionary<string, ServiceEvidence> Services { get; init; } = new Dictionary<string, ServiceEvidence>();
    public IReadOnlyDictionary<string, TelemetryChannelEvidence> TelemetryChannels { get; init; } = new Dictionary<string, TelemetryChannelEvidence>();
    public int GpuModeMask { get; init; }
    public bool? BatteryLimitEnabled { get; init; }
    public byte? ActivePowerMode { get; init; }
    public bool? NitroBatteryBoostReady { get; init; }
    public Guid? ActiveWindowsPowerPlan { get; init; }
    public string? PowerPlanNotice { get; init; }
    public bool CanWrite(HardwareFeature f) => Results.TryGetValue(f, out var c) && c.CanWrite;
    public bool AllowsPower(byte value, bool ac) => CanWrite(HardwareFeature.Power) &&
        PowerModes.Any(m => m.Value == value && (ac && NitroBatteryBoostReady != false ? m.OnAc : m.OnBattery));
    public string? PowerModeLabel(byte? value) => value.HasValue ? PowerModes.FirstOrDefault(m => m.Value == value.Value)?.Label : null;
    public bool SupportsRgbLighting => CanWrite(HardwareFeature.Rgb);
    public bool SupportsCoolBoost => CanWrite(HardwareFeature.CoolBoost);
    public bool SupportsBatteryControl => CanWrite(HardwareFeature.BatteryLimit);
    public bool SupportsGpuMux => CanWrite(HardwareFeature.GpuMux);
    public bool SupportsLcdOverdrive => CanWrite(HardwareFeature.LcdOverdrive);
    public bool SupportsBootSound => CanWrite(HardwareFeature.BootSound);
    public bool SupportsUsbCharging => CanWrite(HardwareFeature.UsbCharging);
    public bool SupportsWinKey => CanWrite(HardwareFeature.WinKey);
    public bool HasAuxFan => Sensors.ContainsKey("auxFanRpm");
    public int PowerModeCount => PowerModes.Count;
    public string[] PowerModeLabels => PowerModes.Select(m => m.Label).ToArray();
    public byte[] PowerModeValues => PowerModes.Select(m => m.Value).ToArray();
    public static readonly HardwareCapabilities None = new();
}

public sealed record PowerControlProjection(string Id, byte Value, string Label, bool Active, bool Enabled);

public static class NitroPowerProfileCoordinator
{
    public const string OperationModePipeBackend = "nitrosense3-opmode";
    public const string OperationModeWmiBackend = "nitrosense3-wmi-opmode";
    public static readonly IReadOnlyList<PowerMode> OperationModes = Array.AsReadOnly(new PowerMode[]
    {
        new(0, "Quiet", OnAc: true, OnBattery: false),
        new(1, "Default", OnAc: true, OnBattery: true),
        new(4, "Performance", OnAc: true, OnBattery: false)
    });
    public static bool IsSupportedOperationMode(byte mode) => mode is 0 or 1 or 4;

    public static string ControlId(byte mode) => mode switch
    {
        0 => "power.quiet",
        1 => "power.balanced",
        2 => "power.acerBalanced",
        4 => "power.performance",
        5 => "power.turbo",
        6 => "power.eco",
        _ => throw new InvalidOperationException("Unsupported UI mode mapping.")
    };

    public static IReadOnlyList<PowerControlProjection> ProjectControls(HardwareCapabilities capabilities, bool isOnAc, byte? activeMode = null)
    {
        byte? resolvedActive = activeMode ?? capabilities.ActivePowerMode;
        return Array.AsReadOnly(capabilities.PowerModes.Select(m => new PowerControlProjection(
            ControlId(m.Value),
            m.Value,
            m.Label,
            resolvedActive == m.Value,
            capabilities.AllowsPower(m.Value, isOnAc))).ToArray());
    }
}

public static class DeviceProfileResolver
{
    private static readonly IReadOnlyDictionary<string, ulong> LegacySensors = new ReadOnlyDictionary<string, ulong>(new Dictionary<string, ulong>
    { ["cpuTemp"] = 1, ["gpuTemp"] = 10, ["cpuFanRpm"] = 2, ["gpuFanRpm"] = 6, ["gpuPowerW"] = 13 });
    private static readonly FeatureBinding[] LegacyPredator =
    [
        new(HardwareFeature.Power, "acer-wmi-profile", "SetGamingProfile"),
        new(HardwareFeature.FanAuto, "acer-wmi-fans", "SetGamingFanBehavior"),
        new(HardwareFeature.FanMax, "acer-wmi-fans", "SetGamingFanBehavior"),
        new(HardwareFeature.FanManual, "acer-wmi-fans", "SetGamingFanSpeed"),
        new(HardwareFeature.FanCurve, "acer-wmi-fans", "SetGamingFanSpeed"),
        new(HardwareFeature.Rgb, "acer-wmi-rgb", "SetGamingKBBacklight"),
        new(HardwareFeature.LcdOverdrive, "acer-wmi-misc", "SetGamingMiscSetting"),
        new(HardwareFeature.BootSound, "acer-wmi-misc", "SetGamingMiscSetting"),
        new(HardwareFeature.UsbCharging, "acer-wmi-misc", "SetGamingMiscSetting"),
        new(HardwareFeature.WinKey, "acer-wmi-winkey", "SetGamingRgbKb")
    ];
    private static readonly PowerMode[] LegacyModes =
    [new(0, "Quiet"), new(1, "Balanced"), new(4, "Performance", OnBattery: false), new(5, "Turbo", OnBattery: false), new(6, "Eco", OnAc: false)];
    private static readonly FeatureBinding[] Nitro2022OperationModeBindings =
    [
        new(HardwareFeature.Power, NitroPowerProfileCoordinator.OperationModePipeBackend, Service: "PSSvc", CanVerify: true)
    ];
    public static readonly IReadOnlyList<DeviceProfile> Profiles = Array.AsReadOnly(new[]
    {
        new DeviceProfile("legacy-ph16-71", "PH16-71", "Repository legacy Predator WMI implementation; physical validation still required for each configuration.",
            LegacyPredator, LegacyModes, new(KeyboardKind.Rgb, "Legacy Predator RGB profile", ZoneCount: 4, Effects: Enumerable.Range(0, 9).ToArray()), LegacySensors),
        new DeviceProfile("legacy-phn16-71", "PHN16-71", "Repository legacy Helios Neo WMI implementation; physical validation still required for each configuration.",
            LegacyPredator, LegacyModes, new(KeyboardKind.Rgb, "Legacy Helios Neo RGB profile", ZoneCount: 4, Effects: Enumerable.Range(0, 9).ToArray()), LegacySensors),
        new DeviceProfile("conservative-an515-58", "AN515-58", "NitroSense 3.01.3056 MachineType=2 (Jimny) operation-mode profile (Quiet=0, Default=1, Performance=4 via SetOperationMode/SetGamingMiscSetting 0x0B) and fixed-red keyboard policy; physical write validation pending.",
            Nitro2022OperationModeBindings, NitroPowerProfileCoordinator.OperationModes, new(KeyboardKind.Binary, "AN515-58 conservative fixed-red profile", "#ff394b"), new Dictionary<string, ulong>()),
        new DeviceProfile("nitro-an515-46-jimny", "AN515-46", "NitroSense 3.01.3056 MachineType=2 (Jimny) operation-mode profile (Quiet=0, Default=1, Performance=4 via SetOperationMode/SetGamingMiscSetting 0x0B); physical write validation pending.",
            Nitro2022OperationModeBindings, NitroPowerProfileCoordinator.OperationModes, new(KeyboardKind.Unknown, "Unverified keyboard configuration"), new Dictionary<string, ulong>()),
        new DeviceProfile("nitro-an515-47-jimny", "AN515-47", "NitroSense 3.01.3056 MachineType=2 (Jimny) operation-mode profile (Quiet=0, Default=1, Performance=4 via SetOperationMode/SetGamingMiscSetting 0x0B); physical write validation pending.",
            Nitro2022OperationModeBindings, NitroPowerProfileCoordinator.OperationModes, new(KeyboardKind.Unknown, "Unverified keyboard configuration"), new Dictionary<string, ulong>()),
        new DeviceProfile("nitro-an517-42-jimny", "AN517-42", "NitroSense 3.01.3056 MachineType=2 (Jimny) operation-mode profile (Quiet=0, Default=1, Performance=4 via SetOperationMode/SetGamingMiscSetting 0x0B); physical write validation pending.",
            Nitro2022OperationModeBindings, NitroPowerProfileCoordinator.OperationModes, new(KeyboardKind.Unknown, "Unverified keyboard configuration"), new Dictionary<string, ulong>()),
        new DeviceProfile("nitro-an517-43-jimny", "AN517-43", "NitroSense 3.01.3056 MachineType=2 (Jimny) operation-mode profile (Quiet=0, Default=1, Performance=4 via SetOperationMode/SetGamingMiscSetting 0x0B); physical write validation pending.",
            Nitro2022OperationModeBindings, NitroPowerProfileCoordinator.OperationModes, new(KeyboardKind.Unknown, "Unverified keyboard configuration"), new Dictionary<string, ulong>()),
        new DeviceProfile("nitro-an517-55-jimny", "AN517-55", "NitroSense 3.01.3056 MachineType=2 (Jimny) operation-mode profile (Quiet=0, Default=1, Performance=4 via SetOperationMode/SetGamingMiscSetting 0x0B); physical write validation pending.",
            Nitro2022OperationModeBindings, NitroPowerProfileCoordinator.OperationModes, new(KeyboardKind.Unknown, "Unverified keyboard configuration"), new Dictionary<string, ulong>()),
        new DeviceProfile("nitro-an515-54-octavia", "AN515-54",
            "AN515-54 Acer user manual documents Auto/Max/Custom fans. Octavia_CFS V1.33 read-only validation confirmed both temperatures, tachometers and Auto mode. Fan writes use the existing Gaming-WMI protocol; physical write validation pending.",
            [new(HardwareFeature.Power, "windows-power-plan", CanVerify: true),
             new(HardwareFeature.FanAuto, "acer-wmi-fans", "SetGamingFanBehavior"),
             new(HardwareFeature.FanMax, "acer-wmi-fans", "SetGamingFanBehavior"),
             new(HardwareFeature.CoolBoost, "nitrosense3-pipe", Service: "PSSvc", CanVerify: true),
             new(HardwareFeature.FanManual, "acer-wmi-fans", "SetGamingFanSpeed"),
             new(HardwareFeature.FanCurve, "acer-wmi-fans", "SetGamingFanSpeed")],
            [new(0, "Power saver"), new(1, "Balanced"), new(2, "Balanced (Acer Optimized)"), new(4, "High performance")],
            new(KeyboardKind.Binary, "AN515-54 fixed-red keyboard; software on/off requires a readable OEM configuration.", "#ff394b"),
            new ReadOnlyDictionary<string, ulong>(LegacySensors.Where(s => s.Key != "gpuPowerW").ToDictionary(s => s.Key, s => s.Value)),
            Board: "Octavia_CFS")
    });

    public static HardwareCapabilities Resolve(HardwareDiscovery d, IEnumerable<DeviceProfile>? profiles = null)
    {
        var matches = (profiles ?? Profiles).Where(p => p.Matches(d.Identity)).OrderByDescending(p => p.Specificity).ToArray();
        // Equally specific, conflicting profiles fail closed instead of using enumeration order.
        DeviceProfile? p = matches.Length > 1 && matches[0].Specificity == matches[1].Specificity ? null : matches.FirstOrDefault();
        var results = Enum.GetValues<HardwareFeature>().ToDictionary(f => f, f => new CapabilityEvidence(
            d.Identity.IsAcer ? CapabilityStatus.Unknown : CapabilityStatus.Unsupported, "none", "No verified implementation for this configuration."));
        void Bind(FeatureBinding b, string evidence)
        {
            if (b.Backend == "nitrosense3-pipe" && d.NitroDirectCoolBoostReadable)
                b = b with { Backend = "nitrosense3-apge", Service = null, Method = "APGeAction.SetFunction" };
            if (b.Backend == "nitrosense3-apge") evidence = "Local signed NitroSense 3.01.3056 client and native APGeAction interface evidence; exact AN515-54 board profile plus valid read-only CoolBoost state. Setter requires matching readback; physical writes untested.";
            if (b.Backend == NitroPowerProfileCoordinator.OperationModePipeBackend && d.NitroDirectOperationModeReadable && d.Methods.Contains("GetGamingMiscSetting"))
                b = b with { Backend = NitroPowerProfileCoordinator.OperationModeWmiBackend, Service = null, Method = "SetGamingMiscSetting" };
            if (b.Backend == NitroPowerProfileCoordinator.OperationModeWmiBackend)
                evidence = "NitroSense 3.01.3056 MachineType=2 operation-mode protocol (SetGamingMiscSetting/GetGamingMiscSetting subfunction 0x0B); setter requires matching readback; physical writes untested.";
            bool method = b.Method == null || d.Methods.Contains(b.Method);
            if (b.Backend == "windows-power-overlay") method &= d.WindowsPowerOverlayReadable;
            if (b.Backend == "windows-power-plan")
            {
                method &= d.WindowsPowerPlans.ActiveScheme.HasValue && d.WindowsPowerPlans.Modes.Any(m => m.WindowsScheme.HasValue && m.WindowsScheme != Guid.Empty && p!.PowerModes.Any(allowed => allowed.Value == m.Value));
                evidence = d.WindowsPowerPlans.Evidence;
            }
            if (b.Backend == NitroPowerProfileCoordinator.OperationModePipeBackend)
            {
                method &= d.NitroOperationModeReadable;
                evidence = "NitroSense 3.01.3056 PSSvc SetOperationMode (command 30) and GetGamingMiscSetting (command 64, subfunction 0x0B); setter requires matching readback; physical writes untested.";
            }
            if (b.Backend == NitroPowerProfileCoordinator.OperationModeWmiBackend)
                method &= d.NitroDirectOperationModeReadable && d.Methods.Contains("SetGamingMiscSetting") && d.Methods.Contains("GetGamingMiscSetting");
            bool service = b.Service == null || (d.Services.TryGetValue(b.Service, out var s) && s.Exists && s.Running && s.Trusted && s.InterfaceWorking);
            results[b.Feature] = new(method && service ? CapabilityStatus.Supported : CapabilityStatus.Unavailable,
                b.Backend, method && service || b.Backend == "windows-power-plan" ? evidence : "Required method or trusted, operational OEM service is unavailable.", b.Service, true, b.CanVerify);
        }
        if (d.Identity.IsAcer)
        {
            if (p != null) foreach (var b in p.Bindings) Bind(b, p.Evidence);
            if (p?.Keyboard.SupportsPower == true && d.KeyboardConfigurationReadable)
            {
                Bind(new(HardwareFeature.KeyboardPower, "apge-keyboard", CanVerify: true), "Profile plus readable, validated APGe configuration.");
                Bind(new(HardwareFeature.KeyboardTimeout, "apge-keyboard", CanVerify: true), "Independent idle-timeout configuration readback.");
            }
            else if (p?.Keyboard.SupportsPower == true)
            {
                results[HardwareFeature.KeyboardPower] = new(CapabilityStatus.Unavailable, "apge-keyboard", "Known keyboard profile; readable APGe configuration is unavailable.");
                results[HardwareFeature.KeyboardTimeout] = new(CapabilityStatus.Unavailable, "apge-keyboard", "Idle timeout requires readable APGe configuration.");
            }
            if (d.BatteryStatusReadable && d.Methods.Contains("BatteryControl.SetBatteryHealthControl"))
                Bind(new(HardwareFeature.BatteryLimit, "battery-control", CanVerify: true), "BatteryControl status reports health-control support and a valid state.");
            else if (d.Methods.Contains("BatteryControl.SetBatteryHealthControl"))
                results[HardwareFeature.BatteryLimit] = new(CapabilityStatus.Unknown, "battery-control", "BatteryControl exists, but its health-status query did not confirm support. No battery command is authorized.");
            if (d.GpuModeMask is > 0 and <= 7)
                Bind(new(HardwareFeature.GpuMux, "acer-agent", Service: "AASSvc", CanVerify: true), "Explicit Acer Agent supported GPU mode mask.");
        }
        if (p?.Keyboard.Kind is KeyboardKind.Binary or KeyboardKind.None)
            results[HardwareFeature.Rgb] = new(CapabilityStatus.Unsupported, "none", "Selected keyboard profile does not implement RGB.");
        if (p?.UnsupportedFeatures != null)
            foreach (var feature in p.UnsupportedFeatures)
                results[feature] = new(results[feature].CanWrite ? CapabilityStatus.Unknown : CapabilityStatus.Unsupported,
                    "none", results[feature].CanWrite ? "Profile and operational interface evidence conflict. No writes are authorized." : "Explicitly excluded by the verified profile.");
        if (results[HardwareFeature.FanManual].CanWrite && !results[HardwareFeature.FanAuto].CanWrite)
            results[HardwareFeature.FanManual] = new(CapabilityStatus.Unavailable, "none", "Manual fans require a supported firmware Auto recovery path.");
        if (results[HardwareFeature.FanCurve].CanWrite && (!results[HardwareFeature.FanAuto].CanWrite || !results[HardwareFeature.FanManual].CanWrite))
            results[HardwareFeature.FanCurve] = new(CapabilityStatus.Unknown, "none", "Safe repeated writes and Auto recovery are required.");
        var keyboard = p?.Keyboard ?? new(KeyboardKind.Unknown, "No verified keyboard profile. Preferences cannot authorize firmware commands.");
        if (keyboard.SupportsRgb && (keyboard.ZoneCount is < 1 or > 4 || keyboard.Effects is not { Count: > 0 } || keyboard.Effects.Any(e => e is < 0 or > 8) || !results[HardwareFeature.Rgb].CanWrite || !d.Methods.Contains("SetGamingRgbKb")))
        {
            results[HardwareFeature.Rgb] = new(CapabilityStatus.Unavailable, "acer-wmi-rgb", "Both lighting and zone interfaces are required by this RGB implementation.");
            keyboard = new(KeyboardKind.Unknown, results[HardwareFeature.Rgb].Evidence);
        }
        if (keyboard.Kind == KeyboardKind.Binary && !results[HardwareFeature.KeyboardPower].CanWrite)
            keyboard = new(KeyboardKind.Unknown, "Known fixed-color policy; APGe configuration is currently unavailable.");
        keyboard = keyboard with { Effects = keyboard.Effects == null ? null : Array.AsReadOnly(keyboard.Effects.ToArray()), IdleTimeoutSupported = results[HardwareFeature.KeyboardTimeout].CanWrite };
        AcerFeature flags = AcerFeature.None;
        foreach (var pair in new[] { (HardwareFeature.Rgb, AcerFeature.RgbLighting), (HardwareFeature.CoolBoost, AcerFeature.CoolBoost),
            (HardwareFeature.BatteryLimit, AcerFeature.BatteryLimit), (HardwareFeature.GpuMux, AcerFeature.GpuMux),
            (HardwareFeature.LcdOverdrive, AcerFeature.LcdOverdrive), (HardwareFeature.BootSound, AcerFeature.BootSound), (HardwareFeature.UsbCharging, AcerFeature.UsbCharging) })
            if (results[pair.Item1].CanWrite) flags |= pair.Item2;
        var sensors = (p?.Sensors ?? new Dictionary<string, ulong>()).ToDictionary(s => s.Key, s => s.Value);
        // Firmware-advertised, documented read-only sensor IDs do not require a writable model profile.
        if (d.Identity.IsAcer && d.SupportedSensorMask is ushort mask)
            foreach (var sensor in LegacySensors.Where(s => s.Key != "gpuPowerW"))
                if ((mask & (1 << ((int)sensor.Value - 1))) != 0) sensors.TryAdd(sensor.Key, sensor.Value);
        var powerBackend = results[HardwareFeature.Power].Backend;
        var resolvedModes = results[HardwareFeature.Power].CanWrite
            ? powerBackend == "windows-power-plan"
                ? d.WindowsPowerPlans.Modes.Where(m => m.WindowsScheme.HasValue && m.WindowsScheme != Guid.Empty && p!.PowerModes.Any(allowed => allowed.Value == m.Value)).ToArray()
                : p!.PowerModes.ToArray()
            : Array.Empty<PowerMode>();
        byte? activePowerMode = results[HardwareFeature.Power].CanWrite
            ? powerBackend == "windows-power-plan"
                ? resolvedModes.FirstOrDefault(m => m.WindowsScheme == d.WindowsPowerPlans.ActiveScheme)?.Value
                : d.NitroOperationMode is byte opMode && resolvedModes.Any(m => m.Value == opMode) ? opMode : null
            : null;
        string? powerNotice = powerBackend switch
        {
            "windows-power-plan" when d.WindowsPowerPlans.Modes.Count <= 1 || !d.WindowsPowerPlans.ActiveScheme.HasValue =>
                d.WindowsPowerPlans.Evidence,
            NitroPowerProfileCoordinator.OperationModePipeBackend or NitroPowerProfileCoordinator.OperationModeWmiBackend when d.NitroBatteryBoostReady == false =>
                "Battery is below 40% (BatteryBoost inactive); only Default mode is available until battery charges above 40% on AC.",
            _ => null
        };
        return new()
        {
            Identity = d.Identity, ChassisFamily = d.Identity.ChassisFamily, ProfileId = p?.Id ?? "read-only",
            Explanation = p?.Evidence ?? "No unambiguous verified profile. Read-only fallback.", Features = flags,
            Results = new ReadOnlyDictionary<HardwareFeature, CapabilityEvidence>(results),
            PowerModes = Array.AsReadOnly(resolvedModes),
            ActivePowerMode = activePowerMode,
            NitroBatteryBoostReady = d.NitroBatteryBoostReady,
            ActiveWindowsPowerPlan = powerBackend == "windows-power-plan" ? d.WindowsPowerPlans.ActiveScheme : null,
            PowerPlanNotice = powerNotice,
            Keyboard = keyboard, Sensors = new ReadOnlyDictionary<string, ulong>(d.Methods.Contains("GetGamingSysInfo") ? sensors : new Dictionary<string, ulong>()),
            Services = new ReadOnlyDictionary<string, ServiceEvidence>(d.Services.ToDictionary(x => x.Key, x => x.Value)),
            GpuModeMask = results[HardwareFeature.GpuMux].CanWrite ? d.GpuModeMask : 0,
            BatteryLimitEnabled = d.BatteryStatusReadable ? d.BatteryLimitEnabled : null
        };
    }
}

public sealed record HardwareOperationResult(string Status, string Message)
{
    public bool Confirmed => Status == "confirmed";
    public bool Accepted => Status is "confirmed" or "unverified";
}

// One gate for all firmware transports. Tests inject outcomes here; no Windows APIs are needed.
public sealed class HardwareWriteGate(Func<HardwareCapabilities> snapshot, Func<string, bool>? serviceAvailable = null)
{
    internal static readonly object CommandLock = new();
    private static readonly AsyncLocal<(HardwareFeature Feature, string Backend)?> Active = new();
    public static bool InScope(HardwareFeature feature, string backend) => Active.Value == (feature, backend);
    public HardwareOperationResult? LastOperation { get; private set; }
    internal void RecordResult(HardwareOperationResult result) { lock (CommandLock) LastOperation = result; }
    public HardwareOperationResult Execute(HardwareFeature feature, bool parametersValid, Func<HardwareOperationResult> write, CancellationToken token = default)
    {
        lock (CommandLock)
        {
            HardwareCapabilities caps;
            try { caps = snapshot(); }
            catch { return LastOperation = new("failed", "Capability discovery failed. No command was sent."); }
            if (token.IsCancellationRequested) return LastOperation = new("failed", "Operation cancelled before dispatch.");
            if (!parametersValid || !caps.CanWrite(feature)) return LastOperation = new("failed", "Operation is not authorized for this device and parameters.");
            var evidence = caps.Results[feature];
            try
            {
                if (evidence.RequiredService is string service && serviceAvailable?.Invoke(service) != true)
                    return LastOperation = new("failed", "Required OEM service is unavailable. Refresh compatibility after repair.");
            }
            catch { return LastOperation = new("failed", "OEM service discovery failed. No command was sent."); }
            var previous = Active.Value;
            try
            {
                Active.Value = (feature, evidence.Backend);
                return LastOperation = write();
            }
            catch { return LastOperation = new("failed", "Hardware interface failed; state was not confirmed."); }
            finally { Active.Value = previous; }
        }
    }
}

public static class CompatibilityDiagnostics
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { WriteIndented = true, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
    public static string Export(HardwareCapabilities caps, string version, object? interfaces, HardwareOperationResult? last, int? revision = null) =>
        JsonSerializer.Serialize(new { version, revision, timestampUtc = DateTime.UtcNow, device = caps.Identity, capabilities = caps, interfaces, lastOperation = last }, Json);
    public static int? Sensor(string channel, int? value) => value.HasValue && (channel.EndsWith("Temp", StringComparison.Ordinal)
        ? value is >= 1 and <= 125 : channel.EndsWith("Rpm", StringComparison.Ordinal) ? value is >= 0 and <= 15000 : value is >= 0 and <= 1000) ? value : null;
}
