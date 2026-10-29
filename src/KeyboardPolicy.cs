namespace PredatorControlApp;

public enum KeyboardKind { Unknown, Binary, Rgb, None }

public sealed record KeyboardCapabilities(KeyboardKind Kind, string Evidence, string? FixedColor = null)
{
    public bool SupportsPower => Kind is KeyboardKind.Binary or KeyboardKind.Rgb;
    public bool SupportsBrightness => Kind == KeyboardKind.Rgb;
    public bool SupportsRgb => Kind == KeyboardKind.Rgb;
    public bool SupportsIdleTimeout => SupportsPower;

    public static bool IsNitroModel(string model) =>
        model.Contains("Nitro", StringComparison.OrdinalIgnoreCase) ||
        System.Text.RegularExpressions.Regex.IsMatch(model.Trim(), @"^(?:ANV|AN)\d{2,3}(?:-|$)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public static KeyboardCapabilities Detect(string manufacturer, string model, bool rgbInterface, string? profile)
    {
        if (!manufacturer.Equals("Acer", StringComparison.OrdinalIgnoreCase) &&
            !manufacturer.StartsWith("Acer ", StringComparison.OrdinalIgnoreCase))
            return new(KeyboardKind.Unknown, "Keyboard hardware has not been identified.");
        // Product policy: Nitro uses a fixed red backlight, regardless of generic RGB
        // method metadata, leftover OEM services or a previously saved RGB profile.
        if (IsNitroModel(model))
            return new(KeyboardKind.Binary, "Nitro keyboard: fixed red backlight with On/Off control. Brightness, colors and RGB effects are unavailable.", "#ff394b");
        if (profile is "binary" or "rgb" or "none")
            return new(profile == "binary" ? KeyboardKind.Binary : profile == "rgb" ? KeyboardKind.Rgb : KeyboardKind.None,
                "Keyboard type selected by you. Firmware control is verified separately.");
        return rgbInterface
            ? new(KeyboardKind.Rgb, "RGB firmware interface detected. Commands may still be unavailable on this configuration.")
            : new(KeyboardKind.Unknown, "Keyboard type is unverified. Select your keyboard type to enable its controls.");
    }
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
internal sealed class KeyboardController(KeyboardCapabilities capabilities, IKeyboardFirmware firmware)
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
        lock (_gate)
        {
            if (!capabilities.SupportsPower || (timeout.HasValue && !capabilities.SupportsIdleTimeout))
                return new("failed", "This keyboard control is unavailable.");
            if (!firmware.TryRead(out uint current))
                return new("failed", "A readable OEM keyboard configuration is unavailable. No command was sent; firmware state was preserved.");
            uint desired = Merge(current, enabled, timeout);
            if (!firmware.TryWrite(desired)) return new("failed", "The OEM keyboard command failed. The setting was not saved.");
            if (!firmware.TryRead(out uint actual) || (actual & 0xFFFFU) != (desired & 0xFFFFU))
            {
                State = State with { Status = "Command sent, but hardware confirmation is unavailable. The setting was not saved." };
                return new("unverified", State.Status);
            }
            // Do not confuse firmware idle sleep with the user's enabled preference.
            State = new(enabled ?? State.Enabled, timeout ?? State.IdleTimeout, "Setting confirmed by the OEM keyboard interface.");
            return new("confirmed", State.Status);
        }
    }

    public KeyboardOperationResult? Restore(bool? enabled, bool? timeout)
        => enabled.HasValue || timeout.HasValue ? Apply(enabled, timeout) : null;
}
