using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using Microsoft.Win32;

namespace PredatorControlApp
{
    /// <summary>
    /// Out-of-the-box Acer OEM Hardware Services Auto-Installer & Auto-Repair Engine.
    /// Handles silent driver INF installation via pnputil, sc.exe service registration,
    /// startup configuration, service startup, and battery threshold registry persistence
    /// for Acer Nitro and Predator laptops without requiring Acer Care Center or NitroSense.
    /// </summary>
    public static class OemServiceManager
    {
        public static readonly string[] NitroEssentialServices =
        {
            "ASMSvc",
            "AcerDeviceEnablingServiceV2",
            "AcerDeviceEnablingService",
            "AcerServiceSvc"
        };

        public static readonly string[] PredatorEssentialServices =
        {
            "AcerLightingService",
            "AASSvc"
        };

        /// <summary>
        /// Checks whether a Windows Service is registered in the SCM or Registry.
        /// </summary>
        public static bool IsServiceInstalled(string serviceName)
        {
            try
            {
                using var sc = new ServiceController(serviceName);
                _ = sc.Status;
                return true;
            }
            catch
            {
                try
                {
                    using var reg = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
                    return reg != null;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Checks whether a Windows Service is actively running.
        /// </summary>
        public static bool IsServiceRunning(string serviceName)
        {
            try
            {
                using var sc = new ServiceController(serviceName);
                return sc.Status == ServiceControllerStatus.Running;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Configures a Windows Service to start automatically and launches it if stopped.
        /// </summary>
        public static void EnsureServiceAutoAndRunning(string serviceName)
        {
            if (!IsServiceInstalled(serviceName)) return;

            // 1. Configure startup to Automatic (both via sc.exe and direct registry)
            RunProcess("sc.exe", $"config \"{serviceName}\" start= auto", 2000);
            try
            {
                using var reg = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}", true);
                reg?.SetValue("Start", 2, RegistryValueKind.DWord);
            }
            catch { }

            // 2. Start service if not already running
            try
            {
                using var sc = new ServiceController(serviceName);
                if (sc.Status != ServiceControllerStatus.Running && sc.Status != ServiceControllerStatus.StartPending)
                {
                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(2));
                }
            }
            catch { }

            // Fallback net.exe start
            RunProcess("net.exe", $"start \"{serviceName}\"", 3000);
        }

        /// <summary>
        /// Silently installs an OEM driver INF file using Windows pnputil.exe.
        /// </summary>
        public static bool InstallDriverInf(string infPath)
        {
            if (!File.Exists(infPath)) return false;
            try
            {
                int exitCode = RunProcess("pnputil.exe", $"/add-driver \"{infPath}\" /install", 12000);
                return exitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Searches Windows DriverStore and local directories for driver packages matching the target INF or EXE names.
        /// </summary>
        public static List<string> FindMatchingDriverFiles(string[] searchPatterns)
        {
            var results = new List<string>();
            var searchRoots = new List<string>();

            // 1. Windows DriverStore FileRepository
            string driverStore = @"C:\Windows\System32\DriverStore\FileRepository";
            if (Directory.Exists(driverStore))
            {
                searchRoots.Add(driverStore);
            }

            // 2. App directory and local oem_drivers folders
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string localOem = Path.Combine(appDir, "oem_drivers");
            if (Directory.Exists(localOem)) searchRoots.Add(localOem);

            string parentOem = Path.Combine(appDir, "..", "oem_drivers");
            if (Directory.Exists(parentOem)) searchRoots.Add(parentOem);

            foreach (var root in searchRoots)
            {
                try
                {
                    // Scan directories for matching folders or files
                    if (root.Equals(driverStore, StringComparison.OrdinalIgnoreCase))
                    {
                        // In DriverStore, folders are named like: sysmonitorservice.inf_amd64_...
                        foreach (var dir in Directory.EnumerateDirectories(root))
                        {
                            string dirName = Path.GetFileName(dir);
                            foreach (var pattern in searchPatterns)
                            {
                                string basePattern = Path.GetFileNameWithoutExtension(pattern);
                                if (dirName.StartsWith(basePattern, StringComparison.OrdinalIgnoreCase) ||
                                    dirName.Contains(basePattern, StringComparison.OrdinalIgnoreCase))
                                {
                                    string fullInfPath = Path.Combine(dir, pattern);
                                    if (File.Exists(fullInfPath) && !results.Contains(fullInfPath))
                                    {
                                        results.Add(fullInfPath);
                                    }

                                    // Also check for EXEs inside this driver directory
                                    foreach (var file in Directory.EnumerateFiles(dir, "*.exe"))
                                    {
                                        if (!results.Contains(file)) results.Add(file);
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        // In local folders, recursively search
                        foreach (var pattern in searchPatterns)
                        {
                            foreach (var file in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
                            {
                                if (!results.Contains(file)) results.Add(file);
                            }
                        }
                    }
                }
                catch { }
            }

            return results;
        }

        /// <summary>
        /// Writes AcerCareCenter Battery 80% charge threshold registry keys to both 64-bit and 32-bit registry paths.
        /// This activates the battery limiter directly without requiring Acer Care Center software.
        /// </summary>
        public static void ConfigureNitroBatteryLimitRegistry()
        {
            string[] subKeys =
            {
                @"SOFTWARE\OEM\AcerCareCenter\Battery",
                @"SOFTWARE\WOW6432Node\OEM\AcerCareCenter\Battery"
            };

            foreach (var subKey in subKeys)
            {
                try
                {
                    using var reg = Registry.LocalMachine.CreateSubKey(subKey, true);
                    if (reg != null)
                    {
                        reg.SetValue("StopCharging", 80, RegistryValueKind.DWord);
                        reg.SetValue("HealthControl", 1, RegistryValueKind.DWord);
                        reg.SetValue("BatteryLimit", 1, RegistryValueKind.DWord);
                        reg.SetValue("LimitPercent", 80, RegistryValueKind.DWord);
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// Dispatches auto-repair and auto-install routines tailored to the detected chassis.
        /// </summary>
        public static (bool success, string summary) AutoRepairForChassis(AcerChassisFamily chassis, Action<string>? statusCallback = null)
        {
            if (chassis == AcerChassisFamily.Nitro)
            {
                return AutoRepairNitro(statusCallback);
            }
            else if (chassis == AcerChassisFamily.Predator)
            {
                return AutoRepairPredator(statusCallback);
            }
            else
            {
                // Generic Acer chassis: apply both battery limit and available hardware services
                var r1 = AutoRepairNitro(statusCallback);
                var r2 = AutoRepairPredator(statusCallback);
                return (r1.success || r2.success, $"{r1.summary}; {r2.summary}");
            }
        }

        /// <summary>
        /// Auto-installs and repairs essential OEM services for Acer Nitro laptops:
        /// ASMSvc (Battery 80% manager), AcerDeviceEnablingService / V2 (AcerIO bridge), AcerServiceSvc.
        /// </summary>
        public static (bool success, string summary) AutoRepairNitro(Action<string>? statusCallback = null)
        {
            int repairedCount = 0;
            int installedCount = 0;

            statusCallback?.Invoke("Inspecting Acer Nitro hardware services...");

            bool asmInstalled = IsServiceInstalled("ASMSvc");
            bool adesInstalled = IsServiceInstalled("AcerDeviceEnablingServiceV2") || IsServiceInstalled("AcerDeviceEnablingService");
            bool svcInstalled = IsServiceInstalled("AcerServiceSvc");

            // 1. Silent DriverStore & Local INF Installation if any required service is missing
            if (!asmInstalled || !adesInstalled || !svcInstalled)
            {
                statusCallback?.Invoke("Scanning DriverStore for OEM hardware drivers...");
                var infPatterns = new[] { "sysmonitorservice.inf", "acerdeviceenablingservicecomponent.inf", "acerservicecomponent.inf" };
                var foundFiles = FindMatchingDriverFiles(infPatterns);

                foreach (var file in foundFiles)
                {
                    if (file.EndsWith(".inf", StringComparison.OrdinalIgnoreCase))
                    {
                        statusCallback?.Invoke($"Installing driver: {Path.GetFileName(file)}...");
                        if (InstallDriverInf(file))
                        {
                            installedCount++;
                        }
                    }
                }

                // 2. Direct SCM fallback creation if INF didn't create the service
                if (!IsServiceInstalled("ASMSvc"))
                {
                    foreach (var file in foundFiles)
                    {
                        if (file.EndsWith("AcerSystemCentralService.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            statusCallback?.Invoke("Registering ASMSvc service...");
                            RunProcess("sc.exe", $"create \"ASMSvc\" binPath= \"\\\"{file}\\\"\" start= auto DisplayName= \"Acer System Monitor Service\"", 3000);
                            installedCount++;
                            break;
                        }
                    }
                }

                if (!IsServiceInstalled("AcerDeviceEnablingServiceV2") && !IsServiceInstalled("AcerDeviceEnablingService"))
                {
                    foreach (var file in foundFiles)
                    {
                        if (file.EndsWith("ADESv2Svc.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            statusCallback?.Invoke("Registering AcerDeviceEnablingServiceV2 service...");
                            RunProcess("sc.exe", $"create \"AcerDeviceEnablingServiceV2\" binPath= \"\\\"{file}\\\"\" start= auto DisplayName= \"Acer Device Enabling Service V2\"", 3000);
                            installedCount++;
                            break;
                        }
                    }
                }
            }

            // 3. Ensure Startup Mode = Automatic & Start Services
            statusCallback?.Invoke("Configuring service startup & running states...");
            foreach (var svc in NitroEssentialServices)
            {
                if (IsServiceInstalled(svc))
                {
                    EnsureServiceAutoAndRunning(svc);
                    if (IsServiceRunning(svc))
                    {
                        repairedCount++;
                    }
                }
            }

            // 4. Configure Battery 80% Limiter Registry Keys
            statusCallback?.Invoke("Configuring 80% battery charge limit...");
            ConfigureNitroBatteryLimitRegistry();

            string summary = $"Nitro services verified: {repairedCount} running" + (installedCount > 0 ? $", {installedCount} installed/registered" : string.Empty);
            statusCallback?.Invoke(summary);
            return (repairedCount > 0, summary);
        }

        /// <summary>
        /// Auto-installs and repairs essential OEM services for Acer Predator laptops:
        /// AcerLightingService (RGB Keyboard & Chassis Lighting synchronization) and AASSvc.
        /// </summary>
        public static (bool success, string summary) AutoRepairPredator(Action<string>? statusCallback = null)
        {
            int repairedCount = 0;
            int installedCount = 0;

            statusCallback?.Invoke("Inspecting Acer Predator hardware services...");

            bool lightingInstalled = IsServiceInstalled("AcerLightingService");

            // 1. Silent DriverStore & Local INF Installation if AcerLightingService is missing
            if (!lightingInstalled)
            {
                statusCallback?.Invoke("Scanning DriverStore for Predator lighting drivers...");
                var foundFiles = FindMatchingDriverFiles(new[] { "acerlightingservice.inf" });

                foreach (var file in foundFiles)
                {
                    if (file.EndsWith(".inf", StringComparison.OrdinalIgnoreCase))
                    {
                        statusCallback?.Invoke($"Installing driver: {Path.GetFileName(file)}...");
                        if (InstallDriverInf(file))
                        {
                            installedCount++;
                        }
                    }
                }

                // SCM fallback creation if INF didn't register service
                if (!IsServiceInstalled("AcerLightingService"))
                {
                    foreach (var file in foundFiles)
                    {
                        if (file.EndsWith("AcerLightingService.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            statusCallback?.Invoke("Registering AcerLightingService...");
                            RunProcess("sc.exe", $"create \"AcerLightingService\" binPath= \"\\\"{file}\\\"\" start= auto DisplayName= \"Acer Lighting Service\"", 3000);
                            installedCount++;
                            break;
                        }
                    }
                }
            }

            // 2. Configure Startup Mode = Automatic & Start Services
            statusCallback?.Invoke("Configuring Predator services...");
            foreach (var svc in PredatorEssentialServices)
            {
                if (IsServiceInstalled(svc))
                {
                    EnsureServiceAutoAndRunning(svc);
                    if (IsServiceRunning(svc))
                    {
                        repairedCount++;
                    }
                }
            }

            string summary = $"Predator services verified: {repairedCount} running" + (installedCount > 0 ? $", {installedCount} installed/registered" : string.Empty);
            statusCallback?.Invoke(summary);
            return (repairedCount > 0, summary);
        }

        /// <summary>
        /// Generates a diagnostic summary string of all hardware services for UI display or reporting.
        /// </summary>
        public static string GetServiceDiagnostics(AcerChassisFamily chassis)
        {
            var sb = new System.Text.StringBuilder();
            var targetSvcs = chassis == AcerChassisFamily.Nitro ? NitroEssentialServices : PredatorEssentialServices;

            foreach (var svc in targetSvcs)
            {
                bool installed = IsServiceInstalled(svc);
                bool running = installed && IsServiceRunning(svc);
                string status = !installed ? "Not Installed" : (running ? "Running" : "Stopped");
                sb.AppendLine($"{svc}: {status}");
            }

            return sb.ToString().TrimEnd();
        }

        private static int RunProcess(string exe, string args, int timeoutMs)
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                if (p != null)
                {
                    p.WaitForExit(timeoutMs);
                    return p.ExitCode;
                }
            }
            catch { }
            return -1;
        }
    }
}
