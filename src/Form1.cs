using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace PredatorControlApp
{
    [SupportedOSPlatform("windows")]
    public partial class Form1 : Form
    {
        #region Win32 Interop — DWM & Dark Mode

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        #endregion

        #region Win32 Interop — Window Dragging

        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HT_CAPTION = 0x2;

        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        private void TitleBar_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            }
        }

        #endregion

        #region Win32 Interop — Hotkeys & Foreground

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int dwProcessId);
        private const int ASFW_ANY = -1;

        private const uint MOD_NOREPEAT = 0x4000;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const int SW_RESTORE = 9;
        private const int WM_HOTKEY = 0x0312;
        private const int WM_APPCOMMAND = 0x0319;
        private const int WM_NCHITTEST = 0x0084;
        private const int HTCLIENT = 1;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;
        private const int APPCOMMAND_LAUNCH_APP2 = 25;
        private const int APPCOMMAND_LAUNCH_APP1 = 24;
        private const int HOTKEY_ID_PREDATOR_APP2 = 9101;
        private const int HOTKEY_ID_PREDATOR_APP1 = 9102;
        private const int HOTKEY_ID_PREDATOR_F24 = 9103;
        private const int HOTKEY_ID_PREDATOR_F23 = 9104;
        private const int HOTKEY_ID_OVERLAY = 9105;
        private const int HOTKEY_ID_OVERLAY_ALT = 9106;

        #endregion

        #region Fields

        private readonly WmiController _wmi = new();
        private readonly SingleInstanceIpc? _ipc;
        private PredatorKeyHook? _predatorKeyHook;
        private TelemetryService? _telemetryService;

        private NotifyIcon _trayIcon = new();
        private ContextMenuStrip _trayMenu = new();
        private readonly ColorDialog _colorPicker = new() { FullOpen = true };

        private int? _cpuTemp, _gpuTemp;
        private int? _cpuFanRpm, _gpuFanRpm;
        private DarkScrollPanel _contentPanel = null!;
        private Panel _pnlTitle = null!;

        private bool? _isPluggedIn;
        private bool? _pendingPluggedIn;
        private int _powerLineStableTicks;
        private bool _isResyncing;
        private bool _isClosing;

        private string? _internalDisplayGdiName;
        private int _maxHz;
        private float _dpiScale = 1f;

        private static readonly Font FontTitle = new("Segoe UI", 9.5f, FontStyle.Bold);
        private static readonly Font FontSectionHeader = new("Segoe UI", 8.5f, FontStyle.Bold);
        private static readonly Font FontBody = new("Segoe UI", 9.5f, FontStyle.Regular);
        private static readonly Font FontBodyBold = new("Segoe UI", 9.5f, FontStyle.Bold);
        private static readonly Font FontSmall = new("Segoe UI", 7.5f, FontStyle.Regular);

        private readonly List<Panel> _separators = new();
        private Label _lblTitle = null!, _lblCpuTemp = null!, _lblGpuTemp = null!;
        private Label _lblGpuHdr = null!, _lblGpuFanHdr = null!, _lblPowerHdr = null!;
        private Label _lblCpuRpm = null!, _lblGpuRpm = null!;
        private Label _lblPowerStatus = null!, _lblFanStatus = null!;
        private Label _lblBrightHdr = null!, _lblSpeedHdr = null!;
        private Label _lblCpuFanSpeedHdr = null!, _lblGpuFanSpeedHdr = null!;
        private Label _lblDisplayHdr = null!;

        private PredatorButton _btnQuiet = null!, _btnBalanced = null!, _btnPerform = null!,
                               _btnTurbo = null!, _btnEco = null!;

        private PredatorButton _btnAutoFan = null!, _btnMaxFan = null!, _btnCustomFan = null!;
        private PredatorButton _btnFixedSpeed = null!, _btnFanCurve = null!;
        private PredatorButton? _activeCustomSubBtn;
        private PredatorButton _btn60Hz = null!, _btnMaxHz = null!;

        // GPU Working Mode (MUX Switch)
        private Label _lblGpuModeHdr = null!;
        private PredatorButton _btnGpuOptimus = null!;
        private PredatorButton _btnGpuDiscrete = null!;
        private PredatorButton _btnGpuAuto = null!;
        private PredatorButton? _activeGpuModeBtn;
        private Label _lblGpuRestartNotice = null!;
        private int? _currentGpuMode;
        private int _gpuCapability = 7;

        private Label _lblExternalDisplayHdr = null!;
        private Panel _pnlExternalMonitors = null!;
        private List<DisplayCcdController.ExternalMonitorInfo> _externalMonitors = new();
        private const int WM_DISPLAYCHANGE = 0x007E;

        // Theme Switcher Buttons
        private PredatorButton _btnThemeTeal = null!, _btnThemeCrimson = null!, _btnThemeOled = null!;
        private PredatorButton? _activeThemeBtn;

        // RGB Controls
        private PredatorDropDown _rgbDropDown = null!;
        private PredatorButton _btnCustomColor = null!;
        private readonly PredatorButton[] _btnPresetColors = new PredatorButton[6];
        private readonly PredatorButton[] _btnZones = new PredatorButton[4];
        private PredatorButton _btnAllZones = null!;
        private int _selectedZone = -1; // -1 = All Zones, 0 = Zone 1, 1 = Zone 2, 2 = Zone 3, 3 = Zone 4
        private readonly Color[] _currentZoneColors = new Color[4]
        {
            Color.FromArgb(0, 230, 180),
            Color.FromArgb(0, 230, 180),
            Color.FromArgb(0, 230, 180),
            Color.FromArgb(0, 230, 180)
        };
        private static readonly Color[] PresetColors =
        {
            Color.FromArgb(0, 230, 180),   // Predator Teal
            Color.FromArgb(255, 0, 0),     // Pure Vivid Red (Fixes pink bug!)
            Color.FromArgb(0, 220, 255),   // Crisp Ice Cyan
            Color.FromArgb(255, 120, 0),   // Warm Amber Orange
            Color.FromArgb(170, 0, 255),   // Vivid Royal Purple
            Color.FromArgb(255, 255, 255)  // Clean White
        };
        private static readonly string[] PresetColorNames = { "Teal", "Red", "Cyan", "Amber", "Purple", "White" };

        private PredatorSlider _brightnessSlider = null!, _speedSlider = null!;
        private PredatorSlider _cpuFanSlider = null!, _gpuFanSlider = null!;

        private FanCurveForm? _fanCurveForm;
        private bool _fanCurveEnabled;
        private List<Point> _cpuCurvePoints = new() { new(30,10), new(45,15), new(55,30), new(65,50), new(72,65), new(80,80), new(88,92), new(95,100) };
        private List<Point> _gpuCurvePoints = new() { new(30,10), new(45,15), new(55,30), new(65,50), new(72,65), new(80,80), new(88,92), new(95,100) };
        private int _lastCurveCpuSpeed = -1;
        private int _lastCurveGpuSpeed = -1;

        private PredatorButton? _activePowerBtn, _activeFanBtn, _activeDisplayBtn;
        private bool _isUpdatingBattery;

        private PredatorDropDown _cboAcProfile = null!, _cboBatteryProfile = null!;
        private Label _lblAcProfileHdr = null!, _lblBatteryProfileHdr = null!;
        internal static readonly byte[] AcProfileValues = { 0xFF, 0x00, 0x01, 0x04, 0x05 };
        internal static readonly byte[] BatteryProfileValues = { 0xFF, 0x00, 0x01, 0x06 };

        private PredatorSwitch _switchBatteryLimit = null!;
        private Label _lblBatteryStatus = null!;

        private GameSyncController _gameSync = null!;
        private PredatorToggle _switchGameSync = null!;
        private Label _lblGameSyncStatus = null!;
        private PredatorButton _btnConfigureGames = null!;
        private bool _isGameSyncOverriding;

        private PredatorToggle _switchStartWithWindows = null!;
        private bool _suppressStartupToggle;
        private Label _lblStartupStatus = null!;

        private PredatorButton _btnCheckUpdates = null!;
        private bool _updateCheckRunning;

        // Hardware Feature Controls
        private PredatorToggle _switchLcdOverdrive = null!;
        private Label _lblLcdOverdrive = null!;

        private PredatorToggle _switchBacklight30s = null!;
        private Label _lblBacklight30s = null!;

        private PredatorToggle _switchWinKeyLock = null!;
        private Label _lblWinKeyLock = null!;

        private GameOverlayForm? _overlayForm;
        private ToolStripMenuItem? _trayOverlay;
        private PredatorToggle? _switchOverlay;
        private Label? _lblOverlay;
        private PredatorButton _btnOverlayLight = null!;
        private PredatorButton _btnOverlayDefault = null!;
        private PredatorButton _btnOverlayFull = null!;
        private Label _lblOverlayScaleHdr = null!;
        private PredatorSlider _sliderOverlayScale = null!;

        private static readonly string[] RgbModeNames = { "Static", "Breathing", "Neon", "Wave", "Shifting", "Zoom", "Meteor", "Twinkling", "Off" };

        private ToolStripMenuItem _trayPowerQuiet = null!, _trayPowerBal = null!, _trayPowerPerf = null!,
                                  _trayPowerTurbo = null!, _trayPowerEco = null!;
        private ToolStripMenuItem _trayFanAuto = null!, _trayFanMax = null!, _trayFanCustom = null!;
        private ToolStripMenuItem _trayDisplay60 = null!, _trayDisplayMax = null!;
        private ToolStripMenuItem _trayGpuOptimus = null!, _trayGpuDiscrete = null!, _trayGpuAuto = null!;
        private ToolStripMenuItem _trayBatteryLimit80 = null!, _trayBatteryLimit100 = null!;
        private ToolStripMenuItem _trayBatteryMenu = null!;
        private ToolStripMenuItem _trayRgbStatic = null!, _trayRgbBreathe = null!, _trayRgbNeon = null!,
                                  _trayRgbWave = null!, _trayRgbShift = null!, _trayRgbZoom = null!,
                                  _trayRgbMeteor = null!, _trayRgbTwinkle = null!, _trayRgbOff = null!;

        #endregion

        #region DPI Scaling Helper

        private int S(int px) => (int)(px * _dpiScale);

        #endregion

        #region Constructor

        public Form1(SingleInstanceIpc? ipc = null)
        {
            _ipc = ipc;
            InitializeComponent();
            DoubleBuffered = true;

            _dpiScale = DeviceDpi / 96f;

            // Query Win32 CCD API for the internal laptop screen
            _internalDisplayGdiName = DisplayCcdController.GetInternalDisplayGdiName();
            _maxHz = DisplayCcdController.GetMaxRefreshRate(_internalDisplayGdiName);

            _overlayForm = new GameOverlayForm();
            _overlayForm.OverlayStateChanged += (m, s) =>
            {
                try { if (IsHandleCreated) BeginInvoke(new Action(UpdateOverlayUI)); } catch { }
            };

            BuildUI();
            BuildTrayMenu();
            SetupSystemTray();

            // Setup display refresh rate highlight based on internal display
            int currentHz = DisplayCcdController.GetCurrentRefreshRate(_internalDisplayGdiName);
            if (currentHz <= 60)
            {
                HighlightBtn(_btn60Hz, ref _activeDisplayBtn);
                CheckTrayItem(_trayDisplay60, _trayDisplay60, _trayDisplayMax);
            }
            else
            {
                HighlightBtn(_btnMaxHz, ref _activeDisplayBtn);
                CheckTrayItem(_trayDisplayMax, _trayDisplay60, _trayDisplayMax);
            }

            LoadMemory();

            // Predator & Mode hardware key interception hook
            try
            {
                _predatorKeyHook = new PredatorKeyHook();
                _predatorKeyHook.PredatorKeyPressed += ToggleApp;
                _predatorKeyHook.ModeKeyPressed += () =>
                {
                    if (InvokeRequired) BeginInvoke(new Action(() => CyclePowerMode(true)));
                    else CyclePowerMode(true);
                };
            }
            catch (Exception ex)
            {
                Program.Report(ex, false);
            }

            // Async background telemetry service (PeriodicTimer on Task.Run)
            _telemetryService = new TelemetryService(_wmi, snapshot =>
            {
                if (_isClosing || IsDisposed || !IsHandleCreated) return;
                try { BeginInvoke(new Action(() => OnTelemetryReceived(snapshot))); }
                catch { }
            });

            _gameSync = new GameSyncController();
            _gameSync.GameDetected += OnGameDetected;
            _gameSync.GameExited += OnGameExited;

            if (_gameSync.IsEnabled)
            {
                _switchGameSync.Checked = true;
                _lblGameSyncStatus.Text = "Active \u2014 Monitoring";
            }

            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            FormClosed += (s, e) =>
            {
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                ThemeManager.ThemeChanged -= OnThemeChanged;
                try { _overlayForm?.Dispose(); } catch { }
                try { NvmlGpuMonitor.Shutdown(); } catch { }
            };

            ThemeManager.ThemeChanged += OnThemeChanged;
            ApplyCurrentTheme();

            Shown += (s, e) =>
            {
                EnableDarkTitleBar();
                Updater.ShowPendingNotes(this);
                if (Environment.CommandLine.Contains("-hidden")) HideApp();
                CheckPredatorSenseConflict();
                OptimizeAcerServices();

                // Delayed settling guard: Acer services (AcerLightingService / AcerAgentService)
                // complete their boot/logon initialization 1-4 seconds after user login.
                // Re-assert user's saved RGB profile to prevent late-boot stomping back to Amber.
                Task.Run(async () =>
                {
                    await Task.Delay(2500);
                    if (!IsDisposed)
                    {
                        BeginInvoke(() =>
                        {
                            try { ApplyRgbModeFromDropdown(_rgbDropDown.SelectedIndex); } catch { }
                        });
                    }

                    await Task.Delay(2500);
                    if (!IsDisposed)
                    {
                        BeginInvoke(() =>
                        {
                            try { ApplyRgbModeFromDropdown(_rgbDropDown.SelectedIndex); } catch { }
                        });
                    }
                });
            };
        }

        private void CheckPredatorSenseConflict()
        {
            try
            {
                var procs = System.Diagnostics.Process.GetProcessesByName("PredatorSense");
                if (procs.Length > 0)
                {
                    var res = MessageBox.Show(
                        "PredatorSense is currently running in the background.\n\n" +
                        "Running both PredatorSense and Predator Control simultaneously causes hardware conflicts on the laptop's Embedded Controller (EC), causing keyboard lighting and sensor glitches.\n\n" +
                        "Would you like to close PredatorSense now to ensure smooth operation?",
                        "PredatorSense Conflict Detected",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);

                    if (res == DialogResult.Yes)
                    {
                        foreach (var p in procs)
                        {
                            try { p.Kill(); } catch { }
                        }
                    }
                }
            }
            catch { }
        }

        private static void OptimizeAcerServices()
        {
            Task.Run(() =>
            {
                try
                {
                    // Bloatware & telemetry services safe to disable
                    string[] bloatServices =
                    {
                        "AcerCCAgentSvis",             // Acer Care Center
                        "AcerQAAgentSvis",             // Acer Quick Access
                        "AcerDIAgentSvis",             // Acer Device Info Telemetry
                        "ASMSvc",                      // Acer System Monitor Service
                        "AcerServiceSvc",              // Acer Service Component Wrapper
                        "AcerDeviceEnablingServiceV2"  // Acer Device Enabling Service V2
                    };

                    foreach (var svcName in bloatServices)
                    {
                        try
                        {
                            using var reg = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{svcName}", true);
                            if (reg != null)
                            {
                                int currentStart = (int)(reg.GetValue("Start", 2) ?? 2);
                                if (currentStart != 4) // 4 = Disabled
                                {
                                    reg.SetValue("Start", 4, RegistryValueKind.DWord);
                                    try
                                    {
                                        var psi = new ProcessStartInfo("net.exe", $"stop {svcName}")
                                        {
                                            CreateNoWindow = true,
                                            UseShellExecute = false
                                        };
                                        Process.Start(psi)?.WaitForExit(3000);
                                    }
                                    catch { }
                                }
                            }
                        }
                        catch { }
                    }

                    // Keep essential services on Automatic (2)
                    string[] essential = { "AASSvc", "AcerLightingService" };
                    foreach (var svc in essential)
                    {
                        try
                        {
                            using var reg = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{svc}", true);
                            if (reg != null)
                            {
                                int currentStart = (int)(reg.GetValue("Start", 2) ?? 2);
                                if (currentStart != 2) // 2 = Automatic
                                    reg.SetValue("Start", 2, RegistryValueKind.DWord);
                            }
                        }
                        catch { }
                    }

                    // Enforce High CPU Priority (3 = High) in Windows IFEO so the app automatically runs at High Priority
                    string[] ifeoTargets = { "PredatorControlApp.exe", "PredatorControl-standalone.exe", "PredatorControl-win-x64.exe" };
                    foreach (var exeName in ifeoTargets)
                    {
                        try
                        {
                            using var ifeoKey = Registry.LocalMachine.CreateSubKey($@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\{exeName}\PerfOptions");
                            ifeoKey?.SetValue("CpuPriorityClass", 3, RegistryValueKind.DWord);
                        }
                        catch { }
                    }
                }
                catch { }
            });
        }

        private void EnableDarkTitleBar()
        {
            try
            {
                int darkMode = 1;
                DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
            }
            catch { }
        }

        #endregion

        #region IPC & Visibility Controls

        public void HandleIpcCommand(string command)
        {
            if (command == "TOGGLE")
                ToggleApp();
            else if (command == "CYCLE_MODE" || command == "MODE")
                CyclePowerMode(true);
            else if (command == "EXIT" || command == "QUIT")
            {
                _isClosing = true;
                if (InvokeRequired) BeginInvoke(new Action(() => Application.Exit()));
                else Application.Exit();
            }
            else
                ShowApp();
        }

        public void ToggleApp()
        {
            if (InvokeRequired) { BeginInvoke(new Action(ToggleApp)); return; }

            // True toggle: hide whenever the window is visible (even if not focused, e.g. pressed during gaming),
            // show and bring to front when hidden or minimized.
            if (Visible && WindowState != FormWindowState.Minimized)
            {
                HideApp();
            }
            else
            {
                ShowApp();
            }
        }

        public void SetOverlayVisible(bool visible)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => SetOverlayVisible(visible))); return; }
            if (_overlayForm == null || _overlayForm.IsDisposed)
            {
                _overlayForm = new GameOverlayForm();
                _overlayForm.OverlayStateChanged += (m, s) =>
                {
                    try { if (IsHandleCreated) BeginInvoke(new Action(UpdateOverlayUI)); } catch { }
                };
            }

            if (visible)
            {
                _overlayForm.ShowOverlay();
            }
            else
            {
                _overlayForm.HideOverlay();
            }

            _telemetryService?.SetOverlayActive(visible);
            if (_trayOverlay != null) _trayOverlay.Checked = visible;
            if (_switchOverlay != null && _switchOverlay.Checked != visible) _switchOverlay.Checked = visible;
            SaveState("OverlayEnabled", visible ? 1 : 0);
        }

        public void ToggleGameOverlay()
        {
            if (_overlayForm == null || _overlayForm.IsDisposed)
            {
                _overlayForm = new GameOverlayForm();
                _overlayForm.OverlayStateChanged += (m, s) =>
                {
                    try { if (IsHandleCreated) BeginInvoke(new Action(UpdateOverlayUI)); } catch { }
                };
            }

            SetOverlayVisible(!_overlayForm.Visible);
        }

        private void UpdateOverlayUI()
        {
            if (_overlayForm == null) return;
            var mode = _overlayForm.Mode;
            int scale = _overlayForm.ScalePercent;

            if (_btnOverlayLight != null) _btnOverlayLight.IsActive = mode == OverlayMode.Light;
            if (_btnOverlayDefault != null) _btnOverlayDefault.IsActive = mode == OverlayMode.Default;
            if (_btnOverlayFull != null) _btnOverlayFull.IsActive = mode == OverlayMode.Full;

            if (_sliderOverlayScale != null && _sliderOverlayScale.Value != scale) _sliderOverlayScale.Value = scale;
            if (_lblOverlayScaleHdr != null) _lblOverlayScaleHdr.Text = $"OVERLAY SCALE: {scale}%";
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                RegisterHotKey(Handle, HOTKEY_ID_PREDATOR_APP2, MOD_NOREPEAT, PredatorKeyHook.VK_LAUNCH_APP2);
                RegisterHotKey(Handle, HOTKEY_ID_PREDATOR_APP1, MOD_NOREPEAT, PredatorKeyHook.VK_LAUNCH_APP1);
                RegisterHotKey(Handle, HOTKEY_ID_PREDATOR_F24, MOD_NOREPEAT, PredatorKeyHook.VK_F24);
                RegisterHotKey(Handle, HOTKEY_ID_PREDATOR_F23, MOD_NOREPEAT, PredatorKeyHook.VK_F23);
                RegisterHotKey(Handle, HOTKEY_ID_OVERLAY, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, (uint)Keys.O);
                RegisterHotKey(Handle, HOTKEY_ID_OVERLAY_ALT, MOD_CONTROL | MOD_SHIFT | MOD_ALT | MOD_NOREPEAT, (uint)Keys.O);
            }
            catch { }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            try
            {
                UnregisterHotKey(Handle, HOTKEY_ID_PREDATOR_APP2);
                UnregisterHotKey(Handle, HOTKEY_ID_PREDATOR_APP1);
                UnregisterHotKey(Handle, HOTKEY_ID_PREDATOR_F24);
                UnregisterHotKey(Handle, HOTKEY_ID_PREDATOR_F23);
                UnregisterHotKey(Handle, HOTKEY_ID_OVERLAY);
                UnregisterHotKey(Handle, HOTKEY_ID_OVERLAY_ALT);
            }
            catch { }
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                if (m.Result == (IntPtr)HTCLIENT)
                {
                    int x = (short)(m.LParam.ToInt64() & 0xFFFF);
                    int y = (short)((m.LParam.ToInt64() >> 16) & 0xFFFF);
                    Point pt = PointToClient(new Point(x, y));
                    int b = S(8);
                    bool l = pt.X <= b;
                    bool r = pt.X >= ClientSize.Width - b;
                    bool t = pt.Y <= b;
                    bool bot = pt.Y >= ClientSize.Height - b;

                    if (t && l) m.Result = (IntPtr)HTTOPLEFT;
                    else if (t && r) m.Result = (IntPtr)HTTOPRIGHT;
                    else if (bot && l) m.Result = (IntPtr)HTBOTTOMLEFT;
                    else if (bot && r) m.Result = (IntPtr)HTBOTTOMRIGHT;
                    else if (l) m.Result = (IntPtr)HTLEFT;
                    else if (r) m.Result = (IntPtr)HTRIGHT;
                    else if (t) m.Result = (IntPtr)HTTOP;
                    else if (bot) m.Result = (IntPtr)HTBOTTOM;
                }
                return;
            }
            else if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == HOTKEY_ID_PREDATOR_APP2 || id == HOTKEY_ID_PREDATOR_APP1)
                {
                    _predatorKeyHook?.TriggerPredatorKey();
                    return;
                }
                else if (id == HOTKEY_ID_PREDATOR_F24 || id == HOTKEY_ID_PREDATOR_F23)
                {
                    _predatorKeyHook?.TriggerModeKey();
                    return;
                }
                else if (id == HOTKEY_ID_OVERLAY || id == HOTKEY_ID_OVERLAY_ALT)
                {
                    ToggleGameOverlay();
                    return;
                }
            }
            else if (m.Msg == WM_APPCOMMAND)
            {
                int cmd = ((int)m.LParam >> 16) & 0xFFF;
                if (cmd == APPCOMMAND_LAUNCH_APP2 || cmd == APPCOMMAND_LAUNCH_APP1)
                {
                    _predatorKeyHook?.TriggerKey();
                    m.Result = (IntPtr)1;
                    return;
                }
            }

            else if (m.Msg == WM_DISPLAYCHANGE)
            {
                // A monitor was connected or disconnected — refresh external monitor section
                BeginInvoke(new Action(RefreshExternalMonitorSection));
            }

            base.WndProc(ref m);
        }

        public void ShowApp()
        {
            if (InvokeRequired) { BeginInvoke(new Action(ShowApp)); return; }

            AllowSetForegroundWindow(ASFW_ANY);
            Show();
            if (WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;

            ShowWindow(Handle, SW_RESTORE);
            SetForegroundWindow(Handle);
            TopMost = true;
            TopMost = false;
            Activate();
            BringToFront();
            _telemetryService?.SetPollingState(true);
            _telemetryService?.SetSensorMask(true);
        }

        public void HideApp()
        {
            if (InvokeRequired) { BeginInvoke(new Action(HideApp)); return; }

            Hide();
            _telemetryService?.SetPollingState(false);
            _telemetryService?.SetSensorMask(false);
        }

        #endregion

        #region Theme Switching

        private void ApplyCurrentTheme()
        {
            var theme = ThemeManager.Current;
            BackColor = theme.FormBg;
            _pnlTitle.BackColor = theme.TitleBarBg;
            _contentPanel.BackColor = theme.FormBg;

            // Highlight current theme button
            var activeThemeBtn = theme.Mode switch
            {
                ThemeMode.NitroCrimson => _btnThemeCrimson,
                ThemeMode.OledBlack => _btnThemeOled,
                _ => _btnThemeTeal
            };
            HighlightBtn(activeThemeBtn, ref _activeThemeBtn);

            foreach (var sep in _separators)
                sep.BackColor = theme.Separator;

            Invalidate(true);
        }

        private void OnThemeChanged()
        {
            if (InvokeRequired) { BeginInvoke(new Action(OnThemeChanged)); return; }
            ApplyCurrentTheme();
        }


        private void RefreshExternalMonitorSection()
        {
            if (_pnlExternalMonitors == null) return;

            // Detect currently connected external monitors via CCD API
            _externalMonitors = DisplayCcdController.GetExternalMonitors();

            bool hasExternal = _externalMonitors.Count > 0;

            // Show/hide the section header
            if (_lblExternalDisplayHdr != null)
                _lblExternalDisplayHdr.Visible = hasExternal;

            // Clear old rows
            _pnlExternalMonitors.Controls.Clear();
            _pnlExternalMonitors.Height = 0;

            if (!hasExternal)
            {
                _pnlExternalMonitors.Visible = false;
                return;
            }

            _pnlExternalMonitors.Visible = true;

            int rowH = S(36);
            int rowGap = S(6);
            int dropW = S(120);
            int btnW = S(70);
            int gap = S(6);
            int contentW = _pnlExternalMonitors.Width;
            int totalH = 0;

            var theme = ThemeManager.Current;

            for (int mi = 0; mi < _externalMonitors.Count; mi++)
            {
                var mon = _externalMonitors[mi];
                int rowY = mi * (rowH + rowGap);

                // Monitor name label (left side)
                string connType = mon.OutputTechnology switch
                {
                    DisplayCcdController.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI => "HDMI",
                    DisplayCcdController.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EXTERNAL => "DP",
                    DisplayCcdController.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DVI => "DVI",
                    _ => "EXT"
                };
                string labelText = $"{mon.FriendlyName}  [{connType}]";
                var lbl = new Label
                {
                    Text = labelText,
                    Location = new Point(0, rowY + (rowH - S(16)) / 2),
                    AutoSize = false,
                    Width = contentW - dropW - btnW - gap * 2,
                    Height = S(18),
                    Font = FontBody,
                    ForeColor = theme.TextSecondary,
                    BackColor = Color.Transparent,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                _pnlExternalMonitors.Controls.Add(lbl);

                // Hz dropdown — populated from supported rates from OS driver only
                var cbo = new PredatorDropDown
                {
                    Location = new Point(contentW - dropW - btnW - gap, rowY),
                    Size = new Size(dropW, rowH)
                };
                foreach (int hz in mon.SupportedHz)
                    cbo.Items.Add($"{hz} Hz");

                // Pre-select the current rate
                int curIdx = mon.SupportedHz.IndexOf(mon.CurrentHz);
                cbo.SelectedIndex = curIdx >= 0 ? curIdx : 0;
                _pnlExternalMonitors.Controls.Add(cbo);

                // Apply button
                var capturedMon = mon;
                var capturedCbo = cbo;
                var applyBtn = new PredatorButton
                {
                    Text = "Apply",
                    Location = new Point(contentW - btnW, rowY),
                    Size = new Size(btnW, rowH)
                };
                applyBtn.Click += (s, e) =>
                {
                    if (capturedCbo.SelectedIndex < 0) return;
                    int targetHz = capturedMon.SupportedHz[capturedCbo.SelectedIndex];
                    DisplayCcdController.SetExternalRefreshRate(capturedMon.GdiName, targetHz);
                    // Re-read to confirm the change took effect
                    BeginInvoke(new Action(RefreshExternalMonitorSection));
                };
                _pnlExternalMonitors.Controls.Add(applyBtn);

                totalH = rowY + rowH;
            }

            _pnlExternalMonitors.Height = totalH + rowGap;
            // New controls auto-apply the current theme via ThemeManager.ThemeChanged subscription
            // Labels need explicit color set since they don't subscribe
            foreach (Control c in _pnlExternalMonitors.Controls)
            {
                if (c is Label lbl2) lbl2.ForeColor = theme.TextSecondary;
            }
        }

        private void ApplyExternalDisplayMode(string gdiName, int hz)
        {
            DisplayCcdController.SetExternalRefreshRate(gdiName, hz);
        }

        #endregion

        #region Display Control

        private void ApplyDisplayMode(int hz, PredatorButton btn)
        {
            if (!DisplayCcdController.SetRefreshRate(hz, _internalDisplayGdiName)) return;

            HighlightBtn(btn, ref _activeDisplayBtn);
            CheckTrayItem(hz <= 60 ? _trayDisplay60 : _trayDisplayMax, _trayDisplay60, _trayDisplayMax);
            SaveState("DisplayHz", hz);
        }

        #endregion

        #region GPU Working Mode (MUX Switch)

        private async void ApplyGpuMode(int mode, PredatorButton btn, string modeName)
        {
            if (_currentGpuMode == mode)
            {
                if (_activeGpuModeBtn != btn)
                    HighlightBtn(btn, ref _activeGpuModeBtn);
                return;
            }

            var result = MessageBox.Show(
                $"Switching GPU Working Mode to {modeName} requires a system restart for hardware firmware changes to take effect.\n\nWould you like to apply this setting and restart your computer now?",
                "Predator Control - GPU Mode Switch",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            if (result == DialogResult.Cancel)
                return;

            bool success = await _wmi.SetGpuModeAsync(mode);
            if (!success)
            {
                MessageBox.Show("Failed to apply GPU mode. Ensure Acer Agent Service is running.", "Predator Control", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _currentGpuMode = mode;
            HighlightBtn(btn, ref _activeGpuModeBtn);
            SaveState("GpuMode", mode);

            ToolStripMenuItem? trayItem = mode switch
            {
                AcerAgentClient.GPU_MODE_OPTIMUS => _trayGpuOptimus,
                AcerAgentClient.GPU_MODE_DISCRETE => _trayGpuDiscrete,
                AcerAgentClient.GPU_MODE_AUTO => _trayGpuAuto,
                _ => null
            };
            if (trayItem != null)
                CheckTrayItem(trayItem, _trayGpuOptimus, _trayGpuDiscrete, _trayGpuAuto);

            if (result == DialogResult.Yes)
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "shutdown.exe",
                        Arguments = "/r /t 5 /c \"Predator Control is restarting the computer to apply GPU MUX switch changes.\"",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                }
                catch { }
            }
            else
            {
                if (_lblGpuRestartNotice != null)
                {
                    _lblGpuRestartNotice.Text = $"Pending restart: {modeName} will be active on next boot";
                    _lblGpuRestartNotice.ForeColor = Color.FromArgb(255, 180, 50);
                }
            }
        }

        #endregion

        #region System Tray

        private void SetupSystemTray()
        {
            try { _trayIcon.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { _trayIcon.Icon = SystemIcons.Application; }

            _trayIcon.ContextMenuStrip = _trayMenu;
            _trayIcon.Text = "Predator Control";
            try { _trayIcon.Visible = true; } catch { }
            _trayIcon.DoubleClick += (s, e) => ShowApp();
            _trayIcon.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowApp(); };
        }

        private void BuildTrayMenu()
        {
            _trayMenu = new ContextMenuStrip();

            var powerMenu = new ToolStripMenuItem("  Power Mode");
            _trayPowerQuiet = new ToolStripMenuItem("Quiet", null, (s, e) => ApplyPowerMode(0x00, _btnQuiet, true));
            _trayPowerBal = new ToolStripMenuItem("Balanced", null, (s, e) => ApplyPowerMode(0x01, _btnBalanced, true));
            _trayPowerPerf = new ToolStripMenuItem("Performance", null, (s, e) => ApplyPowerMode(0x04, _btnPerform, true));
            _trayPowerTurbo = new ToolStripMenuItem("Turbo", null, (s, e) => ApplyPowerMode(0x05, _btnTurbo, true));
            _trayPowerEco = new ToolStripMenuItem("Eco", null, (s, e) => ApplyPowerMode(0x06, _btnEco, true));
            powerMenu.DropDownItems.AddRange([_trayPowerQuiet, _trayPowerBal, _trayPowerPerf, _trayPowerTurbo, _trayPowerEco]);

            var fanMenu = new ToolStripMenuItem("  Fan Mode");
            _trayFanAuto = new ToolStripMenuItem("Auto", null, (s, e) => ApplyFanMode(0x01, _btnAutoFan));
            _trayFanMax = new ToolStripMenuItem("Max", null, (s, e) => ApplyFanMode(0x02, _btnMaxFan));
            _trayFanCustom = new ToolStripMenuItem("Custom", null, (s, e) => ApplyFanMode(0x03, _btnCustomFan));
            fanMenu.DropDownItems.AddRange([_trayFanAuto, _trayFanMax, _trayFanCustom]);

            var displayMenu = new ToolStripMenuItem("  Display (Internal)");
            _trayDisplay60 = new ToolStripMenuItem("60 Hz", null, (s, e) => ApplyDisplayMode(60, _btn60Hz));
            _trayDisplayMax = new ToolStripMenuItem($"{_maxHz} Hz", null, (s, e) => ApplyDisplayMode(_maxHz, _btnMaxHz));
            displayMenu.DropDownItems.AddRange([_trayDisplay60, _trayDisplayMax]);

            var gpuMenu = new ToolStripMenuItem("  GPU Mode (MUX)");
            _trayGpuOptimus = new ToolStripMenuItem("Optimus (Hybrid)", null, (s, e) => ApplyGpuMode(AcerAgentClient.GPU_MODE_OPTIMUS, _btnGpuOptimus, "Optimus (Hybrid)"));
            _trayGpuDiscrete = new ToolStripMenuItem("Discrete GPU", null, (s, e) => ApplyGpuMode(AcerAgentClient.GPU_MODE_DISCRETE, _btnGpuDiscrete, "Discrete GPU Only"));
            _trayGpuAuto = new ToolStripMenuItem("Auto (Advanced Optimus)", null, (s, e) => ApplyGpuMode(AcerAgentClient.GPU_MODE_AUTO, _btnGpuAuto, "Auto (Advanced Optimus)"));
            gpuMenu.DropDownItems.AddRange([_trayGpuOptimus, _trayGpuDiscrete, _trayGpuAuto]);

            var rgbMenu = new ToolStripMenuItem("  Keyboard RGB");
            _trayRgbStatic = new ToolStripMenuItem("Static", null, (s, e) => ApplyRgbModeFromDropdown(0));
            _trayRgbBreathe = new ToolStripMenuItem("Breathing", null, (s, e) => ApplyRgbModeFromDropdown(1));
            _trayRgbNeon = new ToolStripMenuItem("Neon", null, (s, e) => ApplyRgbModeFromDropdown(2));
            _trayRgbWave = new ToolStripMenuItem("Wave", null, (s, e) => ApplyRgbModeFromDropdown(3));
            _trayRgbShift = new ToolStripMenuItem("Shifting", null, (s, e) => ApplyRgbModeFromDropdown(4));
            _trayRgbZoom = new ToolStripMenuItem("Zoom", null, (s, e) => ApplyRgbModeFromDropdown(5));
            _trayRgbMeteor = new ToolStripMenuItem("Meteor", null, (s, e) => ApplyRgbModeFromDropdown(6));
            _trayRgbTwinkle = new ToolStripMenuItem("Twinkling", null, (s, e) => ApplyRgbModeFromDropdown(7));
            _trayRgbOff = new ToolStripMenuItem("Off", null, (s, e) => ApplyRgbModeFromDropdown(8));
            rgbMenu.DropDownItems.AddRange([_trayRgbStatic, _trayRgbBreathe, _trayRgbNeon, _trayRgbWave,
                                            _trayRgbShift, _trayRgbZoom, _trayRgbMeteor, _trayRgbTwinkle, _trayRgbOff]);

            _trayBatteryMenu = new ToolStripMenuItem("  Battery Limit");
            _trayBatteryLimit80 = new ToolStripMenuItem("Limit to 80%", null, (s, e) => ApplyBatteryLimit(true));
            _trayBatteryLimit100 = new ToolStripMenuItem("Full Charge (100%)", null, (s, e) => ApplyBatteryLimit(false));
            _trayBatteryMenu.DropDownItems.AddRange([_trayBatteryLimit80, _trayBatteryLimit100]);

            _trayMenu.Items.Add(powerMenu);
            _trayMenu.Items.Add(fanMenu);
            _trayMenu.Items.Add(displayMenu);
            _trayMenu.Items.Add(gpuMenu);
            _trayMenu.Items.Add(_trayBatteryMenu);
            _trayMenu.Items.Add(rgbMenu);

            var hardwareMenu = new ToolStripMenuItem("  Hardware Features");
            var trayWinKey = new ToolStripMenuItem("Lock Windows Key", null, (s, e) =>
            {
                if (_switchWinKeyLock != null) _switchWinKeyLock.Checked = !_switchWinKeyLock.Checked;
            });
            var trayLcdOverdrive = new ToolStripMenuItem("LCD Overdrive", null, (s, e) =>
            {
                if (_switchLcdOverdrive != null) _switchLcdOverdrive.Checked = !_switchLcdOverdrive.Checked;
            });
            hardwareMenu.DropDownItems.AddRange([trayWinKey, trayLcdOverdrive]);
            _trayMenu.Items.Add(hardwareMenu);

            _trayOverlay = new ToolStripMenuItem("  Gaming Overlay (Ctrl+Shift+O)", null, (s, e) => ToggleGameOverlay())
            {
                CheckOnClick = true
            };
            _trayMenu.Items.Add(_trayOverlay);

            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("Open Dashboard", null, (s, e) => ShowApp());
            _trayMenu.Items.Add("Exit", null, (s, e) => { _isClosing = true; Application.Exit(); });
        }

        #endregion

        #region UI Building & Responsive Layout

        private void BuildUI()
        {
            Controls.Clear();
            _separators.Clear();
            var theme = ThemeManager.Current;
            BackColor = theme.FormBg;
            ForeColor = Color.White;

            int initialW = S(460);
            int workH = Screen.PrimaryScreen?.WorkingArea.Height ?? S(1000);
            ClientSize = new Size(initialW, Math.Max(S(500), Math.Min(S(980), workH - 40)));

            FormBorderStyle = FormBorderStyle.Sizable;
            ControlBox = false;
            Text = string.Empty;
            MinimumSize = new Size(S(420), S(520));
            MaximumSize = new Size(S(700), S(1200));
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            int pad = S(24);
            int gap = S(6);
            int btnH = S(34);
            int y = 0;

            // Title bar
            _pnlTitle = new Panel { Height = S(40), Dock = DockStyle.Top, BackColor = theme.TitleBarBg };
            _pnlTitle.MouseDown += TitleBar_MouseDown;
            Controls.Add(_pnlTitle);

            var picIcon = new PictureBox
            {
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(S(16), S(16)),
                Location = new Point(pad - S(4), S(12)),
                BackColor = Color.Transparent
            };
            try
            {
                var extIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (extIcon != null) picIcon.Image = extIcon.ToBitmap();
            }
            catch { }
            picIcon.MouseDown += TitleBar_MouseDown;
            _pnlTitle.Controls.Add(picIcon);

            _lblTitle = new Label
            {
                Text = "Predator Control",
                ForeColor = Color.White,
                Font = FontTitle,
                AutoSize = true,
                Location = new Point(pad + S(20), S(11)),
                BackColor = Color.Transparent
            };
            _lblTitle.MouseDown += TitleBar_MouseDown;
            _pnlTitle.Controls.Add(_lblTitle);

            var lblClose = new Label
            {
                Text = "●",
                ForeColor = Color.FromArgb(255, 95, 86),
                Font = new Font("Arial", 12f),
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(_pnlTitle.Width - pad - S(4), S(9)),
                Cursor = Cursors.Hand,
                BackColor = Color.Transparent
            };
            var lblMin = new Label
            {
                Text = "●",
                ForeColor = Color.FromArgb(255, 189, 46),
                Font = new Font("Arial", 12f),
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(_pnlTitle.Width - pad - S(24), S(9)),
                Cursor = Cursors.Hand,
                BackColor = Color.Transparent
            };

            lblClose.MouseEnter += (s, e) => lblClose.ForeColor = Color.FromArgb(255, 125, 115);
            lblClose.MouseLeave += (s, e) => lblClose.ForeColor = Color.FromArgb(255, 95, 86);
            lblMin.MouseEnter += (s, e) => lblMin.ForeColor = Color.FromArgb(255, 215, 80);
            lblMin.MouseLeave += (s, e) => lblMin.ForeColor = Color.FromArgb(255, 189, 46);

            lblClose.Click += (s, e) => Close();
            lblMin.Click += (s, e) => WindowState = FormWindowState.Minimized;

            _pnlTitle.Controls.Add(lblClose);
            _pnlTitle.Controls.Add(lblMin);

            // Main Content Panel
            _contentPanel = new DarkScrollPanel
            {
                Dock = DockStyle.Fill,
                BackColor = theme.FormBg
            };
            _contentPanel.SetDpiScale(_dpiScale);
            Controls.Add(_contentPanel);
            _contentPanel.BringToFront();

            int contentW = ClientSize.Width - pad * 2;

            y = S(16);

            // TELEMETRY SENSORS
            MakeLabel("CPU:", pad, y, FontBody, Color.FromArgb(120, 120, 135));
            _lblCpuTemp = MakeLabel("--°C", pad + S(34), y, FontBodyBold, Color.White);

            _lblGpuHdr = MakeLabel("GPU:", ClientSize.Width / 2 + S(10), y, FontBody, Color.FromArgb(120, 120, 135));
            _lblGpuTemp = MakeLabel("--°C", ClientSize.Width / 2 + S(46), y, FontBodyBold, Color.White);

            y += S(24);
            MakeLabel("CPU FAN:", pad, y, FontBody, Color.FromArgb(120, 120, 135));
            _lblCpuRpm = MakeLabel("-- RPM", pad + S(64), y, FontBodyBold, Color.White);

            _lblGpuFanHdr = MakeLabel("GPU FAN:", ClientSize.Width / 2 + S(10), y, FontBody, Color.FromArgb(120, 120, 135));
            _lblGpuRpm = MakeLabel("-- RPM", ClientSize.Width / 2 + S(74), y, FontBodyBold, Color.White);

            y += S(24);
            MakeLabel("Fan speed:", pad, y, FontBody, Color.FromArgb(120, 120, 135));
            _lblFanStatus = MakeLabel("Auto", pad + S(74), y, FontBodyBold, Color.White);

            _lblPowerHdr = MakeLabel("Power:", ClientSize.Width / 2 + S(10), y, FontBody, Color.FromArgb(120, 120, 135));
            _lblPowerStatus = MakeLabel("Checking...", ClientSize.Width / 2 + S(56), y, FontBodyBold, Color.White);

            y += S(30);
            AddSeparator(y);

            // THEME SWITCHER
            y += S(18);
            MakeSectionHeader("APP THEME", pad, y);

            y += S(24);
            int themeBtnW = (contentW - 2 * gap) / 3;
            _btnThemeTeal = MakeButton("Teal", pad, y, themeBtnW, btnH);
            _btnThemeCrimson = MakeButton("Crimson", pad + themeBtnW + gap, y, themeBtnW, btnH);
            _btnThemeOled = MakeButton("OLED Black", pad + (themeBtnW + gap) * 2, y, themeBtnW, btnH);

            _btnThemeTeal.Click += (s, e) => ThemeManager.SetTheme(ThemeMode.PredatorTeal);
            _btnThemeCrimson.Click += (s, e) => ThemeManager.SetTheme(ThemeMode.NitroCrimson);
            _btnThemeOled.Click += (s, e) => ThemeManager.SetTheme(ThemeMode.OledBlack);

            y += btnH + S(16);
            AddSeparator(y);

            // POWER MODE
            y += S(18);
            MakeSectionHeader("POWER MODE", pad, y);

            y += S(24);
            int btnW = (contentW - 4 * gap) / 5;
            _btnQuiet = MakeButton("Quiet", pad, y, btnW, btnH);
            _btnBalanced = MakeButton("Balanced", pad + (btnW + gap), y, btnW, btnH);
            _btnPerform = MakeButton("Perf", pad + (btnW + gap) * 2, y, btnW, btnH);
            _btnTurbo = MakeButton("Turbo", pad + (btnW + gap) * 3, y, btnW, btnH);
            _btnEco = MakeButton("Eco", pad + (btnW + gap) * 4, y, btnW, btnH);

            _btnQuiet.Click += (s, e) => ApplyPowerMode(0x00, _btnQuiet);
            _btnBalanced.Click += (s, e) => ApplyPowerMode(0x01, _btnBalanced);
            _btnPerform.Click += (s, e) => ApplyPowerMode(0x04, _btnPerform);
            _btnTurbo.Click += (s, e) => ApplyPowerMode(0x05, _btnTurbo);
            _btnEco.Click += (s, e) => ApplyPowerMode(0x06, _btnEco);

            y += btnH + S(14);
            int profileDropW = (contentW - gap) / 2;
            _lblAcProfileHdr = MakeLabel("ON AC POWER:", pad, y, FontSectionHeader, Color.FromArgb(120, 120, 135));
            _lblBatteryProfileHdr = MakeLabel("ON BATTERY:", pad + profileDropW + gap, y, FontSectionHeader, Color.FromArgb(120, 120, 135));

            y += S(20);
            _cboAcProfile = new PredatorDropDown { Location = new Point(pad, y), Size = new Size(profileDropW, S(30)) };
            _cboAcProfile.Items.AddRange(["Don't Change", "Quiet", "Balanced", "Perf", "Turbo"]);
            _cboAcProfile.SelectedIndex = 0;
            _contentPanel.Controls.Add(_cboAcProfile);

            _cboBatteryProfile = new PredatorDropDown { Location = new Point(pad + profileDropW + gap, y), Size = new Size(profileDropW, S(30)) };
            _cboBatteryProfile.Items.AddRange(["Don't Change", "Quiet", "Balanced", "Eco"]);
            _cboBatteryProfile.SelectedIndex = 0;
            _contentPanel.Controls.Add(_cboBatteryProfile);

            _cboAcProfile.SelectedIndexChanged += (s, e) =>
            {
                SaveState("AutoPowerAC", _cboAcProfile.SelectedIndex);
                if (_isPluggedIn == true) ApplyPowerRules(true);
            };
            _cboBatteryProfile.SelectedIndexChanged += (s, e) =>
            {
                SaveState("AutoPowerBattery", _cboBatteryProfile.SelectedIndex);
                if (_isPluggedIn == false) ApplyPowerRules(false);
            };

            // FAN CONTROL
            y += S(30) + S(20);
            MakeSectionHeader("FAN CONTROL", pad, y);

            y += S(24);
            int fanBtnW = (contentW - 2 * gap) / 3;
            _btnAutoFan = MakeButton("Auto", pad, y, fanBtnW, btnH);
            _btnMaxFan = MakeButton("Max", pad + (fanBtnW + gap), y, fanBtnW, btnH);
            _btnCustomFan = MakeButton("Custom", pad + (fanBtnW + gap) * 2, y, fanBtnW, btnH);

            _btnAutoFan.Click += (s, e) => ApplyFanMode(0x01, _btnAutoFan);
            _btnMaxFan.Click += (s, e) => ApplyFanMode(0x02, _btnMaxFan);
            _btnCustomFan.Click += (s, e) => ApplyFanMode(0x03, _btnCustomFan);

            y += btnH + S(12);
            int fanSliderW = (contentW - gap) / 2;
            _lblCpuFanSpeedHdr = MakeLabel("CPU FAN: 50%", pad, y, FontSectionHeader, Color.FromArgb(120, 120, 135));
            _lblGpuFanSpeedHdr = MakeLabel("GPU FAN: 50%", pad + fanSliderW + gap, y, FontSectionHeader, Color.FromArgb(120, 120, 135));
            _lblCpuFanSpeedHdr.Visible = false;
            _lblGpuFanSpeedHdr.Visible = false;

            y += S(24);
            _cpuFanSlider = new PredatorSlider
            {
                Location = new Point(pad, y),
                Size = new Size(fanSliderW, S(28)),
                Minimum = 10, Maximum = 100, Value = 50,
                Visible = false
            };
            _gpuFanSlider = new PredatorSlider
            {
                Location = new Point(pad + fanSliderW + gap, y),
                Size = new Size(fanSliderW, S(28)),
                Minimum = 10, Maximum = 100, Value = 50,
                Visible = false
            };
            _contentPanel.Controls.Add(_cpuFanSlider);
            _contentPanel.Controls.Add(_gpuFanSlider);

            _cpuFanSlider.ValueChanged += (s, e) => _lblCpuFanSpeedHdr.Text = $"CPU FAN: {_cpuFanSlider.Value}%";
            _gpuFanSlider.ValueChanged += (s, e) => _lblGpuFanSpeedHdr.Text = $"GPU FAN: {_gpuFanSlider.Value}%";

            _cpuFanSlider.ValueCommitted += (s, e) =>
            {
                _wmi.SetCpuFanSpeed((byte)_cpuFanSlider.Value);
                SaveState("FanSpeedCpu", _cpuFanSlider.Value);
            };
            _gpuFanSlider.ValueCommitted += (s, e) =>
            {
                _wmi.SetGpuFanSpeed((byte)_gpuFanSlider.Value);
                SaveState("FanSpeedGpu", _gpuFanSlider.Value);
            };

            y += S(28) + S(8);
            int subBtnW = (contentW - gap) / 2;
            _btnFixedSpeed = MakeButton("Fixed Speed", pad, y, subBtnW, btnH);
            _btnFanCurve = MakeButton("Curve", pad + subBtnW + gap, y, subBtnW, btnH);
            _btnFixedSpeed.Visible = false;
            _btnFanCurve.Visible = false;

            _btnFixedSpeed.Click += (s, e) =>
            {
                _fanCurveEnabled = false;
                _lastCurveCpuSpeed = -1;
                _lastCurveGpuSpeed = -1;
                HighlightBtn(_btnFixedSpeed, ref _activeCustomSubBtn);
                SaveState("FanCurveEnabled", 0);

                if (_fanCurveForm != null && !_fanCurveForm.IsDisposed)
                    _fanCurveForm.Close();

                _cpuFanSlider.Enabled = true;
                _gpuFanSlider.Enabled = true;

                _wmi.SetCpuFanSpeed((byte)_cpuFanSlider.Value);
                _wmi.SetGpuFanSpeed((byte)_gpuFanSlider.Value);
            };

            _btnFanCurve.Click += (s, e) =>
            {
                HighlightBtn(_btnFanCurve, ref _activeCustomSubBtn);
                _cpuFanSlider.Enabled = false;
                _gpuFanSlider.Enabled = false;
                OpenFanCurveEditor();
            };

            // DISPLAY REFRESH RATE
            y += btnH + S(20);
            string displayTitle = "NOTEBOOK DISPLAY REFRESH RATE";
            _lblDisplayHdr = MakeLabel(displayTitle, pad, y, FontSectionHeader, Color.FromArgb(120, 120, 135));

            y += S(24);
            int dispBtnW = (contentW - gap) / 2;
            _btn60Hz = MakeButton("60 Hz", pad, y, dispBtnW, btnH);
            _btnMaxHz = MakeButton($"{_maxHz} Hz (Max)", pad + dispBtnW + gap, y, dispBtnW, btnH);

            _btn60Hz.Click += (s, e) => ApplyDisplayMode(60, _btn60Hz);
            _btnMaxHz.Click += (s, e) => ApplyDisplayMode(_maxHz, _btnMaxHz);

            y += btnH + S(12);
            int switchH = S(30);
            _lblLcdOverdrive = MakeLabel("LCD Overdrive (3ms Response Boost)", pad, y, FontBody, Color.FromArgb(120, 120, 135));
            CenterV(_lblLcdOverdrive, y, switchH);

            _switchLcdOverdrive = new PredatorToggle
            {
                Location = new Point(ClientSize.Width - pad - S(48), y),
                Size = new Size(S(48), switchH)
            };
            _contentPanel.Controls.Add(_switchLcdOverdrive);

            _switchLcdOverdrive.CheckedChanged += (s, e) =>
            {
                bool enable = _switchLcdOverdrive.Checked;
                SaveState("LcdOverdrive", enable ? 1 : 0);
                Task.Run(() =>
                {
                    try { _wmi.SetLcdOverdrive(enable); } catch { }
                });
            };

            y += switchH + S(16);
            AddSeparator(y);

            // GPU WORKING MODE (MUX SWITCH)
            y += S(18);
            _lblGpuModeHdr = MakeLabel("GPU WORKING MODE (MUX SWITCH)", pad, y, FontSectionHeader, Color.FromArgb(120, 120, 135));

            y += S(24);
            int gpuBtnW = (contentW - 2 * gap) / 3;
            _btnGpuOptimus = MakeButton("Optimus", pad, y, gpuBtnW, btnH);
            _btnGpuDiscrete = MakeButton("Discrete GPU", pad + gpuBtnW + gap, y, gpuBtnW, btnH);
            _btnGpuAuto = MakeButton("Auto (Adv)", pad + (gpuBtnW + gap) * 2, y, gpuBtnW, btnH);

            _btnGpuOptimus.Click += (s, e) => ApplyGpuMode(AcerAgentClient.GPU_MODE_OPTIMUS, _btnGpuOptimus, "Optimus (Hybrid)");
            _btnGpuDiscrete.Click += (s, e) => ApplyGpuMode(AcerAgentClient.GPU_MODE_DISCRETE, _btnGpuDiscrete, "Discrete GPU Only");
            _btnGpuAuto.Click += (s, e) => ApplyGpuMode(AcerAgentClient.GPU_MODE_AUTO, _btnGpuAuto, "Auto (Advanced Optimus)");

            // Default initial selection to Optimus so the button is highlighted on startup
            HighlightBtn(_btnGpuOptimus, ref _activeGpuModeBtn);
            _currentGpuMode = AcerAgentClient.GPU_MODE_OPTIMUS;

            y += btnH + S(8);
            _lblGpuRestartNotice = MakeLabel("Requires system restart to take effect in firmware", pad, y, FontBody, Color.FromArgb(120, 120, 135));

            y += S(22);
            AddSeparator(y);

            // EXTERNAL DISPLAY(S)
            y += S(18);
            _lblExternalDisplayHdr = MakeLabel("EXTERNAL DISPLAY(S)", pad, y, FontSectionHeader, Color.FromArgb(120, 120, 135));

            y += S(24);
            _pnlExternalMonitors = new Panel
            {
                Location = new Point(pad, y),
                Width = contentW,
                Height = 0,
                BackColor = Color.Transparent
            };
            _contentPanel.Controls.Add(_pnlExternalMonitors);

            // Do initial detection and render the section (or hide it if no monitors)
            RefreshExternalMonitorSection();

            // Reserve y-space that RefreshExternalMonitorSection occupies
            y += _pnlExternalMonitors.Height + S(8);

            // BATTERY CHARGE LIMIT
            y += btnH + S(28);
            MakeSectionHeader("BATTERY CHARGE LIMIT", pad, y);

            y += S(24);
            switchH = S(30);
            _lblBatteryStatus = MakeLabel("Full Charge (100%)", pad, y, FontBody, Color.FromArgb(120, 120, 135));
            CenterV(_lblBatteryStatus, y, switchH);

            _switchBatteryLimit = new PredatorSwitch
            {
                Location = new Point(ClientSize.Width - pad - S(48), y),
                Size = new Size(S(48), switchH)
            };
            _contentPanel.Controls.Add(_switchBatteryLimit);

            _switchBatteryLimit.CheckedChanged += (s, e) => ApplyBatteryLimit(_switchBatteryLimit.Checked);

            y += switchH + S(12);
            _lblStartupStatus = MakeLabel("Start with Windows", pad, y, FontBody, Color.FromArgb(120, 120, 135));
            CenterV(_lblStartupStatus, y, switchH);

            MigrateLegacyStartup();

            _switchStartWithWindows = new PredatorToggle
            {
                Location = new Point(ClientSize.Width - pad - S(48), y),
                Size = new Size(S(48), switchH),
                Checked = IsStartupEnabled()
            };
            _contentPanel.Controls.Add(_switchStartWithWindows);

            _switchStartWithWindows.CheckedChanged += (s, e) =>
            {
                if (_suppressStartupToggle) return;

                bool wanted = _switchStartWithWindows.Checked;
                if (SetStartupEnabled(wanted) && IsStartupEnabled() == wanted) return;

                _suppressStartupToggle = true;
                _switchStartWithWindows.Checked = !wanted;
                _suppressStartupToggle = false;

                MessageBox.Show(this,
                    wanted
                        ? "Could not register Predator Control to start with Windows."
                        : "Could not remove the Predator Control startup task.",
                    "Start with Windows", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };

            // KEYBOARD RGB LIGHTING (3-ZONE / 4-ZONE & MODES)
            y += switchH + S(28);
            MakeSectionHeader("KEYBOARD RGB LIGHTING", pad, y);

            y += S(24);
            int dropH = S(34);
            _rgbDropDown = new PredatorDropDown { Location = new Point(pad, y), Size = new Size(contentW, dropH) };
            foreach (var name in RgbModeNames) _rgbDropDown.Items.Add(name);
            _rgbDropDown.SelectedIndex = 3; // Wave
            _contentPanel.Controls.Add(_rgbDropDown);

            // 4-Zone Keyboard Lighting Controls
            y += dropH + S(16);
            MakeLabel("KEYBOARD 4-ZONE CONFIG:", pad, y, FontSectionHeader, Color.FromArgb(120, 120, 135));

            y += S(20);
            int zoneBtnW = (contentW - 4 * gap) / 5;
            _btnAllZones = MakeButton("All Zones", pad, y, zoneBtnW, S(30));
            _btnAllZones.IsActive = true;
            _btnAllZones.ColorIndicator = _currentZoneColors[0];
            _btnAllZones.Click += (s, e) => SelectZone(-1);

            string[] zoneLabels = { "Zone 1", "Zone 2", "Zone 3", "Zone 4" };
            for (int z = 0; z < 4; z++)
            {
                int zIdx = z;
                _btnZones[z] = MakeButton(zoneLabels[z], pad + (zoneBtnW + gap) * (z + 1), y, zoneBtnW, S(30));
                _btnZones[z].ColorIndicator = _currentZoneColors[z];
                _btnZones[z].Click += (s, e) => SelectZone(zIdx);
            }

            // Quick Preset Color Chips
            y += S(30) + S(16);
            MakeLabel("QUICK COLOR PRESETS:", pad, y, FontSectionHeader, Color.FromArgb(120, 120, 135));

            y += S(20);
            int preW = (contentW - 5 * gap) / 6;
            for (int p = 0; p < 6; p++)
            {
                int pIdx = p;
                _btnPresetColors[p] = MakeButton(PresetColorNames[p], pad + (preW + gap) * p, y, preW, S(28));
                _btnPresetColors[p].ColorIndicator = PresetColors[p]; // Live neon bottom strip
                _btnPresetColors[p].Click += (s, e) => ApplyPresetColor(PresetColors[pIdx]);
            }

            y += S(28) + S(12);
            _btnCustomColor = MakeButton("🎨  Custom Color...", pad, y, contentW, btnH);
            _btnCustomColor.Click += (s, e) =>
            {
                if (_selectedZone >= 0 && _selectedZone < 4)
                    _colorPicker.Color = _currentZoneColors[_selectedZone];
                else
                    _colorPicker.Color = _currentZoneColors[0];

                if (_colorPicker.ShowDialog() == DialogResult.OK)
                {
                    ApplyPresetColor(_colorPicker.Color);
                }
            };

            y += btnH + S(16);
            _lblBrightHdr = MakeLabel("BRIGHTNESS: 100%", pad, y, FontSectionHeader, Color.FromArgb(120, 120, 135));
            _lblSpeedHdr = MakeLabel("EFFECT SPEED: 50%", ClientSize.Width / 2 + S(10), y, FontSectionHeader, Color.FromArgb(120, 120, 135));

            y += S(24);
            int sliderW = (contentW - gap * 4) / 2;
            _brightnessSlider = new PredatorSlider { Location = new Point(pad, y), Size = new Size(sliderW, S(28)), Minimum = 0, Maximum = 100, Value = 100 };
            _contentPanel.Controls.Add(_brightnessSlider);

            _speedSlider = new PredatorSlider { Location = new Point(ClientSize.Width / 2 + S(10), y), Size = new Size(sliderW, S(28)), Minimum = 1, Maximum = 100, Value = 50 };
            _contentPanel.Controls.Add(_speedSlider);

            _brightnessSlider.ValueChanged += (s, e) =>
            {
                _lblBrightHdr.Text = $"BRIGHTNESS: {_brightnessSlider.Value}%";
                DebounceHelper.Debounce("RgbBrightnessLive", () =>
                {
                    _wmi.SetBrightness((byte)_brightnessSlider.Value);
                }, 40);
            };
            _brightnessSlider.ValueCommitted += (s, e) =>
            {
                _wmi.SetBrightness((byte)_brightnessSlider.Value);
                SaveState("Brightness", _brightnessSlider.Value);
            };

            _speedSlider.ValueChanged += (s, e) =>
            {
                _lblSpeedHdr.Text = $"EFFECT SPEED: {_speedSlider.Value}%";
                DebounceHelper.Debounce("RgbSpeedLive", () =>
                {
                    _wmi.SetSpeed(GetMappedSpeed());
                }, 40);
            };
            _speedSlider.ValueCommitted += (s, e) =>
            {
                _wmi.SetSpeed(GetMappedSpeed());
                SaveState("RGB_Speed", _speedSlider.Value);
            };

            _rgbDropDown.SelectedIndexChanged += (s, e) =>
            {
                if (_isGameSyncOverriding) return;
                int mode = _rgbDropDown.SelectedIndex;
                ApplyRgbModeFromDropdown(mode);
            };

            UpdateRgbControlsState(_rgbDropDown.SelectedIndex);

            y += S(28) + S(12);
            _lblBacklight30s = MakeLabel("Backlight 30-Sec Idle Sleep", pad, y, FontBody, Color.FromArgb(120, 120, 135));
            CenterV(_lblBacklight30s, y, switchH);

            _switchBacklight30s = new PredatorToggle
            {
                Location = new Point(ClientSize.Width - pad - S(48), y),
                Size = new Size(S(48), switchH)
            };
            _contentPanel.Controls.Add(_switchBacklight30s);

            _switchBacklight30s.CheckedChanged += (s, e) =>
            {
                bool enable = _switchBacklight30s.Checked;
                SaveState("Backlight30s", enable ? 1 : 0);
                Task.Run(() =>
                {
                    try { _wmi.SetBacklight30s(enable); } catch { }
                });
            };

            // SYSTEM & HARDWARE CONTROLS
            y += switchH + S(22);
            AddSeparator(y);

            y += S(18);
            MakeSectionHeader("SYSTEM & HARDWARE CONTROLS", pad, y);

            // In-Game Gaming Overlay HUD
            y += S(24);
            _lblOverlay = MakeLabel("Gaming Overlay HUD (Ctrl+Shift+Alt+O)", pad, y, FontBody, Color.FromArgb(120, 120, 135));
            CenterV(_lblOverlay, y, switchH);

            _switchOverlay = new PredatorToggle
            {
                Location = new Point(ClientSize.Width - pad - S(48), y),
                Size = new Size(S(48), switchH)
            };
            _contentPanel.Controls.Add(_switchOverlay);
            _switchOverlay.CheckedChanged += (s, e) => SetOverlayVisible(_switchOverlay.Checked);

            // Overlay Display Mode (Light, Default, Full)
            y += switchH + S(12);
            MakeLabel("DISPLAY MODE:", pad, y, FontSectionHeader, Color.FromArgb(120, 120, 135));

            y += S(20);
            int modeBtnW = (contentW - gap * 2) / 3;
            _btnOverlayLight = MakeButton("Light", pad, y, modeBtnW, btnH);
            _btnOverlayDefault = MakeButton("Default", pad + modeBtnW + gap, y, modeBtnW, btnH);
            _btnOverlayFull = MakeButton("Full", pad + (modeBtnW + gap) * 2, y, modeBtnW, btnH);

            _btnOverlayLight.Click += (s, e) => { _overlayForm?.SetMode(OverlayMode.Light); UpdateOverlayUI(); };
            _btnOverlayDefault.Click += (s, e) => { _overlayForm?.SetMode(OverlayMode.Default); UpdateOverlayUI(); };
            _btnOverlayFull.Click += (s, e) => { _overlayForm?.SetMode(OverlayMode.Full); UpdateOverlayUI(); };

            // Overlay Scale Slider (50% to 300%)
            y += btnH + S(14);
            int initScale = _overlayForm?.ScalePercent ?? 100;
            _lblOverlayScaleHdr = MakeLabel($"OVERLAY SCALE: {initScale}%", pad, y, FontSectionHeader, Color.FromArgb(120, 120, 135));

            y += S(20);
            _sliderOverlayScale = new PredatorSlider
            {
                Location = new Point(pad, y),
                Size = new Size(contentW, S(28)),
                Minimum = 50,
                Maximum = 300,
                Value = initScale
            };
            _contentPanel.Controls.Add(_sliderOverlayScale);
            _sliderOverlayScale.ValueChanged += (s, e) =>
            {
                _lblOverlayScaleHdr.Text = $"OVERLAY SCALE: {_sliderOverlayScale.Value}%";
                _overlayForm?.SetScale(_sliderOverlayScale.Value);
            };

            y += S(34);
            UpdateOverlayUI();

            // Windows & Menu Key Lock
            y += switchH + S(12);
            _lblWinKeyLock = MakeLabel("Lock Windows & Menu Keys", pad, y, FontBody, Color.FromArgb(120, 120, 135));
            CenterV(_lblWinKeyLock, y, switchH);

            _switchWinKeyLock = new PredatorToggle
            {
                Location = new Point(ClientSize.Width - pad - S(48), y),
                Size = new Size(S(48), switchH)
            };
            _contentPanel.Controls.Add(_switchWinKeyLock);

            _switchWinKeyLock.CheckedChanged += (s, e) =>
            {
                bool lockKeys = _switchWinKeyLock.Checked;
                PredatorKeyHook.SetWinKeyLocked(lockKeys);
                SaveState("LockWinKey", lockKeys ? 1 : 0);
                Task.Run(() =>
                {
                    try { _wmi.SetWinKeyLock(lockKeys); } catch { }
                });
            };

            // GAME SYNC
            y += switchH + S(22);
            AddSeparator(y);
            y += S(18);
            MakeSectionHeader("GAME SYNC", pad, y);

            y += S(24);
            int syncSwitchH = S(30);
            _lblGameSyncStatus = MakeLabel("Disabled", pad, y, FontBody, Color.FromArgb(120, 120, 135));
            CenterV(_lblGameSyncStatus, y, syncSwitchH);

            _switchGameSync = new PredatorToggle
            {
                Location = new Point(ClientSize.Width - pad - S(48), y),
                Size = new Size(S(48), syncSwitchH)
            };
            _contentPanel.Controls.Add(_switchGameSync);

            _switchGameSync.CheckedChanged += (s, e) =>
            {
                _gameSync.IsEnabled = _switchGameSync.Checked;
                _lblGameSyncStatus.Text = _switchGameSync.Checked ? "Active — Monitoring" : "Disabled";
            };

            y += syncSwitchH + S(10);
            _btnConfigureGames = MakeButton("🎮  Configure Executables", pad, y, contentW, btnH);
            _btnConfigureGames.Click += (s, e) =>
            {
                using var form = new GameSyncForm(_gameSync, _maxHz);
                form.ShowDialog(this);
            };

            // UPDATES
            y += btnH + S(24);
            AddSeparator(y);

            y += S(20);
            MakeSectionHeader("UPDATES", pad, y);

            y += S(24);
            int updBtnH = S(30), updBtnW = S(160);
            var lblVersion = MakeLabel($"Version {Updater.CurrentText}", pad, y, FontBody, Color.FromArgb(120, 120, 135));
            CenterV(lblVersion, y, updBtnH);

            _btnCheckUpdates = MakeButton("⬇  Check for Updates", ClientSize.Width - pad - updBtnW, y, updBtnW, updBtnH);
            _btnCheckUpdates.Click += async (s, e) => await CheckForUpdatesAsync();

            _contentPanel.AutoScrollMinSize = new Size(0, y + updBtnH + S(50));
        }

        private void SelectZone(int zoneIndex)
        {
            _selectedZone = zoneIndex;
            if (_btnAllZones != null) _btnAllZones.IsActive = (zoneIndex == -1);
            for (int z = 0; z < 4; z++)
            {
                if (_btnZones[z] != null) _btnZones[z].IsActive = (zoneIndex == z);
            }

            Color activeColor = (zoneIndex >= 0 && zoneIndex < 4) ? _currentZoneColors[zoneIndex] : _currentZoneColors[0];
            _colorPicker.Color = activeColor;
            if (_btnCustomColor != null)
                _btnCustomColor.ColorIndicator = activeColor;
        }

        private void ApplyPresetColor(Color c)
        {
            _colorPicker.Color = c;

            // Only switch to Static if currently in a mode that doesn't support custom colors (Neon, Wave, Off)
            bool wasUnsupported = (_rgbDropDown.SelectedIndex == 2 || _rgbDropDown.SelectedIndex == 3 || _rgbDropDown.SelectedIndex == 8);
            int mode = wasUnsupported ? 0 : _rgbDropDown.SelectedIndex;

            if (mode == 0) // Static: individual physical zones supported
            {
                if (_selectedZone == -1)
                {
                    // Apply to ALL 4 zones
                    for (int i = 0; i < 4; i++)
                    {
                        _currentZoneColors[i] = c;
                        if (_btnZones[i] != null)
                            _btnZones[i].ColorIndicator = c;
                        SaveState($"ZoneColor_{i}", c.ToArgb());
                    }
                    if (_btnAllZones != null)
                        _btnAllZones.ColorIndicator = c;

                    _wmi.SetKeyboardColor(c, 0);
                }
                else if (_selectedZone >= 0 && _selectedZone < 4)
                {
                    // Apply to specific zone
                    _currentZoneColors[_selectedZone] = c;
                    if (_btnZones[_selectedZone] != null)
                        _btnZones[_selectedZone].ColorIndicator = c;

                    SaveState($"ZoneColor_{_selectedZone}", c.ToArgb());
                    _wmi.SetSingleZoneColor(_selectedZone, c, 0);
                }
            }
            else // Animated modes (Breathing, Shifting, Zoom, Meteor, Twinkling)
            {
                // In animation modes, the hardware animates the entire keyboard in the selected color.
                // Preserves user's saved individual ZoneColor_0..3 without overwriting them.
                byte bright = (byte)_brightnessSlider.Value;
                byte speed = GetMappedSpeed();
                _wmi.SetRgbMode(mode, c.R, c.G, c.B, bright, speed, (byte)_wmi.Direction);
            }

            if (wasUnsupported && _rgbDropDown.SelectedIndex != 0)
            {
                _rgbDropDown.SelectedIndex = 0;
            }

            if (_btnCustomColor != null)
                _btnCustomColor.ColorIndicator = c;

            SaveState("RGB_R", c.R);
            SaveState("RGB_G", c.G);
            SaveState("RGB_B", c.B);
        }

        private void UpdateRgbControlsState(int mode)
        {
            // Static (0), Breathing (1), Shifting (4), Zoom (5), Meteor (6), and Twinkling (7) support custom color customization.
            // Only Neon (2), Wave (3), and Off (8) do not support custom colors.
            bool colorsEnabled = (mode != 2 && mode != 3 && mode != 8);

            // Individual zones are ONLY supported in Static mode (mode 0).
            // In animation modes, the hardware firmware animates the entire keyboard with a single unified color.
            bool zonesEnabled = (mode == 0);

            if (_btnAllZones != null)
            {
                _btnAllZones.Enabled = colorsEnabled;
                if (!zonesEnabled) _btnAllZones.IsActive = true;
            }

            for (int z = 0; z < 4; z++)
            {
                if (_btnZones[z] != null)
                {
                    _btnZones[z].Enabled = zonesEnabled;
                    if (!zonesEnabled) _btnZones[z].IsActive = false;
                }
            }

            if (!zonesEnabled)
            {
                _selectedZone = -1; // Automatically lock selection to All Zones for animated modes
            }

            for (int i = 0; i < _btnPresetColors.Length; i++)
            {
                if (_btnPresetColors[i] != null)
                    _btnPresetColors[i].Enabled = colorsEnabled;
            }

            if (_btnCustomColor != null)
                _btnCustomColor.Enabled = colorsEnabled;

            if (_speedSlider != null)
                _speedSlider.Enabled = (mode != 0 && mode != 8);
        }

        private void MakeSectionHeader(string label, int x, int y)
        {
            MakeLabel(label, x, y, FontSectionHeader, Color.FromArgb(120, 120, 135));
        }

        private Label MakeLabelIn(Control parent, string text, int x, int y, Font font, Color color)
        {
            var lbl = new Label
            {
                Text = text, Location = new Point(x, y), AutoSize = true, Font = font, ForeColor = color, BackColor = Color.Transparent
            };
            parent.Controls.Add(lbl);
            return lbl;
        }

        private Label MakeLabel(string text, int x, int y, Font font, Color color) => MakeLabelIn(_contentPanel, text, x, y, font, color);

        private PredatorButton MakeButtonIn(Control parent, string text, int x, int y, int width, int height)
        {
            var btn = new PredatorButton { Text = text, Location = new Point(x, y), Size = new Size(width, height) };
            parent.Controls.Add(btn);
            return btn;
        }

        private PredatorButton MakeButton(string text, int x, int y, int width, int height) => MakeButtonIn(_contentPanel, text, x, y, width, height);

        private void AddSeparator(int y)
        {
            int pad = S(24);
            var sep = new Panel
            {
                Location = new Point(pad, y),
                Size = new Size(ClientSize.Width - pad * 2, 1),
                BackColor = ThemeManager.Current.Separator
            };
            _separators.Add(sep);
            _contentPanel.Controls.Add(sep);
        }

        private void CenterV(Label lbl, int controlY, int controlH)
        {
            lbl.Location = new Point(lbl.Left, controlY + (controlH - lbl.Height) / 2);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            if (_contentPanel != null && _lblTitle != null)
            {
                int pad = S(24);
                int gap = S(6);
                int contentW = Math.Max(S(350), ClientSize.Width - pad * 2);

                // Update separators
                foreach (var sep in _separators)
                    sep.Width = contentW;

                // Telemetry right-column items
                int midX = ClientSize.Width / 2;
                if (_lblGpuHdr != null) _lblGpuHdr.Left = midX + S(10);
                if (_lblGpuTemp != null) _lblGpuTemp.Left = midX + S(46);
                if (_lblGpuFanHdr != null) _lblGpuFanHdr.Left = midX + S(10);
                if (_lblGpuRpm != null) _lblGpuRpm.Left = midX + S(74);
                if (_lblPowerHdr != null) _lblPowerHdr.Left = midX + S(10);
                if (_lblPowerStatus != null) _lblPowerStatus.Left = midX + S(56);

                // Theme switcher buttons
                int themeBtnW = (contentW - 2 * gap) / 3;
                if (_btnThemeTeal != null) _btnThemeTeal.Width = themeBtnW;
                if (_btnThemeCrimson != null) { _btnThemeCrimson.Left = pad + themeBtnW + gap; _btnThemeCrimson.Width = themeBtnW; }
                if (_btnThemeOled != null) { _btnThemeOled.Left = pad + (themeBtnW + gap) * 2; _btnThemeOled.Width = themeBtnW; }

                // Power mode buttons
                int btnW = (contentW - 4 * gap) / 5;
                if (_btnQuiet != null) _btnQuiet.Width = btnW;
                if (_btnBalanced != null) { _btnBalanced.Left = pad + (btnW + gap); _btnBalanced.Width = btnW; }
                if (_btnPerform != null) { _btnPerform.Left = pad + (btnW + gap) * 2; _btnPerform.Width = btnW; }
                if (_btnTurbo != null) { _btnTurbo.Left = pad + (btnW + gap) * 3; _btnTurbo.Width = btnW; }
                if (_btnEco != null) { _btnEco.Left = pad + (btnW + gap) * 4; _btnEco.Width = btnW; }

                // AC / Battery Profile dropdowns
                int profileDropW = (contentW - gap) / 2;
                if (_lblBatteryProfileHdr != null) _lblBatteryProfileHdr.Left = pad + profileDropW + gap;
                if (_cboAcProfile != null) _cboAcProfile.Width = profileDropW;
                if (_cboBatteryProfile != null) { _cboBatteryProfile.Left = pad + profileDropW + gap; _cboBatteryProfile.Width = profileDropW; }

                // Fan mode buttons
                int fanBtnW = (contentW - 2 * gap) / 3;
                if (_btnAutoFan != null) _btnAutoFan.Width = fanBtnW;
                if (_btnMaxFan != null) { _btnMaxFan.Left = pad + (fanBtnW + gap); _btnMaxFan.Width = fanBtnW; }
                if (_btnCustomFan != null) { _btnCustomFan.Left = pad + (fanBtnW + gap) * 2; _btnCustomFan.Width = fanBtnW; }

                // Fan sliders
                int fanSliderW = (contentW - gap) / 2;
                if (_lblGpuFanSpeedHdr != null) _lblGpuFanSpeedHdr.Left = pad + fanSliderW + gap;
                if (_cpuFanSlider != null) _cpuFanSlider.Width = fanSliderW;
                if (_gpuFanSlider != null) { _gpuFanSlider.Left = pad + fanSliderW + gap; _gpuFanSlider.Width = fanSliderW; }

                // Custom fan sub buttons
                int subBtnW = (contentW - gap) / 2;
                if (_btnFixedSpeed != null) _btnFixedSpeed.Width = subBtnW;
                if (_btnFanCurve != null) { _btnFanCurve.Left = pad + subBtnW + gap; _btnFanCurve.Width = subBtnW; }

                // Display refresh rate buttons
                int dispBtnW = (contentW - gap) / 2;
                if (_btn60Hz != null) _btn60Hz.Width = dispBtnW;
                if (_btnMaxHz != null) { _btnMaxHz.Left = pad + dispBtnW + gap; _btnMaxHz.Width = dispBtnW; }

                // GPU Working Mode (MUX Switch) buttons
                int gpuBtnW = (contentW - 2 * gap) / 3;
                if (_btnGpuOptimus != null) _btnGpuOptimus.Width = gpuBtnW;
                if (_btnGpuDiscrete != null) { _btnGpuDiscrete.Left = pad + gpuBtnW + gap; _btnGpuDiscrete.Width = gpuBtnW; }
                if (_btnGpuAuto != null) { _btnGpuAuto.Left = pad + (gpuBtnW + gap) * 2; _btnGpuAuto.Width = gpuBtnW; }

                // LCD Overdrive & Backlight 30s Switches
                if (_switchLcdOverdrive != null) _switchLcdOverdrive.Left = ClientSize.Width - pad - S(48);
                if (_switchBacklight30s != null) _switchBacklight30s.Left = ClientSize.Width - pad - S(48);

                // Battery Limit & Startup Switches
                if (_switchBatteryLimit != null) _switchBatteryLimit.Left = ClientSize.Width - pad - S(48);
                if (_switchStartWithWindows != null) _switchStartWithWindows.Left = ClientSize.Width - pad - S(48);

                // System & Hardware Controls
                if (_switchWinKeyLock != null) _switchWinKeyLock.Left = ClientSize.Width - pad - S(48);

                // Gaming Overlay controls
                if (_switchOverlay != null) _switchOverlay.Left = ClientSize.Width - pad - S(48);
                int overlayModeBtnW = (contentW - 2 * gap) / 3;
                if (_btnOverlayLight != null) { _btnOverlayLight.Left = pad; _btnOverlayLight.Width = overlayModeBtnW; }
                if (_btnOverlayDefault != null) { _btnOverlayDefault.Left = pad + overlayModeBtnW + gap; _btnOverlayDefault.Width = overlayModeBtnW; }
                if (_btnOverlayFull != null) { _btnOverlayFull.Left = pad + (overlayModeBtnW + gap) * 2; _btnOverlayFull.Width = overlayModeBtnW; }
                if (_sliderOverlayScale != null) _sliderOverlayScale.Width = contentW;

                // Keyboard RGB controls
                if (_rgbDropDown != null) _rgbDropDown.Width = contentW;

                // Keyboard 4-Zone controls
                int zoneBtnW = (contentW - 4 * gap) / 5;
                if (_btnAllZones != null)
                {
                    _btnAllZones.Left = pad;
                    _btnAllZones.Width = zoneBtnW;
                }
                for (int z = 0; z < 4; z++)
                {
                    if (_btnZones[z] != null)
                    {
                        _btnZones[z].Left = pad + (zoneBtnW + gap) * (z + 1);
                        _btnZones[z].Width = zoneBtnW;
                    }
                }

                int preW = (contentW - 5 * gap) / 6;
                for (int i = 0; i < 6; i++)
                {
                    if (_btnPresetColors[i] != null)
                    {
                        _btnPresetColors[i].Left = pad + (preW + gap) * i;
                        _btnPresetColors[i].Width = preW;
                    }
                }

                if (_btnCustomColor != null) _btnCustomColor.Width = contentW;

                int sliderW = (contentW - gap * 4) / 2;
                if (_brightnessSlider != null) _brightnessSlider.Width = sliderW;
                if (_lblSpeedHdr != null) _lblSpeedHdr.Left = midX + S(10);
                if (_speedSlider != null) { _speedSlider.Left = midX + S(10); _speedSlider.Width = sliderW; }

                // Game Sync & Updates
                if (_switchGameSync != null) _switchGameSync.Left = ClientSize.Width - pad - S(48);
                if (_btnConfigureGames != null) _btnConfigureGames.Width = contentW;
                if (_btnCheckUpdates != null) _btnCheckUpdates.Left = ClientSize.Width - pad - _btnCheckUpdates.Width;

                _contentPanel.Invalidate(true);
            }

            if (WindowState == FormWindowState.Minimized)
            {
                _telemetryService?.SetPollingState(false);
            }
            else if (WindowState == FormWindowState.Normal)
            {
                _telemetryService?.SetPollingState(true);
            }
        }

        #endregion

        #region Action Handlers

        public void CyclePowerMode(bool showOsd = true)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => CyclePowerMode(showOsd)));
                return;
            }

            bool isPlugged = _isPluggedIn.GetValueOrDefault(SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online);

            byte currentMode = _activePowerBtn == _btnQuiet ? (byte)0x00 :
                               _activePowerBtn == _btnPerform ? (byte)0x04 :
                               _activePowerBtn == _btnTurbo ? (byte)0x05 :
                               _activePowerBtn == _btnEco ? (byte)0x06 :
                               (byte)0x01; // default Balanced

            byte nextMode;
            PredatorButton nextBtn;

            if (!isPlugged)
            {
                // On Battery: Toggle specifically between Balanced (0x01) and Eco (0x06)
                if (currentMode == 0x06)
                {
                    nextMode = 0x01;
                    nextBtn = _btnBalanced;
                }
                else
                {
                    nextMode = 0x06;
                    nextBtn = _btnEco;
                }
            }
            else
            {
                // On Charger / AC: Quiet (0x00) -> Balanced (0x01) -> Performance (0x04) -> Turbo (0x05) -> Quiet (0x00)
                switch (currentMode)
                {
                    case 0x00: // Quiet -> Balanced
                        nextMode = 0x01;
                        nextBtn = _btnBalanced;
                        break;

                    case 0x01: // Balanced -> Performance
                        nextMode = 0x04;
                        nextBtn = _btnPerform;
                        break;

                    case 0x04: // Performance -> Turbo
                        nextMode = 0x05;
                        nextBtn = _btnTurbo;
                        break;

                    case 0x05: // Turbo -> Quiet
                        nextMode = 0x00;
                        nextBtn = _btnQuiet;
                        break;

                    default: // Eco or any unhandled mode -> Quiet
                        nextMode = 0x00;
                        nextBtn = _btnQuiet;
                        break;
                }
            }

            ApplyPowerMode(nextMode, nextBtn, showOsd);
        }

        private void ApplyPowerMode(byte mode, PredatorButton btn, bool showOsd = false)
        {
            _wmi.SetPowerMode(mode);
            HighlightBtn(btn, ref _activePowerBtn);
            SaveState("Power", mode);

            _lblPowerStatus.Text = mode switch
            {
                0x00 => "Quiet",
                0x04 => "Performance",
                0x05 => "Turbo",
                0x06 => "Eco",
                _ => "Balanced"
            };

            var trayItem = mode switch
            {
                0x00 => _trayPowerQuiet,
                0x04 => _trayPowerPerf,
                0x05 => _trayPowerTurbo,
                0x06 => _trayPowerEco,
                _ => _trayPowerBal
            };
            CheckTrayItem(trayItem, _trayPowerQuiet, _trayPowerBal, _trayPowerPerf, _trayPowerTurbo, _trayPowerEco);

            if (showOsd)
            {
                bool isPlugged = _isPluggedIn.GetValueOrDefault(SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online);
                OSDOverlayForm.ShowMode(mode, !isPlugged);
            }
        }

        private void ApplyFanMode(byte mode, PredatorButton btn)
        {
            _wmi.SetFanBehavior(mode);
            HighlightBtn(btn, ref _activeFanBtn);
            SaveState("Fan", mode);

            _lblFanStatus.Text = mode switch
            {
                0x02 => "Max",
                0x03 => "Custom",
                _ => "Auto"
            };

            var trayFan = mode switch
            {
                0x02 => _trayFanMax,
                0x03 => _trayFanCustom,
                _ => _trayFanAuto
            };
            CheckTrayItem(trayFan, _trayFanAuto, _trayFanMax, _trayFanCustom);

            bool isCustom = mode == 0x03;
            if (isCustom && _btnCustomFan != null)
            {
                int fanTop = _btnCustomFan.Bottom + S(12);
                if (_lblCpuFanSpeedHdr != null) _lblCpuFanSpeedHdr.Top = fanTop;
                if (_lblGpuFanSpeedHdr != null) _lblGpuFanSpeedHdr.Top = fanTop;
                int sliderTop = fanTop + S(24);
                if (_cpuFanSlider != null) _cpuFanSlider.Top = sliderTop;
                if (_gpuFanSlider != null) _gpuFanSlider.Top = sliderTop;
                int subBtnTop = sliderTop + S(28) + S(8);
                if (_btnFixedSpeed != null) _btnFixedSpeed.Top = subBtnTop;
                if (_btnFanCurve != null) _btnFanCurve.Top = subBtnTop;
            }

            if (_lblCpuFanSpeedHdr != null) _lblCpuFanSpeedHdr.Visible = isCustom;
            if (_lblGpuFanSpeedHdr != null) _lblGpuFanSpeedHdr.Visible = isCustom;
            if (_cpuFanSlider != null) _cpuFanSlider.Visible = isCustom;
            if (_gpuFanSlider != null) _gpuFanSlider.Visible = isCustom;
            if (_btnFixedSpeed != null) _btnFixedSpeed.Visible = isCustom;
            if (_btnFanCurve != null) _btnFanCurve.Visible = isCustom;

            if (isCustom)
            {
                if (_fanCurveEnabled)
                {
                    if (_btnFanCurve != null) HighlightBtn(_btnFanCurve, ref _activeCustomSubBtn);
                    if (_cpuFanSlider != null) _cpuFanSlider.Enabled = false;
                    if (_gpuFanSlider != null) _gpuFanSlider.Enabled = false;
                }
                else
                {
                    if (_btnFixedSpeed != null) HighlightBtn(_btnFixedSpeed, ref _activeCustomSubBtn);
                    if (_cpuFanSlider != null) _cpuFanSlider.Enabled = true;
                    if (_gpuFanSlider != null) _gpuFanSlider.Enabled = true;
                }
            }
            else
            {
                _lastCurveCpuSpeed = -1;
                _lastCurveGpuSpeed = -1;
                if (_activeCustomSubBtn != null)
                {
                    _activeCustomSubBtn.IsActive = false;
                    _activeCustomSubBtn = null;
                }
            }

            var trayItem = mode switch
            {
                0x01 => _trayFanAuto,
                0x02 => _trayFanMax,
                _ => _trayFanCustom
            };
            CheckTrayItem(trayItem, _trayFanAuto, _trayFanMax, _trayFanCustom);
        }

        private void OpenFanCurveEditor()
        {
            if (_fanCurveForm != null && !_fanCurveForm.IsDisposed)
            {
                _fanCurveForm.Activate();
                return;
            }

            _fanCurveForm = new FanCurveForm();
            _fanCurveForm.SetCpuCurve(_cpuCurvePoints);
            _fanCurveForm.SetGpuCurve(_gpuCurvePoints);
            _fanCurveForm.UpdateTemps(_cpuTemp ?? 0, _gpuTemp ?? 0);

            _fanCurveForm.ApplyClicked += (s, args) =>
            {
                _cpuCurvePoints = args.CpuPoints;
                _gpuCurvePoints = args.GpuPoints;

                int cpuSpeed = _fanCurveForm!.InterpolateCpuSpeed(_cpuTemp ?? 0);
                int gpuSpeed = _fanCurveForm!.InterpolateGpuSpeed(_gpuTemp ?? 0);

                bool cpuOk = _wmi.SetCpuFanSpeed((byte)cpuSpeed);
                bool gpuOk = _wmi.SetGpuFanSpeed((byte)gpuSpeed);

                args.Success = cpuOk && gpuOk;

                if (args.Success)
                {
                    _fanCurveEnabled = true;
                    _lastCurveCpuSpeed = cpuSpeed;
                    _lastCurveGpuSpeed = gpuSpeed;

                    _cpuFanSlider.Value = Math.Clamp(cpuSpeed, 10, 100);
                    _gpuFanSlider.Value = Math.Clamp(gpuSpeed, 10, 100);
                    _lblCpuFanSpeedHdr.Text = $"CPU FAN: {cpuSpeed}%";
                    _lblGpuFanSpeedHdr.Text = $"GPU FAN: {gpuSpeed}%";

                    SaveCurveToRegistry("CpuCurve", _cpuCurvePoints);
                    SaveCurveToRegistry("GpuCurve", _gpuCurvePoints);
                    SaveState("FanCurveEnabled", 1);
                }
            };

            _fanCurveForm.FormClosed += (s, e) => _fanCurveForm = null;
            _fanCurveForm.Show(this);
        }

        private void SaveCurveToRegistry(string name, List<Point> points)
        {
            DebounceHelper.Debounce($"Curve_{name}", () =>
            {
                try
                {
                    using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\PredatorControl");
                    string data = string.Join(";", points.Select(p => $"{p.X},{p.Y}"));
                    key.SetValue(name, data);
                }
                catch { }
            }, 300);
        }

        private List<Point>? LoadCurveFromRegistry(string name)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\PredatorControl");
                string? data = key.GetValue(name) as string;
                if (string.IsNullOrEmpty(data)) return null;

                var pts = new List<Point>();
                foreach (var pair in data.Split(';'))
                {
                    var parts = pair.Split(',');
                    if (parts.Length == 2 && int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y))
                        pts.Add(new Point(x, y));
                }
                return pts.Count >= 2 ? FanCurveGraph.Normalize(pts) : null;
            }
            catch { return null; }
        }

        private void ApplyBatteryLimit(bool limit)
        {
            if (_isUpdatingBattery) return;
            _isUpdatingBattery = true;

            try
            {
                if (_wmi.SetBatteryChargeLimit(limit))
                {
                    if (_switchBatteryLimit.Checked != limit)
                        _switchBatteryLimit.Checked = limit;

                    _lblBatteryStatus.Text = limit ? "Limit to 80% (Health)" : "Full Charge (100%)";
                    CheckTrayItem(limit ? _trayBatteryLimit80 : _trayBatteryLimit100, _trayBatteryLimit80, _trayBatteryLimit100);
                    SaveState("BatteryLimit", limit ? 1 : 0);
                }
                else
                {
                    _switchBatteryLimit.Checked = !limit;
                }
            }
            finally
            {
                _isUpdatingBattery = false;
            }
        }

        private void ApplyRgbModeFromDropdown(int mode)
        {
            UpdateRgbControlsState(mode);

            byte bright = (byte)_brightnessSlider.Value;
            byte speed = GetMappedSpeed();

            if (mode == 8) // Off
            {
                _wmi.SetKeyboardOff();
                _speedSlider.Enabled = false;
            }
            else if (mode == 0) // Static
            {
                _speedSlider.Enabled = false;
                _wmi.Set4ZoneColors(_currentZoneColors, 0, bright, speed);
            }
            else if (mode == 2 || mode == 3) // Neon (2), Wave (3) - fixed firmware rainbow animations
            {
                _speedSlider.Enabled = true;
                _wmi.SetRgbMode(mode, _wmi.LastR, _wmi.LastG, _wmi.LastB, bright, speed, 0);
            }
            else // Breathing (1), Shifting (4), Zoom (5), Meteor (6), Twinkling (7) - custom colors supported
            {
                _speedSlider.Enabled = true;
                Color c = _colorPicker.Color;
                _wmi.SetRgbMode(mode, c.R, c.G, c.B, bright, speed, 0);
            }

            if (_rgbDropDown.SelectedIndex != mode)
                _rgbDropDown.SelectedIndex = mode;

            SaveState("RGB_Mode", mode);
            CheckRgbTrayFromMode(mode);
        }

        private void CheckRgbTrayFromMode(int mode)
        {
            var active = mode switch
            {
                0 => _trayRgbStatic,
                1 => _trayRgbBreathe,
                2 => _trayRgbNeon,
                3 => _trayRgbWave,
                4 => _trayRgbShift,
                5 => _trayRgbZoom,
                6 => _trayRgbMeteor,
                7 => _trayRgbTwinkle,
                _ => _trayRgbOff
            };
            CheckTrayItem(active, _trayRgbStatic, _trayRgbBreathe, _trayRgbNeon, _trayRgbWave,
                          _trayRgbShift, _trayRgbZoom, _trayRgbMeteor, _trayRgbTwinkle, _trayRgbOff);
        }

        #endregion

        #region Game Sync Handlers

        private DashboardSnapshot CaptureCurrentState()
        {
            return new DashboardSnapshot
            {
                PowerMode = GetCurrentPowerByte(),
                FanMode = GetCurrentFanByte(),
                CpuFanSpeed = _cpuFanSlider.Value,
                GpuFanSpeed = _gpuFanSlider.Value,
                FanCurveWasEnabled = _fanCurveEnabled,
                RefreshRate = DisplayCcdController.GetCurrentRefreshRate(_internalDisplayGdiName),
                BatteryLimit = _switchBatteryLimit.Checked ? 1 : 0,
                RgbMode = _rgbDropDown.SelectedIndex,
                RgbBrightness = _brightnessSlider.Value,
                RgbSpeed = _speedSlider.Value,
                RgbR = _wmi.LastR,
                RgbG = _wmi.LastG,
                RgbB = _wmi.LastB,
                WasWinKeyLocked = _switchWinKeyLock?.Checked ?? false,
                WasLcdOverdriveEnabled = _switchLcdOverdrive?.Checked ?? true
            };
        }

        private byte GetCurrentPowerByte()
        {
            if (_activePowerBtn == _btnQuiet) return 0x00;
            if (_activePowerBtn == _btnPerform) return 0x04;
            if (_activePowerBtn == _btnTurbo) return 0x05;
            if (_activePowerBtn == _btnEco) return 0x06;
            return 0x01;
        }

        private byte GetCurrentFanByte()
        {
            if (_activeFanBtn == _btnMaxFan) return 0x02;
            if (_activeFanBtn == _btnCustomFan) return 0x03;
            return 0x01;
        }

        private PredatorButton PowerByteToBtn(byte mode) => mode switch
        {
            0x00 => _btnQuiet,
            0x04 => _btnPerform,
            0x05 => _btnTurbo,
            0x06 => _btnEco,
            _ => _btnBalanced
        };

        private PredatorButton FanByteToBtn(byte mode) => mode switch
        {
            0x02 => _btnMaxFan,
            0x03 => _btnCustomFan,
            _ => _btnAutoFan
        };

        private async void OnGameDetected(GameProfile profile)
        {
            if (InvokeRequired) { Invoke(() => OnGameDetected(profile)); return; }

            _isGameSyncOverriding = true;
            try { await ApplyGameProfile(profile); }
            finally { _isGameSyncOverriding = false; }
        }

        private async Task ApplyGameProfile(GameProfile profile)
        {
            _lblGameSyncStatus.Text = $"Active \u2014 {profile.DisplayName}";

            _gameSync.SetPreGameSnapshot(CaptureCurrentState());

            ApplyPowerMode(profile.PowerMode, PowerByteToBtn(profile.PowerMode));
            ApplyFanMode(profile.FanMode, FanByteToBtn(profile.FanMode));

            if (profile.FanMode == 0x03)
            {
                _fanCurveEnabled = false;
                HighlightBtn(_btnFixedSpeed, ref _activeCustomSubBtn);
                _cpuFanSlider.Enabled = true;
                _gpuFanSlider.Enabled = true;

                if (profile.CpuFanSpeed >= 10)
                {
                    int cpuSpeed = Math.Clamp(profile.CpuFanSpeed, 10, 100);
                    _wmi.SetCpuFanSpeed((byte)cpuSpeed);
                    _cpuFanSlider.Value = cpuSpeed;
                    _lblCpuFanSpeedHdr.Text = $"CPU FAN: {cpuSpeed}%";
                }
                if (profile.GpuFanSpeed >= 10)
                {
                    int gpuSpeed = Math.Clamp(profile.GpuFanSpeed, 10, 100);
                    _wmi.SetGpuFanSpeed((byte)gpuSpeed);
                    _gpuFanSlider.Value = gpuSpeed;
                    _lblGpuFanSpeedHdr.Text = $"GPU FAN: {gpuSpeed}%";
                }
            }

            if (profile.RefreshRate > 0)
                ApplyDisplayMode(profile.RefreshRate, profile.RefreshRate <= 60 ? _btn60Hz : _btnMaxHz);

            if (profile.BatteryLimit >= 0)
                ApplyBatteryLimit(profile.BatteryLimit == 1);

            if (profile.LockWinKey >= 0)
            {
                bool lockKeys = profile.LockWinKey == 1;
                if (_switchWinKeyLock != null) _switchWinKeyLock.Checked = lockKeys;
                PredatorKeyHook.SetWinKeyLocked(lockKeys);
                try { _wmi.SetWinKeyLock(lockKeys); } catch { }
            }

            if (profile.LcdOverdrive >= 0)
            {
                bool enableLcd = profile.LcdOverdrive == 1;
                if (_switchLcdOverdrive != null) _switchLcdOverdrive.Checked = enableLcd;
                try { _wmi.SetLcdOverdrive(enableLcd); } catch { }
            }

            await Task.Delay(500);
            if (IsDisposed) return;

            if (profile.RgbMode >= 0)
            {
                int mode = Math.Clamp(profile.RgbMode, 0, 8);
                int brightVal = profile.RgbBrightness >= 0 ? Math.Clamp(profile.RgbBrightness, 0, 100) : _brightnessSlider.Value;
                int speedVal = profile.RgbSpeed >= 0 ? Math.Clamp(profile.RgbSpeed, 1, 100) : _speedSlider.Value;
                byte bright = (byte)brightVal;
                byte speed = profile.RgbSpeed >= 0 ? (byte)Math.Clamp(Math.Round(speedVal * 9.0 / 100.0), 1, 9) : GetMappedSpeed();
                byte r = profile.RgbR >= 0 ? (byte)Math.Clamp(profile.RgbR, 0, 255) : _wmi.LastR;
                byte g = profile.RgbG >= 0 ? (byte)Math.Clamp(profile.RgbG, 0, 255) : _wmi.LastG;
                byte b = profile.RgbB >= 0 ? (byte)Math.Clamp(profile.RgbB, 0, 255) : _wmi.LastB;

                _wmi.SetRgbMode(mode, r, g, b, bright, speed, 0);
                if (_rgbDropDown.SelectedIndex != mode)
                    _rgbDropDown.SelectedIndex = mode;
                _brightnessSlider.Value = brightVal;
                _speedSlider.Value = speedVal;
                CheckRgbTrayFromMode(mode);
                UpdateRgbControlsState(mode);
            }
        }

        private async void OnGameExited(DashboardSnapshot snap)
        {
            if (InvokeRequired) { Invoke(() => OnGameExited(snap)); return; }

            _lblGameSyncStatus.Text = "Active \u2014 Monitoring";

            _isGameSyncOverriding = true;
            try { await RestoreSnapshot(snap); }
            finally { _isGameSyncOverriding = false; }
        }

        private async Task RestoreSnapshot(DashboardSnapshot snap)
        {
            ApplyPowerMode(snap.PowerMode, PowerByteToBtn(snap.PowerMode));
            ApplyFanMode(snap.FanMode, FanByteToBtn(snap.FanMode));

            if (snap.FanMode == 0x03)
            {
                if (snap.FanCurveWasEnabled)
                {
                    _fanCurveEnabled = true;
                    HighlightBtn(_btnFanCurve, ref _activeCustomSubBtn);
                    _cpuFanSlider.Enabled = false;
                    _gpuFanSlider.Enabled = false;
                }
                else
                {
                    _fanCurveEnabled = false;
                    HighlightBtn(_btnFixedSpeed, ref _activeCustomSubBtn);
                    _cpuFanSlider.Enabled = true;
                    _gpuFanSlider.Enabled = true;
                    int cpuSpeed = Math.Clamp(snap.CpuFanSpeed, 10, 100);
                    int gpuSpeed = Math.Clamp(snap.GpuFanSpeed, 10, 100);
                    _wmi.SetCpuFanSpeed((byte)cpuSpeed);
                    _wmi.SetGpuFanSpeed((byte)gpuSpeed);
                    _cpuFanSlider.Value = cpuSpeed;
                    _gpuFanSlider.Value = gpuSpeed;
                    _lblCpuFanSpeedHdr.Text = $"CPU FAN: {cpuSpeed}%";
                    _lblGpuFanSpeedHdr.Text = $"GPU FAN: {gpuSpeed}%";
                }
            }

            if (snap.RefreshRate > 0)
                ApplyDisplayMode(snap.RefreshRate, snap.RefreshRate <= 60 ? _btn60Hz : _btnMaxHz);

            ApplyBatteryLimit(snap.BatteryLimit == 1);

            if (_switchWinKeyLock != null)
            {
                _switchWinKeyLock.Checked = snap.WasWinKeyLocked;
                PredatorKeyHook.SetWinKeyLocked(snap.WasWinKeyLocked);
                try { _wmi.SetWinKeyLock(snap.WasWinKeyLocked); } catch { }
            }

            if (_switchLcdOverdrive != null)
            {
                _switchLcdOverdrive.Checked = snap.WasLcdOverdriveEnabled;
                try { _wmi.SetLcdOverdrive(snap.WasLcdOverdriveEnabled); } catch { }
            }

            await Task.Delay(500);
            if (IsDisposed) return;

            int rgbMode = Math.Clamp(snap.RgbMode, 0, 8);
            int bright = Math.Clamp(snap.RgbBrightness, 0, 100);
            int speed = Math.Clamp(snap.RgbSpeed, 1, 100);

            _wmi.SetRgbMode(rgbMode,
                            (byte)Math.Clamp(snap.RgbR, 0, 255),
                            (byte)Math.Clamp(snap.RgbG, 0, 255),
                            (byte)Math.Clamp(snap.RgbB, 0, 255),
                            (byte)bright,
                            (byte)Math.Clamp(Math.Round(speed * 9.0 / 100.0), 1, 9), 0);
            if (_rgbDropDown.SelectedIndex != rgbMode)
                _rgbDropDown.SelectedIndex = rgbMode;
            _brightnessSlider.Value = bright;
            _speedSlider.Value = speed;
            CheckRgbTrayFromMode(rgbMode);
            UpdateRgbControlsState(rgbMode);
        }

        #endregion

        #region State Persistence

        private void SaveState(string name, int value)
        {
            if (_isGameSyncOverriding) return;
            DebounceHelper.Debounce($"State_{name}", () =>
            {
                try
                {
                    using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\PredatorControl");
                    key.SetValue(name, value);
                }
                catch { }

                // Mirror hardware states to HKLM so boot-time task can enforce them before user logon
                if (name is "BatteryLimit" or "Power" or "Fan" or "RGB_Mode" or "RGB_R" or "RGB_G" or "RGB_B" or "Brightness" or "RGB_Speed"
                    || name.StartsWith("ZoneColor_"))
                {
                    try
                    {
                        using var hklmKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\PredatorControl");
                        hklmKey?.SetValue(name, value);
                    }
                    catch { }
                }
            }, 300);
        }

        private static int GetInt(RegistryKey key, string name, int fallback, int min, int max)
        {
            int v = fallback;
            try
            {
                object? raw = key.GetValue(name);
                if (raw is int i) v = i;
                else if (raw != null && int.TryParse(raw.ToString(), out int p)) v = p;
            }
            catch { }
            return Math.Clamp(v, min, max);
        }

        private void LoadMemory()
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\PredatorControl");
                int savedPower = GetInt(key, "Power", 0x01, 0x00, 0xFF);
                int savedFan = GetInt(key, "Fan", 0x01, 0x00, 0xFF);
                int savedRgbMode = GetInt(key, "RGB_Mode", 0, 0, 8);
                int savedBrightness = GetInt(key, "Brightness", 100, 0, 100);
                int savedSpeed = GetInt(key, "RGB_Speed", 50, 1, 100);

                int savedR = GetInt(key, "RGB_R", 0, 0, 255);
                int savedG = GetInt(key, "RGB_G", 230, 0, 255);
                int savedB = GetInt(key, "RGB_B", 180, 0, 255);
                _colorPicker.Color = Color.FromArgb(savedR, savedG, savedB);

                _brightnessSlider.Value = Math.Clamp(savedBrightness, 0, 100);
                if (_lblBrightHdr != null) _lblBrightHdr.Text = $"BRIGHTNESS: {_brightnessSlider.Value}%";
                _speedSlider.Value = Math.Clamp(savedSpeed, 1, 100);
                if (_lblSpeedHdr != null) _lblSpeedHdr.Text = $"EFFECT SPEED: {_speedSlider.Value}%";

                var (powerMode, powerBtn) = savedPower switch
                {
                    0x00 => ((byte)0x00, _btnQuiet),
                    0x04 => ((byte)0x04, _btnPerform),
                    0x05 => ((byte)0x05, _btnTurbo),
                    0x06 => ((byte)0x06, _btnEco),
                    _ => ((byte)0x01, _btnBalanced)
                };
                ApplyPowerMode(powerMode, powerBtn);

                _cboAcProfile.SelectedIndex = GetInt(key, "AutoPowerAC", 0, 0, _cboAcProfile.Items.Count - 1);
                _cboBatteryProfile.SelectedIndex = GetInt(key, "AutoPowerBattery", 0, 0, _cboBatteryProfile.Items.Count - 1);

                int savedFanSpeedCpu = GetInt(key, "FanSpeedCpu", 50, 10, 100);
                int savedFanSpeedGpu = GetInt(key, "FanSpeedGpu", 50, 10, 100);

                var (fanMode, fanBtn) = savedFan switch
                {
                    0x02 => ((byte)0x02, _btnMaxFan),
                    0x03 => ((byte)0x03, _btnCustomFan),
                    _ => ((byte)0x01, _btnAutoFan)
                };

                var loadedCpu = LoadCurveFromRegistry("CpuCurve");
                var loadedGpu = LoadCurveFromRegistry("GpuCurve");
                if (loadedCpu != null) _cpuCurvePoints = loadedCpu;
                if (loadedGpu != null) _gpuCurvePoints = loadedGpu;

                int savedCurveEnabled = GetInt(key, "FanCurveEnabled", 0, 0, 1);
                if (savedCurveEnabled == 1 && fanMode == 0x03)
                    _fanCurveEnabled = true;

                ApplyFanMode(fanMode, fanBtn);

                if (fanMode == 0x03)
                {
                    if (_fanCurveEnabled)
                    {
                        int cpuSpeed = InterpolateCurve(_cpuCurvePoints, _cpuTemp ?? 45);
                        int gpuSpeed = InterpolateCurve(_gpuCurvePoints, _gpuTemp ?? 45);
                        _wmi.SetCpuFanSpeed((byte)cpuSpeed);
                        _wmi.SetGpuFanSpeed((byte)gpuSpeed);
                        _lastCurveCpuSpeed = cpuSpeed;
                        _lastCurveGpuSpeed = gpuSpeed;
                        _cpuFanSlider.Value = Math.Clamp(cpuSpeed, 10, 100);
                        _gpuFanSlider.Value = Math.Clamp(gpuSpeed, 10, 100);
                        _lblCpuFanSpeedHdr.Text = $"CPU FAN: {cpuSpeed}%";
                        _lblGpuFanSpeedHdr.Text = $"GPU FAN: {gpuSpeed}%";
                    }
                    else
                    {
                        _cpuFanSlider.Value = savedFanSpeedCpu;
                        _gpuFanSlider.Value = savedFanSpeedGpu;
                        _lblCpuFanSpeedHdr.Text = $"CPU FAN: {savedFanSpeedCpu}%";
                        _lblGpuFanSpeedHdr.Text = $"GPU FAN: {savedFanSpeedGpu}%";
                        _wmi.SetFanSpeed((byte)savedFanSpeedCpu, (byte)savedFanSpeedGpu);
                    }
                }

                // Load 4-Zone Colors
                for (int z = 0; z < 4; z++)
                {
                    int savedArgb = GetInt(key, $"ZoneColor_{z}", -1, int.MinValue, int.MaxValue);
                    if (savedArgb != -1)
                    {
                        _currentZoneColors[z] = Color.FromArgb(savedArgb);
                    }
                    else
                    {
                        _currentZoneColors[z] = Color.FromArgb(savedR, savedG, savedB);
                    }

                    if (_btnZones[z] != null)
                        _btnZones[z].ColorIndicator = _currentZoneColors[z];
                }
                if (_btnAllZones != null)
                    _btnAllZones.ColorIndicator = _currentZoneColors[0];

                int clampedMode = Math.Clamp(savedRgbMode, 0, 8);
                ApplyRgbModeFromDropdown(clampedMode);

                var activeColor = Color.FromArgb(savedR, savedG, savedB);
                if (_btnCustomColor != null)
                    _btnCustomColor.ColorIndicator = activeColor;

                try
                {
                    using var hklmKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\PredatorControl");
                    if (hklmKey != null)
                    {
                        hklmKey.SetValue("RGB_Mode", clampedMode);
                        hklmKey.SetValue("Brightness", savedBrightness);
                        hklmKey.SetValue("RGB_Speed", savedSpeed);
                        hklmKey.SetValue("RGB_R", savedR);
                        hklmKey.SetValue("RGB_G", savedG);
                        hklmKey.SetValue("RGB_B", savedB);
                        for (int z = 0; z < 4; z++)
                        {
                            hklmKey.SetValue($"ZoneColor_{z}", _currentZoneColors[z].ToArgb());
                        }
                    }
                }
                catch { }

                if (_wmi.IsBatteryControlSupported())
                {
                    bool limitEnabled = GetInt(key, "BatteryLimit", 0, 0, 1) == 1;
                    _wmi.SetBatteryChargeLimit(limitEnabled);

                    _isUpdatingBattery = true;
                    _switchBatteryLimit.Checked = limitEnabled;
                    _lblBatteryStatus.Text = limitEnabled ? "Limit to 80% (Health)" : "Full Charge (100%)";
                    CheckTrayItem(limitEnabled ? _trayBatteryLimit80 : _trayBatteryLimit100, _trayBatteryLimit80, _trayBatteryLimit100);
                    _isUpdatingBattery = false;
                }
                else
                {
                    _isUpdatingBattery = true;
                    _switchBatteryLimit.Checked = false;
                    _switchBatteryLimit.Enabled = false;
                    _lblBatteryStatus.Text = "Not Supported";
                    _lblBatteryStatus.ForeColor = Color.FromArgb(120, 120, 135);
                    _trayBatteryLimit80.Enabled = false;
                    _trayBatteryLimit100.Enabled = false;
                    _trayBatteryMenu.Enabled = false;
                    _isUpdatingBattery = false;
                }

                if (IsStartupEnabled())
                {
                    Task.Run(() => { try { SetStartupEnabled(true); } catch { } });
                }

                // LCD Overdrive
                int savedLcdOverdrive = GetInt(key, "LcdOverdrive", 1, 0, 1);
                if (_switchLcdOverdrive != null) _switchLcdOverdrive.Checked = (savedLcdOverdrive == 1);
                Task.Run(() => { try { _wmi.SetLcdOverdrive(savedLcdOverdrive == 1); } catch { } });

                // Backlight 30s Sleep
                int savedBacklight30s = GetInt(key, "Backlight30s", -1, 0, 1);
                if (savedBacklight30s == -1)
                {
                    try { savedBacklight30s = _wmi.GetBacklight30s() ? 1 : 0; } catch { savedBacklight30s = 1; }
                }
                if (_switchBacklight30s != null) _switchBacklight30s.Checked = (savedBacklight30s == 1);
                Task.Run(() => { try { _wmi.SetBacklight30s(savedBacklight30s == 1); } catch { } });

                // Gaming Overlay HUD
                int savedOverlay = GetInt(key, "OverlayEnabled", 0, 0, 1);
                if (savedOverlay == 1)
                {
                    BeginInvoke(() => SetOverlayVisible(true));
                }

                // Windows Key Lock
                int savedWinKeyLock = GetInt(key, "LockWinKey", 0, 0, 1);
                if (_switchWinKeyLock != null) _switchWinKeyLock.Checked = (savedWinKeyLock == 1);
                PredatorKeyHook.SetWinKeyLocked(savedWinKeyLock == 1);
                Task.Run(() => { try { _wmi.SetWinKeyLock(savedWinKeyLock == 1); } catch { } });

                // GPU Working Mode (MUX Switch)
                int savedGpuMode = GetInt(key, "GpuMode", AcerAgentClient.GPU_MODE_OPTIMUS, 0, 2);
                _currentGpuMode = savedGpuMode;
                PredatorButton? targetBtn = savedGpuMode switch
                {
                    AcerAgentClient.GPU_MODE_OPTIMUS => _btnGpuOptimus,
                    AcerAgentClient.GPU_MODE_DISCRETE => _btnGpuDiscrete,
                    AcerAgentClient.GPU_MODE_AUTO => _btnGpuAuto,
                    _ => _btnGpuOptimus
                };
                if (targetBtn != null)
                    HighlightBtn(targetBtn, ref _activeGpuModeBtn);

                ToolStripMenuItem? targetTray = savedGpuMode switch
                {
                    AcerAgentClient.GPU_MODE_OPTIMUS => _trayGpuOptimus,
                    AcerAgentClient.GPU_MODE_DISCRETE => _trayGpuDiscrete,
                    AcerAgentClient.GPU_MODE_AUTO => _trayGpuAuto,
                    _ => _trayGpuOptimus
                };
                if (targetTray != null)
                    CheckTrayItem(targetTray, _trayGpuOptimus, _trayGpuDiscrete, _trayGpuAuto);

                // Query live hardware capability & mode from OEM service/firmware
                Task.Run(async () =>
                {
                    try
                    {
                        int cap = await _wmi.GetGpuModeCapabilityAsync();
                        _gpuCapability = cap;
                        int? liveMode = await _wmi.GetGpuModeAsync();

                        void UpdateGpuUi()
                        {
                            if (_isClosing || IsDisposed) return;

                            if ((_gpuCapability & 4) == 0 && _btnGpuAuto != null)
                            {
                                _btnGpuAuto.Visible = false;
                                if (_trayGpuAuto != null) _trayGpuAuto.Visible = false;
                            }

                            if (liveMode.HasValue)
                            {
                                PredatorButton? liveBtn = liveMode.Value switch
                                {
                                    AcerAgentClient.GPU_MODE_OPTIMUS => _btnGpuOptimus,
                                    AcerAgentClient.GPU_MODE_DISCRETE => _btnGpuDiscrete,
                                    AcerAgentClient.GPU_MODE_AUTO => _btnGpuAuto,
                                    _ => null
                                };

                                ToolStripMenuItem? liveTray = liveMode.Value switch
                                {
                                    AcerAgentClient.GPU_MODE_OPTIMUS => _trayGpuOptimus,
                                    AcerAgentClient.GPU_MODE_DISCRETE => _trayGpuDiscrete,
                                    AcerAgentClient.GPU_MODE_AUTO => _trayGpuAuto,
                                    _ => null
                                };

                                if (_currentGpuMode == null || _currentGpuMode == liveMode.Value)
                                {
                                    _currentGpuMode = liveMode.Value;
                                    if (liveBtn != null)
                                        HighlightBtn(liveBtn, ref _activeGpuModeBtn);
                                    if (liveTray != null)
                                        CheckTrayItem(liveTray, _trayGpuOptimus, _trayGpuDiscrete, _trayGpuAuto);
                                }
                                else if (_currentGpuMode != liveMode.Value)
                                {
                                    // A mode switch is pending restart!
                                    string pendingName = _currentGpuMode.Value switch
                                    {
                                        AcerAgentClient.GPU_MODE_OPTIMUS => "Optimus (Hybrid)",
                                        AcerAgentClient.GPU_MODE_DISCRETE => "Discrete GPU Only",
                                        AcerAgentClient.GPU_MODE_AUTO => "Auto (Advanced Optimus)",
                                        _ => "Selected mode"
                                    };
                                    if (_lblGpuRestartNotice != null)
                                    {
                                        _lblGpuRestartNotice.Text = $"Pending restart: {pendingName} will be active on next boot";
                                        _lblGpuRestartNotice.ForeColor = Color.FromArgb(255, 180, 50);
                                    }
                                }
                            }
                        }

                        if (IsHandleCreated)
                        {
                            BeginInvoke(new Action(UpdateGpuUi));
                        }
                        else
                        {
                            for (int i = 0; i < 25 && !IsHandleCreated && !IsDisposed; i++)
                                await Task.Delay(100);

                            if (IsHandleCreated && !IsDisposed)
                                BeginInvoke(new Action(UpdateGpuUi));
                        }
                    }
                    catch { }
                });
            }
            catch { }
        }

        #endregion

        #region Telemetry & Power Rules

        private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.Resume) return;
            if (_isClosing || IsDisposed || !IsHandleCreated) return;

            try { BeginInvoke(new Action(async () => await ResyncAfterResume())); }
            catch { }
        }

        private async Task ResyncAfterResume()
        {
            if (_isResyncing) return;
            _isResyncing = true;

            try
            {
                _isPluggedIn = null;
                _lastCurveCpuSpeed = -1;
                _lastCurveGpuSpeed = -1;

                var snap = CaptureCurrentState();
                snap.RefreshRate = _activeDisplayBtn == _btn60Hz ? 60 : _maxHz;

                // Immediately re-enforce battery limit register upon waking up
                if (_wmi.IsBatteryControlSupported())
                {
                    _wmi.SetBatteryChargeLimit(snap.BatteryLimit == 1);
                }

                await Task.Delay(2000);
                if (_isClosing || IsDisposed) return;

                await RestoreSnapshot(snap);
            }
            catch (Exception ex) { Program.Report(ex, false); }
            finally
            {
                _pendingPluggedIn = null;
                _powerLineStableTicks = 0;
                _isResyncing = false;
            }
        }

        internal static bool? DebouncePowerLine(PowerLineStatus line, bool? current, ref bool? pending, ref int ticks)
        {
            if (line == PowerLineStatus.Unknown)
            {
                pending = null;
                ticks = 0;
                return current;
            }

            bool pluggedIn = line == PowerLineStatus.Online;

            if (pending != pluggedIn)
            {
                pending = pluggedIn;
                ticks = 1;
            }
            else if (ticks < 2)
            {
                ticks++;
            }

            return ticks >= 2 ? pluggedIn : current;
        }

        private void OnTelemetryReceived(TelemetrySnapshot snap)
        {
            bool? confirmed = DebouncePowerLine(snap.PowerLine, _isPluggedIn, ref _pendingPluggedIn, ref _powerLineStableTicks);

            if (confirmed != _isPluggedIn && !_isResyncing)
            {
                try { ApplyPowerRules(confirmed == true); }
                catch (Exception ex) { Program.Report(ex, false); }
                finally { _isPluggedIn = confirmed; }
            }

            _cpuTemp = snap.CpuTemp;
            _gpuTemp = snap.GpuTemp;
            _cpuFanRpm = snap.CpuFanRpm;
            _gpuFanRpm = snap.GpuFanRpm;

            // Audit: check .HasValue and never format as "CPU: °C"
            _lblCpuTemp.Text = _cpuTemp.HasValue ? $"{_cpuTemp.Value}°C" : "--°C";
            _lblGpuTemp.Text = _gpuTemp.HasValue ? $"{_gpuTemp.Value}°C" : "--°C";
            _lblCpuTemp.ForeColor = TempColor(_cpuTemp ?? 0);
            _lblGpuTemp.ForeColor = TempColor(_gpuTemp ?? 0);

            _lblCpuRpm.Text = _cpuFanRpm.HasValue ? $"{_cpuFanRpm.Value} RPM" : "-- RPM";
            _lblGpuRpm.Text = _gpuFanRpm.HasValue ? $"{_gpuFanRpm.Value} RPM" : "-- RPM";

            _trayIcon.Text = $"Predator Control\nCPU: {(_cpuTemp.HasValue ? $"{_cpuTemp.Value}°C" : "--°C")}  GPU: {(_gpuTemp.HasValue ? $"{_gpuTemp.Value}°C" : "--°C")}";

            if (_fanCurveForm != null && !_fanCurveForm.IsDisposed)
                _fanCurveForm.UpdateTemps(_cpuTemp ?? 0, _gpuTemp ?? 0);

            if (_overlayForm != null && !_overlayForm.IsDisposed && _overlayForm.Visible)
                _overlayForm.UpdateSnapshot(snap);

            ApplyFanCurve();
        }

        private void ApplyFanCurve()
        {
            if (!_fanCurveEnabled) return;
            if (GetCurrentFanByte() != 0x03) return;
            if ((_cpuTemp ?? 0) <= 0 && (_gpuTemp ?? 0) <= 0) return;

            int cpuSpeed = InterpolateCurve(_cpuCurvePoints, _cpuTemp ?? 0);
            int gpuSpeed = InterpolateCurve(_gpuCurvePoints, _gpuTemp ?? 0);

            if (cpuSpeed != _lastCurveCpuSpeed)
            {
                _wmi.SetCpuFanSpeed((byte)cpuSpeed);
                _lastCurveCpuSpeed = cpuSpeed;

                _cpuFanSlider.Value = Math.Clamp(cpuSpeed, 10, 100);
                _lblCpuFanSpeedHdr.Text = $"CPU FAN: {cpuSpeed}%";
            }

            if (gpuSpeed != _lastCurveGpuSpeed)
            {
                _wmi.SetGpuFanSpeed((byte)gpuSpeed);
                _lastCurveGpuSpeed = gpuSpeed;

                _gpuFanSlider.Value = Math.Clamp(gpuSpeed, 10, 100);
                _lblGpuFanSpeedHdr.Text = $"GPU FAN: {gpuSpeed}%";
            }
        }

        private static int InterpolateCurve(List<Point> curve, int temp)
        {
            if (curve.Count == 0) return 50;
            if (temp <= curve[0].X) return curve[0].Y;
            if (temp >= curve[^1].X) return curve[^1].Y;

            for (int i = 0; i < curve.Count - 1; i++)
            {
                if (temp >= curve[i].X && temp <= curve[i + 1].X)
                {
                    float span = curve[i + 1].X - curve[i].X;
                    if (span == 0) return curve[i].Y;
                    float t = (temp - curve[i].X) / span;
                    return (int)Math.Round(curve[i].Y + t * (curve[i + 1].Y - curve[i].Y));
                }
            }
            return curve[^1].Y;
        }

        private void ApplyPowerRules(bool pluggedIn)
        {
            if (pluggedIn)
            {
                _btnPerform.Enabled = true;
                _btnTurbo.Enabled = true;
                _btnEco.Enabled = false;
                _trayPowerPerf.Enabled = true;
                _trayPowerTurbo.Enabled = true;
                _trayPowerEco.Enabled = false;

                int acIdx = _cboAcProfile.SelectedIndex;
                if (acIdx > 0 && acIdx < AcProfileValues.Length)
                {
                    byte mode = AcProfileValues[acIdx];
                    ApplyPowerMode(mode, PowerByteToBtn(mode));
                }
                else if (_activePowerBtn == _btnEco)
                {
                    ApplyPowerMode(0x01, _btnBalanced);
                }
            }
            else
            {
                _btnPerform.Enabled = false;
                _btnTurbo.Enabled = false;
                _btnEco.Enabled = true;
                _trayPowerPerf.Enabled = false;
                _trayPowerTurbo.Enabled = false;
                _trayPowerEco.Enabled = true;

                int batIdx = _cboBatteryProfile.SelectedIndex;
                if (batIdx > 0 && batIdx < BatteryProfileValues.Length)
                {
                    byte mode = BatteryProfileValues[batIdx];
                    ApplyPowerMode(mode, PowerByteToBtn(mode));
                }
                else if (_activePowerBtn == _btnPerform || _activePowerBtn == _btnTurbo)
                {
                    // Unplugging AC while in Turbo/Perf switches to Balanced
                    // and removes active highlight from the disabled Turbo button
                    ApplyPowerMode(0x01, _btnBalanced);
                }
            }
        }

        private static Color TempColor(int temp) => temp switch
        {
            <= 0 => Color.FromArgb(107, 114, 128),
            < 55 => Color.FromArgb(0, 200, 160),
            < 72 => Color.FromArgb(255, 220, 50),
            < 87 => Color.FromArgb(255, 140, 0),
            _ => Color.FromArgb(255, 60, 60)
        };

        #endregion

        #region UI Helpers

        private byte GetMappedSpeed() => (byte)Math.Clamp(Math.Round(_speedSlider.Value * 9.0 / 100.0), 1, 9);

        private void HighlightBtn(PredatorButton btn, ref PredatorButton? tracker)
        {
            if (tracker != null) tracker.IsActive = false;
            btn.IsActive = true;
            tracker = btn;
        }

        private static void CheckTrayItem(ToolStripMenuItem active, params ToolStripMenuItem?[] group)
        {
            foreach (var item in group) if (item != null) item.Checked = false;
            active.Checked = true;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_isClosing)
            {
                e.Cancel = true;
                HideApp();
            }
            else
            {
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                ThemeManager.ThemeChanged -= OnThemeChanged;
                _trayIcon.Visible = false;
                _colorPicker.Dispose();
                _telemetryService?.Dispose();
                _predatorKeyHook?.Dispose();
                _gameSync.Dispose();
                _wmi.Dispose();
                _ipc?.Dispose();
                base.OnFormClosing(e);
            }
        }

        #endregion

        #region Startup Task Registration

        private const string StartupTaskName = "PredatorControl";
        private const string BootTaskName = "PredatorControlBoot";

        private static bool IsStartupEnabled()
        {
            return RunSchtasks($"/Query /TN \"{StartupTaskName}\"") == 0;
        }

        private static bool SetStartupEnabled(bool enable)
        {
            RemoveLegacyRunKey();

            if (!enable)
            {
                RunSchtasks($"/Delete /TN \"{BootTaskName}\" /F");
                return RunSchtasks($"/Delete /TN \"{StartupTaskName}\" /F") == 0 || !IsStartupEnabled();
            }

            // 1. Register main interactive task: triggers immediately at user logon with 0 delay and correct working directory
            string xmlPath = Path.Combine(Path.GetTempPath(), "PredatorControlStartup.xml");
            bool logonOk = false;
            try
            {
                File.WriteAllText(xmlPath, BuildStartupTaskXml(), Encoding.Unicode);
                logonOk = RunSchtasks($"/Create /TN \"{StartupTaskName}\" /XML \"{xmlPath}\" /F") == 0;
            }
            catch { }
            finally
            {
                try { File.Delete(xmlPath); } catch { }
            }

            // 2. Register machine early boot task: triggers at system boot (before logon) as SYSTEM to lock 80% battery limit and apply saved RGB profile
            string bootXmlPath = Path.Combine(Path.GetTempPath(), "PredatorControlBoot.xml");
            try
            {
                File.WriteAllText(bootXmlPath, BuildBootTaskXml(), Encoding.Unicode);
                RunSchtasks($"/Create /TN \"{BootTaskName}\" /XML \"{bootXmlPath}\" /RU \"NT AUTHORITY\\SYSTEM\" /F");
            }
            catch { }
            finally
            {
                try { File.Delete(bootXmlPath); } catch { }
            }

            return logonOk;
        }

        private static string BuildStartupTaskXml()
        {
            string exePath = Environment.ProcessPath ?? Application.ExecutablePath;
            if (exePath.Contains("PredatorSense", StringComparison.OrdinalIgnoreCase) ||
                exePath.Contains("Prerequisites", StringComparison.OrdinalIgnoreCase) ||
                exePath.Contains("NitroSense", StringComparison.OrdinalIgnoreCase))
            {
                string[] candidates =
                {
                    @"C:\Users\youse\Downloads\PredatorControl\PredatorControl-standalone.exe",
                    @"C:\Users\youse\Downloads\PredatorControl-standalone.exe",
                    @"C:\Users\youse\Downloads\PredatorControl-Unpacked\PredatorControlApp.exe"
                };
                foreach (var c in candidates)
                {
                    if (File.Exists(c)) { exePath = c; break; }
                }
            }
            string workingDir = Path.GetDirectoryName(exePath) ?? "";
            string exe = System.Security.SecurityElement.Escape(exePath);
            string dir = System.Security.SecurityElement.Escape(workingDir);
            string userSid = WindowsIdentity.GetCurrent().User?.Value ?? "";
            string user = System.Security.SecurityElement.Escape(string.IsNullOrEmpty(userSid) ? WindowsIdentity.GetCurrent().Name : userSid);

            return $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>Starts Predator Control at logon with administrator rights.</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <UserId>{user}</UserId>
      <Delay>PT0S</Delay>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>{user}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>Parallel</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>2</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{exe}</Command>
      <Arguments>-hidden</Arguments>
      <WorkingDirectory>{dir}</WorkingDirectory>
    </Exec>
  </Actions>
</Task>";
        }

        private static string BuildBootTaskXml()
        {
            string exePath = Environment.ProcessPath ?? Application.ExecutablePath;
            if (exePath.Contains("PredatorSense", StringComparison.OrdinalIgnoreCase) ||
                exePath.Contains("Prerequisites", StringComparison.OrdinalIgnoreCase) ||
                exePath.Contains("NitroSense", StringComparison.OrdinalIgnoreCase))
            {
                string[] candidates =
                {
                    @"C:\Users\youse\Downloads\PredatorControl\PredatorControl-standalone.exe",
                    @"C:\Users\youse\Downloads\PredatorControl-standalone.exe",
                    @"C:\Users\youse\Downloads\PredatorControl-Unpacked\PredatorControlApp.exe"
                };
                foreach (var c in candidates)
                {
                    if (File.Exists(c)) { exePath = c; break; }
                }
            }
            string workingDir = Path.GetDirectoryName(exePath) ?? "";
            string exe = System.Security.SecurityElement.Escape(exePath);
            string dir = System.Security.SecurityElement.Escape(workingDir);

            return $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>Enforces Predator Control battery charge limiter at machine boot.</Description>
  </RegistrationInfo>
  <Triggers>
    <BootTrigger>
      <Enabled>true</Enabled>
    </BootTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>S-1-5-18</UserId>
      <LogonType>ServiceAccount</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>true</Hidden>
    <ExecutionTimeLimit>PT30S</ExecutionTimeLimit>
    <Priority>1</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{exe}</Command>
      <Arguments>--boot-limit</Arguments>
      <WorkingDirectory>{dir}</WorkingDirectory>
    </Exec>
  </Actions>
</Task>";
        }

        private static int RunSchtasks(string args)
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
                if (p == null) return -1;
                p.WaitForExit(15000);
                return p.HasExited ? p.ExitCode : -1;
            }
            catch { return -1; }
        }

        private static void RemoveLegacyRunKey()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                key?.DeleteValue(StartupTaskName, false);
            }
            catch { }
        }

        private static void MigrateLegacyStartup()
        {
            bool hadLegacy;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
                hadLegacy = key?.GetValue(StartupTaskName) != null;
            }
            catch { return; }

            if (hadLegacy && !IsStartupEnabled())
                SetStartupEnabled(true);
            else if (hadLegacy)
                RemoveLegacyRunKey();
        }

        #endregion

        #region Updates

        private async Task CheckForUpdatesAsync()
        {
            if (_updateCheckRunning) return;
            _updateCheckRunning = true;
            _btnCheckUpdates.Enabled = false;

            try
            {
                var info = await Updater.CheckAsync();

                if (info == null)
                {
                    MessageBox.Show(this, $"You're on the latest version (v{Updater.CurrentText}).",
                        "Predator Control", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                await PromptUpdateAsync(info);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not check for updates:\n{ex.Message}",
                    "Predator Control", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                _updateCheckRunning = false;
                if (!_btnCheckUpdates.IsDisposed) _btnCheckUpdates.Enabled = true;
            }
        }

        private async Task PromptUpdateAsync(UpdateInfo info)
        {
            bool accepted = Updater.ShowNotes(this, "Update available",
                $"Version {info.Version.ToString(3)} is available — you have v{Updater.CurrentText}",
                info.Notes, confirm: true);

            if (!accepted) return;

            try
            {
                _btnCheckUpdates.Enabled = false;
                _btnCheckUpdates.Text = "Downloading update…";
                await Updater.ApplyAsync(info);

                _isClosing = true;
                Application.Exit();
            }
            catch (Exception ex)
            {
                _btnCheckUpdates.Enabled = true;
                _btnCheckUpdates.Text = $"⬇  Check for Updates  (v{Updater.CurrentText})";
                MessageBox.Show(this, $"Update failed:\n{ex.Message}",
                    "Predator Control", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        #endregion
    }
}
