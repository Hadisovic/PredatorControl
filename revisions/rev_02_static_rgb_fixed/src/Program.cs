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
            // Immediate ultra-fast battery health limit enforcement at boot:
            // Reads user's persisted preference from registry and immediately sets the ACPI WMI register
            // within the first few milliseconds of execution to prevent the 1% charge creep on startup.
            try
            {
                using var regKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PredatorControl");
                if (regKey != null)
                {
                    object? val = regKey.GetValue("BatteryLimit");
                    bool enableLimit = false;
                    if (val is int i) enableLimit = i == 1;
                    else if (val != null && int.TryParse(val.ToString(), out int p)) enableLimit = p == 1;

                    using var wmiEarly = new WmiController();
                    wmiEarly.SetBatteryChargeLimit(enableLimit);
                }
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
