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

            // Immediate ultra-fast hardware enforcement at boot:
            // Reads user's persisted preferences from registry (HKLM machine mirror or HKCU)
            // and immediately sets ACPI WMI registers + AcerLightingService profile
            // within the first few milliseconds of execution before user logon.
            try
            {
                bool enableLimit = false;
                int rgbMode = 0;
                int brightness = 100;
                int rgbSpeed = 50;
                int rgbR = 0, rgbG = 230, rgbB = 180;
                int[] zoneColors = new int[4] { unchecked((int)0xFF00E6B4), unchecked((int)0xFF00E6B4), unchecked((int)0xFF00E6B4), unchecked((int)0xFF00E6B4) };

                // 1. Check HKLM first (machine boot mirror, accessible to SYSTEM before user logon)
                using (var hklmKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\PredatorControl"))
                {
                    if (hklmKey != null)
                    {
                        object? val = hklmKey.GetValue("BatteryLimit");
                        if (val is int i) enableLimit = i == 1;
                        else if (val != null && int.TryParse(val.ToString(), out int p)) enableLimit = p == 1;

                        if (hklmKey.GetValue("RGB_Mode") is int rm) rgbMode = rm;
                        if (hklmKey.GetValue("Brightness") is int br) brightness = br;
                        if (hklmKey.GetValue("RGB_Speed") is int sp) rgbSpeed = sp;
                        if (hklmKey.GetValue("RGB_R") is int r) rgbR = r;
                        if (hklmKey.GetValue("RGB_G") is int g) rgbG = g;
                        if (hklmKey.GetValue("RGB_B") is int b) rgbB = b;

                        for (int z = 0; z < 4; z++)
                        {
                            if (hklmKey.GetValue($"ZoneColor_{z}") is int zc) zoneColors[z] = zc;
                        }
                    }
                }

                // 2. If not found in HKLM, check CurrentUser
                if (!enableLimit)
                {
                    using var regKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PredatorControl");
                    if (regKey != null)
                    {
                        object? val = regKey.GetValue("BatteryLimit");
                        if (val is int i) enableLimit = i == 1;
                        else if (val != null && int.TryParse(val.ToString(), out int p)) enableLimit = p == 1;

                        if (regKey.GetValue("RGB_Mode") is int rm) rgbMode = rm;
                        if (regKey.GetValue("Brightness") is int br) brightness = br;
                        if (regKey.GetValue("RGB_Speed") is int sp) rgbSpeed = sp;
                        if (regKey.GetValue("RGB_R") is int r) rgbR = r;
                        if (regKey.GetValue("RGB_G") is int g) rgbG = g;
                        if (regKey.GetValue("RGB_B") is int b) rgbB = b;

                        for (int z = 0; z < 4; z++)
                        {
                            if (regKey.GetValue($"ZoneColor_{z}") is int zc) zoneColors[z] = zc;
                        }
                    }
                }

                using var wmiEarly = new WmiController();
                wmiEarly.SetBatteryChargeLimit(enableLimit);

                // Early boot keyboard lighting enforcement:
                // Applies user's saved color to ACPI WMI and updates LightingProfile.ini with ActiveMode=2 (Static)
                // so AcerLightingService loads user color immediately upon service start instead of amber.
                var zones = new Color[4];
                for (int z = 0; z < 4; z++)
                {
                    zones[z] = Color.FromArgb(zoneColors[z]);
                }
                byte mappedEarlySpeed = (byte)Math.Clamp(Math.Round(rgbSpeed * 9.0 / 100.0), 1, 9);
                wmiEarly.ApplyLightingSynchronous(zones, rgbMode, (byte)Math.Clamp(brightness, 0, 100), mappedEarlySpeed);
            }
            catch { }

            if (args.Length > 0 && args[0].Equals("--boot-limit", StringComparison.OrdinalIgnoreCase))
            {
                return; // Fast headless battery charge enforcement mode
            }

            ApplicationConfiguration.Initialize();
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

            SelfCheck.Run();

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
