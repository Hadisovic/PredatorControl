using PredatorControlApp;

internal static class UiCapabilityChecks
{
    internal static void Run(Action<bool, string> check)
    {
        HardwareCapabilities Capabilities(KeyboardCapabilities keyboard, params HardwareFeature[] writable) => new()
        {
            Keyboard = keyboard,
            Results = writable.ToDictionary(feature => feature,
                _ => new CapabilityEvidence(CapabilityStatus.Supported, "synthetic-ui-fixture", "Test only", Writable: true))
        };

        var red = Capabilities(new(KeyboardKind.Binary, "Synthetic fixed red", "#ff394b"), HardwareFeature.KeyboardPower);
        var redUi = KeyboardControlProjection.From(red);
        check(redUi.Power && !redUi.Rgb && redUi.ZoneCount == 0 && !redUi.Brightness && !redUi.EffectSpeed && !redUi.IdleTimeout,
            "Classic and Jelli fixed-red projection exposes only the verified power toggle");
        check(redUi.FixedColor == "#ff394b", "Fixed keyboard indicator preserves its explicit hardware color");

        foreach (int zones in new[] { 1, 3, 4 })
        {
            var rgb = Capabilities(new(KeyboardKind.Rgb, "Synthetic RGB", ZoneCount: zones, Effects: [0, 1, 8]), HardwareFeature.Rgb);
            var ui = KeyboardControlProjection.From(rgb);
            check(ui.Rgb && ui.ZoneCount == zones && ui.Effects.SequenceEqual([0, 1, 8]) && ui.AllowsZone(zones - 1) && !ui.AllowsZone(zones),
                $"Shared RGB projection exposes exactly {zones} zones and the reviewed effects");
            var basic = KeyboardControlProjection.From(rgb with { Keyboard = rgb.Keyboard with { BrightnessSupported = false, EffectSpeedSupported = false } });
            check(basic.Rgb && !basic.Brightness && !basic.EffectSpeed, $"{zones}-zone color support does not imply brightness or effect speed");
        }

        foreach (int zones in new[] { -1, 0, 5, 128 })
        {
            var invalid = Capabilities(new(KeyboardKind.Rgb, "Unsupported topology", ZoneCount: zones, Effects: [0]), HardwareFeature.Rgb);
            check(!KeyboardControlProjection.From(invalid).Rgb, "No unimplemented topology or per-key backend is exposed: " + zones);
        }
        var invalidEffects = Capabilities(new(KeyboardKind.Rgb, "Invalid effects", ZoneCount: 4, Effects: [0, 9]), HardwareFeature.Rgb);
        check(!KeyboardControlProjection.From(invalidEffects).Rgb, "Unknown effect IDs cannot become UI actions");
        var unknown = Capabilities(new(KeyboardKind.Unknown, "Unverified controller"), HardwareFeature.KeyboardPower, HardwareFeature.Rgb);
        var unknownUi = KeyboardControlProjection.From(unknown);
        check(!unknownUi.Power && !unknownUi.Rgb && unknownUi.Effects.Count == 0, "Unidentified keyboard presents diagnostics without firmware controls");
        check(!KeyboardControlProjection.From(red with { Results = new Dictionary<HardwareFeature, CapabilityEvidence>() }).Power,
            "Service or interface loss removes the fixed-color power control");
        var timeout = red with
        {
            Keyboard = red.Keyboard with { IdleTimeoutSupported = true },
            Results = red.Results.Append(new(HardwareFeature.KeyboardTimeout,
                new(CapabilityStatus.Supported, "synthetic-ui-fixture", "Test only", Writable: true))).ToDictionary()
        };
        check(KeyboardControlProjection.From(timeout).IdleTimeout && !KeyboardControlProjection.From(timeout with { Keyboard = red.Keyboard }).IdleTimeout,
            "Idle timeout requires both an explicit keyboard capability and an authorized operation");
        check(!KeyboardControlProjection.From(red with { Keyboard = red.Keyboard with { PowerToggleSupported = false } }).Power,
            "Fixed-color illumination does not imply software power control");
        check(!KeyboardControlProjection.From(red with { IsCurrent = false }).Power,
            "A stale shared capability snapshot cannot expose an enabled keyboard write control");
    }
}
