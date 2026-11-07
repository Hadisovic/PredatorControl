namespace PredatorControlApp;

public sealed record CoolingCapabilities(int FanCount, bool AutoSupported, bool MaxSupported,
    bool ManualSupported, bool FanCurveSupported, bool CoolBoostSupported);
public sealed record PerformanceCapabilities(IReadOnlyList<PowerMode> SupportedModes, byte? CurrentMode);
// Refresh choices come from Windows display enumeration, never firmware guesses.
public sealed record DisplayCapabilities(bool MuxSupported, bool OverdriveSupported);
public sealed record BatteryCapabilities(bool ChargeLimitSupported, bool? ChargeLimitEnabled);

// Observations are input to a compiled, reviewed protocol selector. They are not write grants.
public sealed record ProtocolEvidence(string Family, string Service, string BinarySha256,
    IReadOnlyList<PowerMode> PowerModes, byte? CurrentPowerMode, IReadOnlyList<OemFanChannel>? Fans = null,
    bool? BatteryBoostReady = null, bool? AdaptorReady = null);
public sealed record OemFanChannel(int Index, string Name, int Minimum, int Maximum);
public sealed record OemKeyboardInfo(string LightingType, int ZoneCount, bool PerKey);
