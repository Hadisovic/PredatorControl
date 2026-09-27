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

        public static BatteryHealthInfo? CachedInfo => _cachedInfo;

        public static Task<BatteryHealthInfo?> GetBatteryHealthAsync(bool forceRefresh = false)
            => Task.Run(() => GetBatteryHealth(forceRefresh));

        public static BatteryHealthInfo? GetBatteryHealth(bool forceRefresh = false)
        {
            long now = Environment.TickCount64;
            if (!forceRefresh && _cachedInfo != null && (now - _lastQueryTick < 60_000)) // 1 min cache
                return _cachedInfo;

            // 1. Instant ACPI WMI hardware probe (~5ms, zero subprocess overhead)
            try
            {
                int design = 0, full = 0, cycles = 0;

                using (var s = new System.Management.ManagementObjectSearcher(@"root\WMI", "SELECT DesignedCapacity FROM BatteryStaticData"))
                using (var res = s.Get())
                {
                    foreach (System.Management.ManagementObject mo in res)
                    {
                        if (mo["DesignedCapacity"] is uint d && d > 0) { design = (int)d; break; }
                        if (mo["DesignedCapacity"] is int di && di > 0) { design = di; break; }
                    }
                }

                using (var s = new System.Management.ManagementObjectSearcher(@"root\WMI", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity"))
                using (var res = s.Get())
                {
                    foreach (System.Management.ManagementObject mo in res)
                    {
                        if (mo["FullChargedCapacity"] is uint f && f > 0) { full = (int)f; break; }
                        if (mo["FullChargedCapacity"] is int fi && fi > 0) { full = fi; break; }
                    }
                }

                using (var s = new System.Management.ManagementObjectSearcher(@"root\WMI", "SELECT CycleCount FROM BatteryCycleCount"))
                using (var res = s.Get())
                {
                    foreach (System.Management.ManagementObject mo in res)
                    {
                        if (mo["CycleCount"] is uint c) { cycles = (int)c; break; }
                        if (mo["CycleCount"] is int ci) { cycles = ci; break; }
                    }
                }

                if (design > 0 && full > 0)
                {
                    double wear = Math.Max(0.0, Math.Round((1.0 - ((double)full / design)) * 100.0, 1));
                    string status = wear < 15.0 ? "Good" : (wear < 30.0 ? "Normal" : "Degraded");
                    _cachedInfo = new BatteryHealthInfo(design, full, cycles, wear, status);
                    _lastQueryTick = now;
                    return _cachedInfo;
                }
            }
            catch { }

            // 2. Fallback to Windows powercfg XML battery report
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

        public static void OpenBatteryReport()
        {
            _ = Task.Run(() =>
            {
                try
                {
                    string reportPath = Path.Combine(Path.GetTempPath(), "battery-report.html");
                    using (var p = Process.Start(new ProcessStartInfo
                    {
                        FileName = "powercfg.exe",
                        Arguments = $"/batteryreport /output \"{reportPath}\"",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    }))
                    {
                        p?.WaitForExit(4500);
                    }

                    if (File.Exists(reportPath))
                    {
                        Process.Start(new ProcessStartInfo(reportPath) { UseShellExecute = true });
                    }
                }
                catch { }
            });
        }
    }
}
