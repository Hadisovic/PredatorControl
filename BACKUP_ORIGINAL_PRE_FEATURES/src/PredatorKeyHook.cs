using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PredatorControlApp
{
    [SupportedOSPlatform("windows")]
    public sealed class PredatorKeyHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        // Known Acer Predator / Nitro virtual keys
        public const uint VK_LAUNCH_APP2 = 0xB7; // 183 (Predator / Calc / OEM App 2)
        public const uint VK_LAUNCH_APP1 = 0xB6; // 182 (Nitro / OEM App 1)
        public const uint VK_F24 = 0x87;         // 135 (Frequently mapped to Turbo / Mode)
        public const uint VK_F23 = 0x86;         // 134

        // Known Acer Predator / Nitro scan codes
        public const uint ACER_SCANCODE_PREDATOR = 0x71;     // 113 (Alias)
        public const uint ACER_SCANCODE_PREDATOR_KEY = 0x71; // 113 (Physical Predator App Key)
        public const uint ACER_SCANCODE_PREDATOR_ALT = 0x6C; // 108
        public const uint ACER_SCANCODE_MODE_KEY = 0x76;     // 118 (Physical Turbo / Mode Key)
        public const uint ACER_SCANCODE_MODE_EXT = 0x54;     // 84  (Acer extended Mode Scan Code)
        public const uint ACER_SCANCODE_NITRO_1 = 0x6E;      // 110
        public const uint ACER_SCANCODE_NITRO_2 = 0x6D;      // 109
        public const uint LLKHF_EXTENDED = 0x01;

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        private IntPtr _hookId = IntPtr.Zero;
        private readonly LowLevelKeyboardProc _proc;
        private ManagementEventWatcher? _wmiWatcherApg;
        private ManagementEventWatcher? _wmiWatcherAcer;
        private long _lastPredatorTriggerTick = 0;
        private long _lastModeTriggerTick = 0;
        private bool _disposed;

        public event Action? PredatorKeyPressed;
        public event Action? ModeKeyPressed;

        public PredatorKeyHook()
        {
            _proc = HookCallback;
            InstallHook();
            StartWmiWatchers();
            RegisterAppKeyInRegistry();
        }

        private void InstallHook()
        {
            try
            {
                using var curProcess = Process.GetCurrentProcess();
                using var curModule = curProcess.MainModule;
                IntPtr moduleHandle = curModule != null ? GetModuleHandle(curModule.ModuleName) : IntPtr.Zero;

                _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, moduleHandle, 0);

                if (_hookId == IntPtr.Zero)
                {
                    _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
                }
            }
            catch (Exception ex)
            {
                Program.Report(ex, false);
            }
        }

        private void StartWmiWatchers()
        {
            // 1. APGeEvent Watcher: Used by AcerHardwareService.exe for Turbo/Mode key notifications
            try
            {
                var scope = new ManagementScope(@"root\wmi");
                scope.Connect();

                var queryApg = new EventQuery("SELECT * FROM APGeEvent");
                _wmiWatcherApg = new ManagementEventWatcher(scope, queryApg);
                _wmiWatcherApg.EventArrived += (s, e) =>
                {
                    try
                    {
                        var detail = e.NewEvent.Properties["EventDetail"]?.Value as byte[];
                        if (detail != null && detail.Length > 1)
                        {
                            // In AcerHardwareService.exe: detail[1] == 4 || detail[1] == 5 || (detail[1] == 0x10 && detail[2] == 0x07)
                            if (detail[1] == 0x04 || detail[1] == 0x05 ||
                                (detail.Length > 2 && detail[1] == 0x10 && detail[2] == 0x07))
                            {
                                TriggerModeKey();
                                return;
                            }
                        }
                    }
                    catch { }
                };
                _wmiWatcherApg.Start();
            }
            catch { }

            // 2. AcerGenericEvent Watcher
            try
            {
                var scope = new ManagementScope(@"root\wmi");
                scope.Connect();

                var queryAcer = new EventQuery("SELECT * FROM AcerGenericEvent");
                _wmiWatcherAcer = new ManagementEventWatcher(scope, queryAcer);
                _wmiWatcherAcer.EventArrived += (s, e) =>
                {
                    try
                    {
                        var detail = e.NewEvent.Properties["EventDetail"]?.Value as byte[];
                        if (detail != null && detail.Length > 1)
                        {
                            if (detail[1] == 0x04 || detail[1] == 0x05 ||
                                (detail.Length > 2 && detail[1] == 0x10 && detail[2] == 0x07))
                            {
                                TriggerModeKey();
                                return;
                            }
                        }
                    }
                    catch { }
                };
                _wmiWatcherAcer.Start();
            }
            catch { }
        }

        public void TriggerPredatorKey()
        {
            long now = Environment.TickCount64;
            if (now - Interlocked.Read(ref _lastPredatorTriggerTick) < 350) return;
            Interlocked.Exchange(ref _lastPredatorTriggerTick, now);

            Task.Run(() =>
            {
                try { PredatorKeyPressed?.Invoke(); } catch (Exception ex) { Program.Report(ex, false); }
            });
        }

        // Backward-compatibility alias
        public void TriggerKey() => TriggerPredatorKey();

        public void TriggerModeKey()
        {
            long now = Environment.TickCount64;
            if (now - Interlocked.Read(ref _lastModeTriggerTick) < 350) return;
            Interlocked.Exchange(ref _lastModeTriggerTick, now);

            Task.Run(() =>
            {
                try { ModeKeyPressed?.Invoke(); } catch (Exception ex) { Program.Report(ex, false); }
            });
        }

        public static bool IsModeKey(uint vkCode, uint scanCode, uint flags)
        {
            // Hardware scan codes emitted by physical Mode / Turbo button
            if (scanCode == ACER_SCANCODE_MODE_KEY || scanCode == ACER_SCANCODE_MODE_EXT)
                return true;

            // Extended Function Keys mapped to Mode / Turbo by laptop firmware
            if (vkCode == VK_F24 || vkCode == VK_F23)
                return true;

            return false;
        }

        public static bool IsPredatorKey(uint vkCode, uint scanCode, uint flags)
        {
            // Primary Acer Hotkeys: Launch App 2 (Predator), Launch App 1 (Nitro)
            if (vkCode == VK_LAUNCH_APP2 || vkCode == VK_LAUNCH_APP1)
                return true;

            // Physical Predator key scan codes
            if (scanCode == ACER_SCANCODE_PREDATOR_KEY ||
                scanCode == ACER_SCANCODE_PREDATOR_ALT ||
                scanCode == ACER_SCANCODE_NITRO_1 ||
                scanCode == ACER_SCANCODE_NITRO_2)
            {
                return true;
            }

            return false;
        }



        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN || wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP))
            {
                var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

                

                // CRITICAL SAFETY FILTER:
                // NEVER intercept Windows keys, modifier keys, or normal typing keys!
                if (kb.vkCode == 0x5B || kb.vkCode == 0x5C || kb.scanCode == 0x5B || // VK_LWIN, VK_RWIN
                    kb.vkCode == 0x10 || kb.vkCode == 0x11 || kb.vkCode == 0x12 || // Shift, Ctrl, Alt
                    kb.vkCode == 0x14 || kb.vkCode == 0x1B || kb.vkCode == 0x09 || // Caps, Esc, Tab
                    kb.vkCode == 0x0D || kb.vkCode == 0x20)                         // Enter, Space
                {
                    return CallNextHookEx(_hookId, nCode, wParam, lParam);
                }

                // 1. Check Mode Button
                if (IsModeKey(kb.vkCode, kb.scanCode, kb.flags))
                {
                    if (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)
                    {
                        TriggerModeKey();
                    }
                    return (IntPtr)1; // Suppress OEM key from Windows default handler
                }

                // 2. Check Predator App Key
                if (IsPredatorKey(kb.vkCode, kb.scanCode, kb.flags))
                {
                    if (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)
                    {
                        TriggerPredatorKey();
                    }
                    return (IntPtr)1; // Suppress OEM key
                }
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        /// <summary>
        /// Registers PredatorControlApp as the ShellExecute target for Windows AppKey 18 (Launch App 2 / Predator Key)
        /// and AppKey 17 (Launch App 1). Cleans up AppKey 15/16.
        /// </summary>
        public static void RegisterAppKeyInRegistry()
        {
            try
            {
                string exePath = Application.ExecutablePath;

                // Delete unwanted AppKeys 15 and 16 if previously written
                string[] unwantedKeys = { "15", "16" };
                foreach (var k in unwantedKeys)
                {
                    try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree($@"Software\Microsoft\Windows\CurrentVersion\Explorer\AppKey\{k}", false); } catch { }
                    try { Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree($@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\AppKey\{k}", false); } catch { }
                }

                // Register AppKey 18 (Launch App 2) & 17 (Launch App 1)
                string[] appKeys = { "18", "17" };
                foreach (var k in appKeys)
                {
                    try
                    {
                        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey($@"Software\Microsoft\Windows\CurrentVersion\Explorer\AppKey\{k}");
                        key?.SetValue("ShellExecute", exePath);
                    }
                    catch { }
                }

                foreach (var k in appKeys)
                {
                    try
                    {
                        using var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\AppKey\{k}");
                        key?.SetValue("ShellExecute", exePath);
                    }
                    catch { }
                }
            }
            catch { }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;

                try
                {
                    _wmiWatcherApg?.Stop();
                    _wmiWatcherApg?.Dispose();
                    _wmiWatcherApg = null;
                }
                catch { }

                try
                {
                    _wmiWatcherAcer?.Stop();
                    _wmiWatcherAcer?.Dispose();
                    _wmiWatcherAcer = null;
                }
                catch { }

                if (_hookId != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_hookId);
                    _hookId = IntPtr.Zero;
                }
            }
        }
    }
}
