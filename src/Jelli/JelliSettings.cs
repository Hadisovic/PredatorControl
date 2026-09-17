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
    public string OverlayMode { get; init; } = "default";
    public int OverlayScale { get; init; } = 100;
    public bool OverlayGraphs { get; init; } = true;
    public bool OverlayPower { get; init; } = true;
    public bool OverlayUsage { get; init; } = true;
    public bool SyncRgb { get; init; }
    public bool JelliEnabled { get; init; } = true;
    public int X { get; init; } = 80;
    public int Y { get; init; } = 120;

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static readonly string[] Metrics = ["off", "none", "cpuTemp", "gpuTemp", "cpuFanRpm", "gpuFanRpm", "cpuUsage", "gpuUsage", "gpuPowerW", "batteryPercent", "powerMode", "fps", "cpuPowerW", "ramUsedGb", "vramUsedGb"];
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
        string pres = s.Presentation is "expand" or "flyout" ? s.Presentation : "expand";
        string corner = s.OverlayCorner is "topLeft" or "topRight" or "bottomLeft" or "bottomRight" ? s.OverlayCorner : "topLeft";
        string m1 = Metrics.Contains(s.Metric1) ? s.Metric1 : "off";
        string m2 = Metrics.Contains(s.Metric2) ? s.Metric2 : "off";
        return s with { Presentation = pres, OverlayCorner = corner, Metric1 = m1, Metric2 = m2 };
    }
    internal void Save()
    {
        Validate(this);
        using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\PredatorControl");
        key.SetValue("JelliUI", JsonSerializer.Serialize(this, Json));
    }
}
