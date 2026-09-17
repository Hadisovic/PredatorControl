using PredatorControlApp;

// Representative descriptors from Form1.Jelli; values are fixture preferences,
// never live telemetry. Every action is handled by the in-memory test backend.
internal static class FixtureCatalog
{
    internal static JelliSection[] Sections => [
        new("power", "Performance", [Button("power.quiet", "Quiet"), Button("power.balanced", "Balanced"), Button("power.performance", "Performance"), Button("power.turbo", "Turbo"), Button("power.eco", "Eco"),
            Select("power.ac", "When plugged in", "Balanced", "Performance"), Select("power.battery", "On battery", "Quiet", "Eco")]),
        new("fans", "Cooling", [Button("fans.auto", "Auto"), Button("fans.max", "Maximum"), Button("fans.custom", "Custom"), Button("fans.curve", "Edit fan curve ↗"), new("fans.cpu", "CPU fan", "range", 50, Min: 10), new("fans.gpu", "GPU fan", "range", 50, Min: 10)]),
        new("gpu", "GPU · restart required", [Button("gpu.hybrid", "Hybrid"), Button("gpu.discrete", "Discrete"), Button("gpu.auto", "Automatic")]),
        new("display", "Displays", [Button("display.60", "60 Hz"), Button("display.max", "165 Hz"), Toggle("display.overdrive", "LCD overdrive")]),
        new("battery", "Battery care", [Toggle("battery.limit", "Stop charging at 80%")]),
        new("hardware", "Hardware", [Toggle("hardware.keyLock", "Lock Windows & Menu keys")]),
        new("rgb", "Keyboard light", [Select("rgb.mode", "Lighting effect", "Static", "Breathing", "Neon", "Wave", "Shifting", "Zoom", "Meteor", "Twinkling", "Off"),
            Button("rgb.all", "All zones"), Button("rgb.zone0", "Zone 1"), Button("rgb.zone1", "Zone 2"), Button("rgb.zone2", "Zone 3"), Button("rgb.zone3", "Zone 4"),
            new("rgb.color", "Zone color", "color", "#81e4dc"), new("rgb.brightness", "Brightness", "range", 70), new("rgb.speed", "Effect speed", "range", 50), Toggle("rgb.sleep", "Backlight sleeps after 30 seconds")]),
        new("games", "Game Sync", [Toggle("games.enabled", "Apply profiles when games launch"), Button("games.configure", "Configure game profiles ↗")]),
        new("app", "Application", [Toggle("app.startup", "Start with Windows"), Button("app.updates", "Check for updates"), Button("app.teal", "Teal"), Button("app.crimson", "Crimson"), Button("app.oled", "OLED")]),
    ];
    private static JelliControl Button(string id, string label) => new(id, label, "button", false);
    private static JelliControl Toggle(string id, string label) => new(id, label, "toggle", false);
    private static JelliControl Select(string id, string label, params string[] options) => new(id, label, "select", 0, Choices: options.Select((label, value) => new JelliChoice(label, value)).ToArray());
}
