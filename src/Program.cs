using System.Drawing;
using System.Runtime.Versioning;

namespace PredatorControlApp
{
    [SupportedOSPlatform("windows")]
    internal static class Program
    {
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
                bool enableLimit = RegistryConfig.ReadInt("BatteryLimit") == 1;
                int rgbMode = RegistryConfig.ReadInt("RGB_Mode") ?? 0;
                int brightness = RegistryConfig.ReadInt("Brightness") ?? 100;
                int rgbSpeed = RegistryConfig.ReadInt("RGB_Speed") ?? 50;
                int[] zoneColors = new int[4];
                for (int z = 0; z < 4; z++)
                {
                    zoneColors[z] = RegistryConfig.ReadInt($"ZoneColor_{z}") ?? unchecked((int)0xFF00E6B4);
                }

                using var wmiEarly = new WmiController();
                wmiEarly.SetBatteryChargeLimit(enableLimit);

                if (batteryOnly) return;

                // Early boot keyboard lighting enforcement:
                // Applies user's saved color to ACPI WMI and updates LightingProfile.ini with ActiveMode=2 (Static)
                var zones = new Color[4];
                for (int z = 0; z < 4; z++)
                {
                    zones[z] = Color.FromArgb(zoneColors[z]);
                }
                byte mappedEarlySpeed = (byte)Math.Clamp(Math.Round(rgbSpeed * 9.0 / 100.0), 1, 9);
                wmiEarly.ApplyLightingSynchronous(zones, rgbMode, (byte)Math.Clamp(brightness, 0, 100), mappedEarlySpeed);

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
            ApplyHardwarePreferences();

            try
            {
                form = new Form1(ipc);
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
