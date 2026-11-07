namespace PredatorControlApp;

public enum KeyboardKind { Unknown, Binary, Rgb, None }

public sealed record KeyboardCapabilities(KeyboardKind Kind, string Evidence, string? FixedColor = null,
    int ZoneCount = 0, IReadOnlyList<int>? Effects = null, bool IdleTimeoutSupported = false,
    bool BrightnessSupported = true, bool EffectSpeedSupported = true, bool PowerToggleSupported = true)
{
    public bool SupportsPower => PowerToggleSupported && (Kind is KeyboardKind.Binary or KeyboardKind.Rgb);
    public bool SupportsBrightness => Kind == KeyboardKind.Rgb && BrightnessSupported;
    public bool SupportsEffectSpeed => Kind == KeyboardKind.Rgb && EffectSpeedSupported;
    public bool SupportsRgb => Kind == KeyboardKind.Rgb;
    public bool SupportsIdleTimeout => IdleTimeoutSupported;

    public static bool IsNitroModel(string model) =>
        model.Contains("Nitro", StringComparison.OrdinalIgnoreCase) ||
        System.Text.RegularExpressions.Regex.IsMatch(model.Trim(), @"^(?:ANV|AN)\d{2,3}(?:-|$)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public static KeyboardCapabilities Detect(string manufacturer, string model, bool rgbInterface, string? profile)
    {
        if (!manufacturer.Equals("Acer", StringComparison.OrdinalIgnoreCase) &&
            !manufacturer.StartsWith("Acer ", StringComparison.OrdinalIgnoreCase))
            return new(KeyboardKind.Unknown, "Keyboard hardware has not been identified.");
        // Compatibility is resolved centrally. A user preference or method name cannot prove support.
        return new(KeyboardKind.Unknown, "Use the resolved device profile and operational interface evidence.");
    }
}

// Classic and Jelli render this same projection. UI preferences never add capabilities.
public sealed record KeyboardControlProjection(bool Rgb, int ZoneCount, IReadOnlyList<int> Effects,
    bool Power, bool Brightness, bool EffectSpeed, bool IdleTimeout, string? FixedColor, string Explanation)
{
    public static KeyboardControlProjection From(HardwareCapabilities capabilities)
    {
        var keyboard = capabilities.Keyboard;
        bool rgb = capabilities.CanWrite(HardwareFeature.Rgb) && keyboard.SupportsRgb &&
            keyboard.ZoneCount is >= 1 and <= 4 && keyboard.Effects is { Count: > 0 } &&
            keyboard.Effects.All(effect => effect is >= 0 and <= 8);
        return new(rgb, rgb ? keyboard.ZoneCount : 0,
            rgb ? Array.AsReadOnly(keyboard.Effects!.Distinct().ToArray()) : Array.Empty<int>(),
            keyboard.SupportsPower && capabilities.CanWrite(HardwareFeature.KeyboardPower),
            rgb && keyboard.SupportsBrightness, rgb && keyboard.SupportsEffectSpeed,
            keyboard.SupportsIdleTimeout && capabilities.CanWrite(HardwareFeature.KeyboardTimeout),
            keyboard.FixedColor, keyboard.Evidence);
    }

    public bool AllowsEffect(int effect) => Rgb && Effects.Contains(effect);
    public bool AllowsZone(int zone) => Rgb && zone >= 0 && zone < ZoneCount;
}

public sealed record KeyboardState(bool? Enabled, bool? IdleTimeout, string Status);
public sealed record KeyboardOperationResult(string Status, string Message)
{
    public bool Confirmed => Status == "confirmed";
}

internal interface IKeyboardFirmware
{
    bool TryRead(out uint configuration);
    bool TryWrite(uint configuration);
}

internal sealed record KeyboardPreferences(bool? Enabled, bool? IdleTimeout)
{
    public static KeyboardPreferences Read(Func<string, object?> read)
    {
        static bool? Explicit(object? value) => value is int number && number is 0 or 1 ? number == 1 : null;
        return new(Explicit(read("KeyboardPowerEnabled")), Explicit(read("KeyboardIdleTimeout")));
    }
}

// No legacy RGB/brightness preference is interpreted as an explicit binary power setting.
internal sealed class KeyboardController(KeyboardCapabilities capabilities, IKeyboardFirmware firmware, Action<KeyboardOperationResult>? report = null)
{
    private readonly object _gate = new();
    public KeyboardState State { get; private set; } = new(null, null, "Current hardware state is unverified.");

    internal static uint Merge(uint configuration, bool? enabled, bool? timeout)
    {
        uint value = configuration;
        if (enabled.HasValue) value = (value & ~0xFFU) | (enabled.Value ? 100U : 0U);
        if (timeout.HasValue) value = (value & ~0xFF00U) | (timeout.Value ? 0x1E00U : 0U);
        return value;
    }

    public KeyboardOperationResult Apply(bool? enabled, bool? timeout)
    {
        KeyboardOperationResult Finish(KeyboardOperationResult result) { report?.Invoke(result); return result; }
        lock (HardwareWriteGate.CommandLock)
        lock (_gate)
        {
            if (!enabled.HasValue && !timeout.HasValue) return Finish(new("failed", "No keyboard change was requested."));
            if ((enabled.HasValue && !capabilities.SupportsPower) || (timeout.HasValue && !capabilities.SupportsIdleTimeout))
                return Finish(new("failed", "This keyboard control is unavailable."));
            if (!firmware.TryRead(out uint current))
                return Finish(new("failed", "A readable OEM keyboard configuration is unavailable. No command was sent; firmware state was preserved."));
            uint desired = Merge(current, enabled, timeout);
            if (!firmware.TryWrite(desired)) return Finish(new("failed", "The OEM keyboard command failed. The setting was not saved."));
            if (!firmware.TryRead(out uint actual) || (actual & 0xFFFFU) != (desired & 0xFFFFU))
            {
                State = State with { Status = "Command sent, but hardware confirmation is unavailable. The setting was not saved." };
                return Finish(new("unverified", State.Status));
            }
            // Do not confuse firmware idle sleep with the user's enabled preference.
            State = new(enabled ?? State.Enabled, timeout ?? State.IdleTimeout, "Setting confirmed by the OEM keyboard interface.");
            return Finish(new("confirmed", State.Status));
        }
    }

    public KeyboardOperationResult? Restore(bool? enabled, bool? timeout)
        => enabled.HasValue || timeout.HasValue ? Apply(enabled, timeout) : null;
}
