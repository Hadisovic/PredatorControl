using System;
using System.Diagnostics;
using System.IO;
using System.Xml;

namespace PredatorControlApp
{
    public record BatteryHealthInfo(
        int DesignCapacityMWh,
        int FullChargeCapacityMWh,
        int CycleCount,
        double WearLevelPercent,
        string HealthStatus);

    /// <summary>
    /// Universal Battery Health &amp; Wear Level Diagnostics Engine.
    /// Reads native Windows battery hardware telemetry to compute design capacity,
    /// full charge capacity, charge cycle count, and wear percentage without requiring
    /// OEM bloatware like Acer Care Center.
    /// </summary>
    public static class BatteryHealthMonitor
    {
        private static BatteryHealthInfo? _cachedInfo;
        private static long _lastQueryTick = 0;

        public static Task<BatteryHealthInfo?> GetBatteryHealthAsync(bool forceRefresh = false)
            => Task.Run(() => GetBatteryHealth(forceRefresh));

        public static BatteryHealthInfo? GetBatteryHealth(bool forceRefresh = false)
        {
            long now = Environment.TickCount64;
            if (!forceRefresh && _cachedInfo != null && (now - _lastQueryTick < 300_000)) // 5 min cache
                return _cachedInfo;

            try
            {
                string tempXml = Path.Combine(Path.GetTempPath(), $"bat_rep_{Guid.NewGuid():N}.xml");
                using (var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "powercfg.exe",
                    Arguments = $"/batteryreport /xml /output \"{tempXml}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                }))
                {
                    p?.WaitForExit(3500);
                }

                if (File.Exists(tempXml))
                {
                    string xmlContent = File.ReadAllText(tempXml);
                    try { File.Delete(tempXml); } catch { }

                    var doc = new XmlDocument();
                    doc.LoadXml(xmlContent);

                    var designNode = doc.SelectSingleNode("//DesignCapacity");
                    var fullNode = doc.SelectSingleNode("//FullChargeCapacity");
                    var cycleNode = doc.SelectSingleNode("//CycleCount");

                    if (int.TryParse(designNode?.InnerText, out int design) && design > 0 &&
                        int.TryParse(fullNode?.InnerText, out int full) && full > 0)
                    {
                        int.TryParse(cycleNode?.InnerText, out int cycles);
                        double wear = Math.Max(0.0, Math.Round((1.0 - ((double)full / design)) * 100.0, 1));
                        string status = wear < 15.0 ? "Good" : (wear < 30.0 ? "Normal" : "Degraded");

                        _cachedInfo = new BatteryHealthInfo(design, full, cycles, wear, status);
                        _lastQueryTick = now;
                        return _cachedInfo;
                    }
                }
            }
            catch { }

            return _cachedInfo;
        }

        public static string GetFormattedReport(BatteryHealthInfo? info)
        {
            if (info == null) return "Battery health: Telemetry unavailable";
            string cycleText = info.CycleCount > 0 ? $" · {info.CycleCount} cycles" : string.Empty;
            return $"Health: {info.HealthStatus} ({info.WearLevelPercent}% wear) · {info.FullChargeCapacityMWh:N0} / {info.DesignCapacityMWh:N0} mWh{cycleText}";
        }
    }
}
