using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PredatorControlApp
{
    [SupportedOSPlatform("windows")]
    internal static class Program
    {
        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        private static long _lastTrimTick;

        /// <summary>
        /// Compacts the managed heap and asks the Windows Memory Manager to trim unused working-set pages.
        /// Rate-limited to at most once every 3 seconds.
        /// </summary>
        internal static void TrimWorkingSet(bool forceGc = false)
        {
            long now = Environment.TickCount64;
            if (!forceGc && now - Interlocked.Read(ref _lastTrimTick) < 3000) return;
            Interlocked.Exchange(ref _lastTrimTick, now);

            _ = Task.Run(() =>
            {
                try
                {
                    if (forceGc)
                    {
                        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                        GC.WaitForPendingFinalizers();
                    }
                    using var proc = Process.GetCurrentProcess();
                    EmptyWorkingSet(proc.Handle);
                }
                catch { }
            });
        }
        private static readonly HashSet<string> _reported = new();
        private static bool _dialogOpen;
        private static Form1? form;

        private static string LogPath
        {
            get
            {
                var dir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(dir)) dir = Path.GetTempPath();
                return Path.Combine(dir, "PredatorControl", "crash.log");
            }
        }

        internal static void Report(Exception ex, bool fatal)
        {
            string key = $"{ex.GetType().FullName}|{ex.StackTrace}";

            try
            {
                var path = LogPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, $"[{DateTime.Now:u}] {(fatal ? "FATAL" : "ERROR")} {ex}\n\n");
            }
            catch { }

            // Never display a modal dialog if Windows is shutting down / logging off
            if (Form1.IsShuttingDown || Environment.HasShutdownStarted) return;

            if (_dialogOpen || !_reported.Add(key)) return;

            _dialogOpen = true;
            try
            {
                MessageBox.Show(
                    $"{(fatal ? "Fatal" : "Error")}: {ex.Message}\n\nLogged to:\n{LogPath}",
                    "Predator Control", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
            finally { _dialogOpen = false; }
        }

        /// <summary>
        /// Applies persisted hardware preferences (battery limit, RGB, boot sound, USB charging).
        /// batteryOnly = true performs only the headless battery charge enforcement (--boot-limit).
        /// Registry reads use RegistryConfig (HKCU first, then HKLM fallback).
        /// </summary>
        private static void ApplyHardwarePreferences(bool batteryOnly = false)
        {
            try
            {
                int? preference = RegistryConfig.ReadInt("BatteryLimit");
                if (!preference.HasValue) return;
                bool enableLimit = preference == 1;
                using var wmiEarly = new WmiController();
                wmiEarly.SetBatteryChargeLimit(enableLimit);

                if (batteryOnly) return;

                // Keyboard restoration runs once in Form1 after capability detection.
                // Never send generic RGB defaults before the keyboard has been identified.

                // Boot Sound and USB Power-off Charging enforcement
                try
                {
                    int? bs = RegistryConfig.ReadInt("BootSound");
                    if (bs.HasValue) wmiEarly.SetBootSound(bs.Value == 1);
                    int? uc = RegistryConfig.ReadInt("UsbCharging");
                    if (uc.HasValue) wmiEarly.SetUsbCharging(uc.Value == 1);
                }
                catch { }
            }
            catch { }
        }

        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--diagnostics")
            {
                // Read-only compatibility mode: bypass startup preferences, service optimization and UI hooks.
                try
                {
                    using var controller = new WmiController();
                    File.WriteAllText(Path.GetFullPath(args[1]), controller.ExportDiagnosticReport());
                }
                catch { Environment.ExitCode = 1; }
                return;
            }
            if (args.Length == 1 && args[0] is "--repair-services" or "--optimize-services")
            {
                ApplicationConfiguration.Initialize();
                bool success;
                if (args[0] == "--repair-services")
                    success = OemServiceManager.AutoRepairForChassis(WmiController.DetectChassisFamily(), forcePrompt: true).success;
                else
                    success = OemServiceManager.OptimizeTelemetryServices();
                Environment.ExitCode = success ? 0 : 1;
                return;
            }
            // Enforce High Process Priority Class for responsive background fan/RGB polling and zero latency during heavy gaming
            try
            {
                using var currentProc = System.Diagnostics.Process.GetCurrentProcess();
                currentProc.PriorityClass = System.Diagnostics.ProcessPriorityClass.High;
            }
            catch { }

            if (args.Length > 0 && args[0].Equals("--boot-limit", StringComparison.OrdinalIgnoreCase))
            {
                ApplyHardwarePreferences(batteryOnly: true); // Fast headless battery charge enforcement mode
                return;
            }

            if (args.Length > 0 && args[0].Equals("--self-check", StringComparison.OrdinalIgnoreCase))
            {
#if DEBUG
                SelfCheck.Run();
#endif
                return;
            }

            ApplicationConfiguration.Initialize();
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            JelliHostForm.EnsureWebView2Loader();

#if DEBUG
            SelfCheck.Run();
#endif

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Report(e.Exception, false);

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                if (e.ExceptionObject is Exception ex) Report(ex, true);
            };

            SynchronizationContext? syncContext = null;

            // Single-Instance Named Pipe IPC:
            // If another instance is running, signal it to toggle/show and terminate immediately.
            bool isPrimary = SingleInstanceIpc.TryAcquireOrSignal("TOGGLE", cmd =>
            {
                if (syncContext != null)
                {
                    syncContext.Post(_ =>
                    {
                        try { form?.HandleIpcCommand(cmd); } catch { }
                    }, null);
                }
                else if (form != null && !form.IsDisposed)
                {
                    try
                    {
                        if (form.IsHandleCreated) form.BeginInvoke(() => form.HandleIpcCommand(cmd));
                        else form.HandleIpcCommand(cmd);
                    }
                    catch { }
                }
            }, out var ipc);

            if (!isPrimary)
            {
                return;
            }

            // Only the primary instance writes hardware state (secondary IPC launches exit above).
            // Preferences are restored by Form1 after capability resolution.

            try
            {
                var hardware = new WmiController();
                // Read-only discovery runs on a worker before a UI is constructed; telemetry reuses its snapshot.
                Task.Run(() => hardware.Capabilities).GetAwaiter().GetResult();
                form = new Form1(ipc, hardware);
                _ = form.Handle;
                syncContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            }
            catch (Exception ex)
            {
                Report(ex, true);
                ipc?.Dispose();
                return;
            }

            try
            {
                Application.Run(form);
            }
            finally
            {
                ipc?.Dispose();
                DebounceHelper.FlushAll();
            }
        }
    }
}
