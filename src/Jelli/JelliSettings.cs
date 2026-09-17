using Microsoft.Win32;
using System.Text.Json;

namespace PredatorControlApp;

internal sealed record JelliSettings
{
    public string Presentation { get; init; } = "expand";
    public string Metric1 { get; init; } = "cpuTemp";
    public string Metric2 { get; init; } = "gpuTemp";
    public string OverlayCorner { get; init; } = "topLeft";
    public string OverlayMonitor { get; init; } = "";
    public bool OverlayGraphs { get; init; } = true;
    public bool OverlayPower { get; init; } = true;
    public bool OverlayUsage { get; init; } = true;
    public bool SyncRgb { get; init; }
    public int X { get; init; } = 80;
    public int Y { get; init; } = 120;

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static readonly string[] Metrics = ["cpuTemp", "gpuTemp", "cpuFanRpm", "gpuFanRpm", "cpuUsage", "gpuUsage", "gpuPowerW", "batteryPercent", "powerMode", "fps"];
    internal static JelliSettings Load()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PredatorControl");
            return Validate(JsonSerializer.Deserialize<JelliSettings>(key?.GetValue("JelliUI") as string ?? "{}", Json) ?? new());
        }
        catch { return new(); }
    }
    internal static JelliSettings Validate(JelliSettings s)
    {
        if (s.Presentation is not ("expand" or "flyout") ||
            s.OverlayCorner is not ("topLeft" or "topRight" or "bottomLeft" or "bottomRight") ||
            !Metrics.Contains(s.Metric1) || !Metrics.Contains(s.Metric2))
            throw new ArgumentException("Invalid Jelli preferences.");
        return s;
    }
    internal void Save()
    {
        Validate(this);
        using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\PredatorControl");
        key.SetValue("JelliUI", JsonSerializer.Serialize(this, Json));
    }
}
