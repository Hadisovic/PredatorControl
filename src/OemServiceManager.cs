// ====================================================================================================
// PREDATOR CONTROL · WINDOWS SCM & OEM DRIVER SERVICE ORCHESTRATOR
// File: src/OemServiceManager.cs
//
// 📖 C++ / SYSTEMS DEVELOPER ROSETTA STONE:
// - C++ Equivalent : Windows Service Control Manager client (advapi32.dll / winsvc.h).
// - Subsystem Role : Queries, configures, starts, and repairs background Windows services
//                    (ASMSvc, AcerDeviceEnablingService, AcerLightingService, AASSvc) and runs pnputil.exe.
//
// 🔄 C# -> C++ TYPE TRANSLATION REFERENCE:
// - ServiceController                 => SC_HANDLE via OpenSCManagerW / OpenServiceW
// - sc.Status == Running              => QueryServiceStatusEx(hService, ... SERVICE_STATUS_PROCESS)
// - sc.Start()                        => StartServiceW(hService, 0, NULL)
// - sc.Stop()                         => ControlService(hService, SERVICE_CONTROL_STOP, ...)
// - Process.Start("sc.exe", ...)      => ChangeServiceConfigW(hService, ..., SERVICE_AUTO_START, ...)
// ====================================================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.ServiceProcess;
using System.Windows.Forms;
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
        /// Optional delegate for intercepting user consent prompts (useful for testing and headless runs).
        /// Signature: (serviceDisplayName, binaryPath) => bool userApproved
        /// </summary>
        public static Func<string, string, bool>? UserConsentPrompt { get; set; }

        #region Authenticode & Digital Signature Verification

        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
        private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 = new Guid("{00AAC56B-CD44-11d0-8CC2-00C04FC295EE}");

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
        private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, ref WINTRUST_DATA pWVTData);

        private const uint WTD_UI_NONE = 2;
        private const uint WTD_REVOKE_NONE = 0;
        private const uint WTD_CHOICE_FILE = 1;
        private const uint WTD_STATEACTION_IGNORE = 0;
        private const uint WTD_CACHE_ONLY_URL_RETRIEVAL = 0x00004000;

        /// <summary>
        /// Calls Windows WinVerifyTrust to verify that a file has an intact, untampered Authenticode signature.
        /// Returns 0 (ERROR_SUCCESS) on valid trust verification.
        /// </summary>
        public static int VerifyWinTrust(string path)
        {
            var fileInfo = new WINTRUST_FILE_INFO
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
                pcwszFilePath = path,
                hFile = IntPtr.Zero,
                pgKnownSubject = IntPtr.Zero
            };

            IntPtr pFileInfo = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
            try
            {
                Marshal.StructureToPtr(fileInfo, pFileInfo, false);
                var data = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                    pPolicyCallbackData = IntPtr.Zero,
                    pSIPClientData = IntPtr.Zero,
                    dwUIChoice = WTD_UI_NONE,
                    fdwRevocationChecks = WTD_REVOKE_NONE,
                    dwUnionChoice = WTD_CHOICE_FILE,
                    pFile = pFileInfo,
                    dwStateAction = WTD_STATEACTION_IGNORE,
                    hWVTStateData = IntPtr.Zero,
                    pwszURLReference = IntPtr.Zero,
                    dwProvFlags = WTD_CACHE_ONLY_URL_RETRIEVAL,
                    dwUIContext = 0,
                    pSignatureSettings = IntPtr.Zero
                };

                return WinVerifyTrust(INVALID_HANDLE_VALUE, WINTRUST_ACTION_GENERIC_VERIFY_V2, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(pFileInfo);
            }
        }

        /// <summary>
        /// Verifies that a discovered driver or service file is Authenticode-signed, trusted by Windows,
        /// and published by Acer Incorporated or Microsoft Windows Hardware Compatibility Publisher (WHQL).
        /// Rejects any file outside C:\Windows\System32\DriverStore\FileRepository.
        /// </summary>
        public static bool VerifyFileAuthenticity(string filePath, out string failureReason)
        {
            failureReason = string.Empty;
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                failureReason = $"File does not exist: {filePath}";
                return false;
            }

            try
            {
                string fullPath = Path.GetFullPath(filePath);
                string driverStore = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "DriverStore", "FileRepository");
                if (!fullPath.StartsWith(driverStore, StringComparison.OrdinalIgnoreCase))
                {
                    failureReason = $"Untrusted path: file is located outside DriverStore ({fullPath})";
                    return false;
                }

                string ext = Path.GetExtension(fullPath).ToLowerInvariant();

                if (ext == ".exe")
                {
                    // 1. WinVerifyTrust check (must be untampered and cryptographically valid)
                    int trustResult = VerifyWinTrust(fullPath);
                    if (trustResult != 0)
                    {
                        failureReason = $"WinVerifyTrust failed (0x{trustResult:X8}) on {Path.GetFileName(fullPath)}";
                        return false;
                    }

                    // 2. Extract signing certificate and verify publisher is Acer
#pragma warning disable SYSLIB0057
                    using var rawCert = X509Certificate.CreateFromSignedFile(fullPath);
#pragma warning restore SYSLIB0057
                    using var cert = new X509Certificate2(rawCert);

                    string subject = cert.Subject;
                    bool isAcer = subject.Contains("Acer Incorporated", StringComparison.OrdinalIgnoreCase) ||
                                 subject.Contains("O=Acer Incorporated", StringComparison.OrdinalIgnoreCase) ||
                                 subject.Contains("CN=Acer Incorporated", StringComparison.OrdinalIgnoreCase);

                    if (!isAcer)
                    {
                        failureReason = $"Unexpected publisher '{subject}' (expected Acer Incorporated) on {Path.GetFileName(fullPath)}";
                        return false;
                    }

                    // 3. Chain validation
                    using var chain = new X509Chain();
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
                    chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
                    chain.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromSeconds(3);
                    bool chainValid = chain.Build(cert);

                    if (!chainValid)
                    {
                        bool onlyOfflineRevocation = chain.ChainStatus.Length > 0 && chain.ChainStatus.All(s =>
                            s.Status == X509ChainStatusFlags.RevocationStatusUnknown ||
                            s.Status == X509ChainStatusFlags.OfflineRevocation);

                        if (onlyOfflineRevocation)
                        {
                            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                            chainValid = chain.Build(cert);
                        }
                    }

                    if (!chainValid)
                    {
                        string errors = string.Join("; ", chain.ChainStatus.Select(s => s.StatusInformation.Trim()));
                        failureReason = $"Certificate chain validation failed on {Path.GetFileName(fullPath)}: {errors}";
                        return false;
                    }

                    return true;
                }
                else if (ext == ".inf")
                {
                    // For an INF driver package:
                    // 1. Must have an associated .cat security catalog in the same directory
                    string dir = Path.GetDirectoryName(fullPath) ?? string.Empty;
                    var catFiles = Directory.GetFiles(dir, "*.cat");
                    if (catFiles.Length == 0)
                    {
                        failureReason = $"No companion security catalog (.cat) found for driver INF {Path.GetFileName(fullPath)}";
                        return false;
                    }

                    // 2. The catalog file must pass WinVerifyTrust
                    bool anyCatValid = false;
                    foreach (var cat in catFiles)
                    {
                        if (VerifyWinTrust(cat) == 0)
                        {
                            anyCatValid = true;
                            break;
                        }
                    }

                    if (!anyCatValid)
                    {
                        failureReason = $"Security catalog verification failed for {Path.GetFileName(fullPath)}";
                        return false;
                    }

                    // 3. Any EXEs in the driver package directory must also be verified as Acer-signed
                    var dirExes = Directory.GetFiles(dir, "*.exe", SearchOption.AllDirectories);
                    if (dirExes.Length > 0)
                    {
                        bool anyExeVerified = false;
                        foreach (var dirExe in dirExes)
                        {
                            if (VerifyFileAuthenticity(dirExe, out _))
                            {
                                anyExeVerified = true;
                                break;
                            }
                        }

                        if (!anyExeVerified)
                        {
                            failureReason = $"Driver package contains executables, but none passed Acer signature verification for {Path.GetFileName(fullPath)}";
                            return false;
                        }
                    }

                    return true;
                }
                else
                {
                    failureReason = $"Unsupported file extension '{ext}' for driver/service verification";
                    return false;
                }
            }
            catch (Exception ex)
            {
                failureReason = $"Signature verification exception on {Path.GetFileName(filePath)}: {ex.Message}";
                return false;
            }
        }

        #endregion

        #region User Consent Management

        private static bool CheckOrRequestConsent(string serviceName, string displayName, string binaryPath, bool forcePrompt = false)
        {
            try
            {
                using var reg = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PredatorControl");
                if (reg != null && !forcePrompt)
                {
                    var val = reg.GetValue($"Consent_Install_{serviceName}");
                    if (val is int v)
                    {
                        return v == 1;
                    }
                }
            }
            catch { }

            bool approved = RequestUserConsent(displayName, binaryPath);

            try
            {
                using var reg = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\PredatorControl");
                reg?.SetValue($"Consent_Install_{serviceName}", approved ? 1 : 0, RegistryValueKind.DWord);
            }
            catch { }

            return approved;
        }

        private static bool RequestUserConsent(string serviceDisplayName, string path)
        {
            if (UserConsentPrompt != null)
            {
                return UserConsentPrompt(serviceDisplayName, path);
            }

            try
            {
                string message =
                    $"Predator Control detected that a missing Acer OEM hardware service is required for full hardware support on your laptop:\n\n" +
                    $"Service: {serviceDisplayName}\n" +
                    $"Binary: {path}\n\n" +
                    $"The binary has been cryptographically verified and signed by Acer Incorporated.\n\n" +
                    $"Would you like to install and register this Windows service to start automatically?";

                string title = "Predator Control - Install OEM Hardware Service";

                var result = MessageBox.Show(
                    message,
                    title,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                return result == DialogResult.Yes;
            }
            catch
            {
                return false;
            }
        }

        #endregion

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
        /// Verifies Authenticode signature and DriverStore origin before execution.
        /// </summary>
        public static bool InstallDriverInf(string infPath)
        {
            if (!File.Exists(infPath)) return false;
            if (!VerifyFileAuthenticity(infPath, out _)) return false;

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
        /// Searches Windows DriverStore for driver packages matching the target INF or EXE names.
        /// Strictly restricted to C:\Windows\System32\DriverStore\FileRepository (Administrator-only).
        /// Never searches user-writable directories.
        /// </summary>
        public static List<string> FindMatchingDriverFiles(string[] searchPatterns)
        {
            var results = new List<string>();
            string driverStore = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "DriverStore", "FileRepository");

            if (!Directory.Exists(driverStore))
                return results;

            try
            {
                // In DriverStore, folders are named like: sysmonitorservice.inf_amd64_...
                foreach (var dir in Directory.EnumerateDirectories(driverStore))
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

                            // Also check for EXEs inside this driver directory or its subdirectories (e.g. x64)
                            try
                            {
                                foreach (var file in Directory.EnumerateFiles(dir, "*.exe", SearchOption.AllDirectories))
                                {
                                    if (!results.Contains(file)) results.Add(file);
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }

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
        public static (bool success, string summary) AutoRepairForChassis(AcerChassisFamily chassis, Action<string>? statusCallback = null, bool forcePrompt = false)
        {
            if (chassis == AcerChassisFamily.Nitro)
            {
                return AutoRepairNitro(statusCallback, forcePrompt);
            }
            else if (chassis == AcerChassisFamily.Predator)
            {
                return AutoRepairPredator(statusCallback, forcePrompt);
            }
            else
            {
                // Generic Acer chassis: apply both battery limit and available hardware services
                var r1 = AutoRepairNitro(statusCallback, forcePrompt);
                var r2 = AutoRepairPredator(statusCallback, forcePrompt);
                return (r1.success || r2.success, $"{r1.summary}; {r2.summary}");
            }
        }

        /// <summary>
        /// Auto-installs and repairs essential OEM services for Acer Nitro laptops:
        /// ASMSvc (Battery 80% manager), AcerDeviceEnablingService / V2 (AcerIO bridge), AcerServiceSvc.
        /// </summary>
        public static (bool success, string summary) AutoRepairNitro(Action<string>? statusCallback = null, bool forcePrompt = false)
        {
            int repairedCount = 0;
            int installedCount = 0;

            statusCallback?.Invoke("Inspecting Acer Nitro hardware services...");

            bool asmInstalled = IsServiceInstalled("ASMSvc");
            bool adesInstalled = IsServiceInstalled("AcerDeviceEnablingServiceV2") || IsServiceInstalled("AcerDeviceEnablingService");
            bool svcInstalled = IsServiceInstalled("AcerServiceSvc");

            // 1. Authenticated DriverStore INF Installation & Service Creation if any required service is missing
            if (!asmInstalled || !adesInstalled || !svcInstalled)
            {
                statusCallback?.Invoke("Scanning DriverStore for OEM hardware drivers...");
                var infPatterns = new[] { "sysmonitorservice.inf", "acerdeviceenablingservicecomponent.inf", "acerservicecomponent.inf" };
                var foundFiles = FindMatchingDriverFiles(infPatterns);

                // Build candidate install plans for each missing service
                var servicePlans = new List<(string serviceName, string displayName, string? infFile, string? exeFile)>();

                if (!asmInstalled)
                {
                    string? inf = foundFiles.FirstOrDefault(f => f.EndsWith("sysmonitorservice.inf", StringComparison.OrdinalIgnoreCase));
                    string? exe = foundFiles.FirstOrDefault(f => f.EndsWith("AcerSystemCentralService.exe", StringComparison.OrdinalIgnoreCase));
                    if (inf != null || exe != null)
                        servicePlans.Add(("ASMSvc", "Acer System Monitor Service (ASMSvc)", inf, exe));
                }

                if (!adesInstalled)
                {
                    string? inf = foundFiles.FirstOrDefault(f => f.EndsWith("acerdeviceenablingservicecomponent.inf", StringComparison.OrdinalIgnoreCase));
                    string? exe = foundFiles.FirstOrDefault(f => f.EndsWith("ADESv2Svc.exe", StringComparison.OrdinalIgnoreCase));
                    if (inf != null || exe != null)
                        servicePlans.Add(("AcerDeviceEnablingServiceV2", "Acer Device Enabling Service V2", inf, exe));
                }

                if (!svcInstalled)
                {
                    string? inf = foundFiles.FirstOrDefault(f => f.EndsWith("acerservicecomponent.inf", StringComparison.OrdinalIgnoreCase));
                    string? exe = foundFiles.FirstOrDefault(f => f.EndsWith("AcerServiceWrapper.exe", StringComparison.OrdinalIgnoreCase) || f.EndsWith("AcerService.exe", StringComparison.OrdinalIgnoreCase));
                    if (inf != null || exe != null)
                        servicePlans.Add(("AcerServiceSvc", "Acer Service Wrapper (AcerServiceSvc)", inf, exe));
                }

                foreach (var plan in servicePlans)
                {
                    string targetFile = plan.exeFile ?? plan.infFile!;

                    // Step A: Cryptographic Authenticode & Publisher verification
                    if (!VerifyFileAuthenticity(targetFile, out string failReason))
                    {
                        statusCallback?.Invoke($"Signature verification rejected {Path.GetFileName(targetFile)}: {failReason}");
                        continue;
                    }

                    if (!string.IsNullOrEmpty(plan.infFile) && plan.infFile != targetFile)
                    {
                        if (!VerifyFileAuthenticity(plan.infFile, out string infFail))
                        {
                            statusCallback?.Invoke($"Signature verification rejected {Path.GetFileName(plan.infFile)}: {infFail}");
                            continue;
                        }
                    }

                    // Step B: Explicit User Consent before installing or creating new Windows service
                    if (!CheckOrRequestConsent(plan.serviceName, plan.displayName, targetFile, forcePrompt))
                    {
                        statusCallback?.Invoke($"Registration of {plan.displayName} declined by user.");
                        continue;
                    }

                    // Step C: Install INF driver package
                    if (!string.IsNullOrEmpty(plan.infFile) && File.Exists(plan.infFile))
                    {
                        statusCallback?.Invoke($"Installing driver: {Path.GetFileName(plan.infFile)}...");
                        if (InstallDriverInf(plan.infFile))
                        {
                            installedCount++;
                        }
                    }

                    // Step D: Fallback direct SCM service creation if INF did not register the service
                    if (!IsServiceInstalled(plan.serviceName) && !string.IsNullOrEmpty(plan.exeFile) && File.Exists(plan.exeFile))
                    {
                        statusCallback?.Invoke($"Registering {plan.displayName} via SCM...");
                        RunProcess("sc.exe", $"create \"{plan.serviceName}\" binPath= \"\\\"{plan.exeFile}\\\"\" start= auto DisplayName= \"{plan.displayName}\"", 3000);
                        if (IsServiceInstalled(plan.serviceName))
                        {
                            installedCount++;
                        }
                    }
                }
            }

            // 2. Ensure Startup Mode = Automatic & Start Services (safe path for ALREADY-INSTALLED services)
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

            string summary = $"Nitro services verified: {repairedCount} running" + (installedCount > 0 ? $", {installedCount} installed/registered" : string.Empty);
            statusCallback?.Invoke(summary);
            return (repairedCount > 0, summary);
        }

        /// <summary>
        /// Auto-installs and repairs essential OEM services for Acer Predator laptops:
        /// AcerLightingService (RGB Keyboard & Chassis Lighting synchronization) and AASSvc.
        /// </summary>
        public static (bool success, string summary) AutoRepairPredator(Action<string>? statusCallback = null, bool forcePrompt = false)
        {
            int repairedCount = 0;
            int installedCount = 0;

            statusCallback?.Invoke("Inspecting Acer Predator hardware services...");

            bool lightingInstalled = IsServiceInstalled("AcerLightingService");

            // 1. Authenticated DriverStore INF Installation & Service Creation if AcerLightingService is missing
            if (!lightingInstalled)
            {
                statusCallback?.Invoke("Scanning DriverStore for Predator lighting drivers...");
                var foundFiles = FindMatchingDriverFiles(new[] { "acerlightingservice.inf", "predatorservice.inf" });

                string? inf = foundFiles.FirstOrDefault(f => f.EndsWith("acerlightingservice.inf", StringComparison.OrdinalIgnoreCase) || f.EndsWith("predatorservice.inf", StringComparison.OrdinalIgnoreCase));
                string? exe = foundFiles.FirstOrDefault(f => f.EndsWith("AcerLightingService.exe", StringComparison.OrdinalIgnoreCase));

                if (inf != null || exe != null)
                {
                    string targetFile = exe ?? inf!;

                    // Step A: Cryptographic Authenticode & Publisher verification
                    if (!VerifyFileAuthenticity(targetFile, out string failReason))
                    {
                        statusCallback?.Invoke($"Signature verification rejected {Path.GetFileName(targetFile)}: {failReason}");
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(inf) && inf != targetFile)
                        {
                            if (!VerifyFileAuthenticity(inf, out string infFail))
                            {
                                statusCallback?.Invoke($"Signature verification rejected {Path.GetFileName(inf)}: {infFail}");
                                inf = null;
                            }
                        }

                        // Step B: Explicit User Consent before installing or creating new Windows service
                        if (!CheckOrRequestConsent("AcerLightingService", "Acer Lighting Service", targetFile, forcePrompt))
                        {
                            statusCallback?.Invoke("Registration of AcerLightingService declined by user.");
                        }
                        else
                        {
                            // Step C: Install INF driver package
                            if (!string.IsNullOrEmpty(inf) && File.Exists(inf))
                            {
                                statusCallback?.Invoke($"Installing driver: {Path.GetFileName(inf)}...");
                                if (InstallDriverInf(inf))
                                {
                                    installedCount++;
                                }
                            }

                            // Step D: Fallback direct SCM registration if INF did not register service
                            if (!IsServiceInstalled("AcerLightingService") && !string.IsNullOrEmpty(exe) && File.Exists(exe))
                            {
                                statusCallback?.Invoke("Registering AcerLightingService via SCM...");
                                RunProcess("sc.exe", $"create \"AcerLightingService\" binPath= \"\\\"{exe}\\\"\" start= auto DisplayName= \"Acer Lighting Service\"", 3000);
                                if (IsServiceInstalled("AcerLightingService"))
                                {
                                    installedCount++;
                                }
                            }
                        }
                    }
                }
            }

            // 2. Configure Startup Mode = Automatic & Start Services (safe path for ALREADY-INSTALLED services)
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
