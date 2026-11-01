// ====================================================================================================
// PREDATOR CONTROL · MAIN DASHBOARD, WIN32 MESSAGE DISPATCHER & UI HUB
// File: src/Form1.cs
//
// 📖 C++ / SYSTEMS DEVELOPER ROSETTA STONE:
// - C++ Equivalent : A native Win32 window class with custom WindowProc message loop (HWND, WndProc).
// - Subsystem Role : Orchestrates UI events, telemetry timer dispatch, SCM services, and hardware methods.
// - Win32 Messages : Handles WM_HOTKEY, WM_DPICHANGED, WM_POWERBROADCAST, WM_NCLBUTTONDOWN.
//
// 🔄 C# -> C++ TYPE TRANSLATION REFERENCE:
// - this.Handle                      => HWND (Window Handle)
// - Message m / WndProc(ref Message) => LRESULT CALLBACK WndProc(HWND hwnd, UINT uMsg, WPARAM wParam, LPARAM lParam)
// - BeginInvoke / Invoke             => PostMessage / SendMessage to UI thread message queue
// - System.Windows.Forms.Timer       => SetTimer / WM_TIMER callback
// - Color                            => COLORREF / RGB(r, g, b)
// ====================================================================================================

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using System.ServiceProcess;

namespace PredatorControlApp
{
    [SupportedOSPlatform("windows")]
    public partial class Form1 : Form
    {
        // ============================================================================================
        // SECTION 1: WINDOW INITIALIZATION, DWM DARK MODE & NATIVE WIN32 MESSAGES
        // ============================================================================================

        #region Win32 Interop - DWM & Dark Mode

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        #endregion

        #region Win32 Interop - Window Dragging

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

        #region Win32 Interop - Hotkeys & Foreground

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

        #endregion

        #region Fields

        private readonly WmiController _wmi;
        private readonly SingleInstanceIpc? _ipc;
        private PredatorKeyHook? _predatorKeyHook;
        private TelemetryService? _telemetryService;

        private NotifyIcon _trayIcon = new();
        private ContextMenuStrip _trayMenu = new();
        private readonly ColorDialog _colorPicker = new() { FullOpen = true };

        private int? _cpuTemp, _gpuTemp;
        private int? _cpuFanRpm, _gpuFanRpm;
        private int? _prevCpuTemp, _prevGpuTemp, _prevCpuFanRpm, _prevGpuFanRpm, _prevAuxFanRpm;
        private static readonly string[] _cachedTemps = Enumerable.Range(0, 130).Select(i => $"{i}\u00b0C").ToArray();
        private static string FormatTemp(int? temp) => (temp.HasValue && temp.Value >= 0 && temp.Value < _cachedTemps.Length) ? _cachedTemps[temp.Value] : (temp.HasValue ? $"{temp.Value}\u00b0C" : "--\u00b0C");
        private DarkScrollPanel _contentPanel = null!;
        private Panel _pnlTitle = null!;

        private bool? _isPluggedIn;
        private bool? _pendingPluggedIn;
        private int _powerLineStableTicks;
        private int _telemetryTickCount;
        private bool _isResyncing;
        private bool _isClosing;
        private int _powerRequestSeq;
        private int _fanRequestSeq;
        public static bool IsShuttingDown { get; private set; }
        private PredatorDropDown _cboRgbProfiles = null!;
        private PredatorButton _btnSaveRgbProfile = null!;
        private PredatorButton _btnNewRgbProfile = null!;
        private PredatorButton _btnDeleteRgbProfile = null!;
        private System.Windows.Forms.Timer? _profileSaveFeedbackTimer;
        private bool _isApplyingRgbProfile;

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
        private Label _lblCpuHdr = null!, _lblCpuFanHdr = null!, _lblFanHdr = null!;
        private Label _lblGpuHdr = null!, _lblGpuFanHdr = null!, _lblPowerHdr = null!;
        private Label _lblCpuRpm = null!, _lblGpuRpm = null!;
        private Label? _lblAuxFanHdr, _lblAuxRpm;
        private Label _lblPowerStatus = null!, _lblFanStatus = null!;
        private Label _lblBrightHdr = null!, _lblSpeedHdr = null!;
        private Label _lblCpuFanSpeedHdr = null!, _lblGpuFanSpeedHdr = null!;
        private Label? _lblAuxFanSpeedHdr;
        private Label _lblDisplayHdr = null!;

        private PredatorButton _btnQuiet = null!, _btnBalanced = null!, _btnPerform = null!,
                               _btnTurbo = null!, _btnEco = null!;

        private PredatorButton _btnAutoFan = null!, _btnMaxFan = null!, _btnCustomFan = null!;
        private PredatorButton _btnFixedSpeed = null!, _btnFanCurve = null!;
        private PredatorButton? _activeCustomSubBtn;
        private Panel? _pnlCustomFan;
        private bool _isCustomFanExpanded;
        private int _customFanShiftHeight;
        private PredatorButton _btn60Hz = null!;
        private PredatorButton? _btnMaxHz;
        private ToolStripMenuItem? _trayDisplayMax;

        // GPU Working Mode (MUX Switch)
        private Label _lblGpuModeHdr = null!;
        private PredatorButton _btnGpuOptimus = null!;
        private PredatorButton _btnGpuDiscrete = null!;
        private PredatorButton _btnGpuAuto = null!;
        private PredatorButton? _activeGpuModeBtn;
        private Label? _lblGpuRestartNotice;
        private int? _currentGpuMode;
        private int _gpuCapability;

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
            Color.FromArgb(255, 0, 0),     // Pure Vivid Red
            Color.FromArgb(0, 220, 255),   // Crisp Ice Cyan
            Color.FromArgb(255, 120, 0),   // Warm Amber Orange
            Color.FromArgb(170, 0, 255),   // Vivid Royal Purple
            Color.FromArgb(255, 255, 255)  // Clean White
        };
        private static readonly string[] PresetColorNames = { "Teal", "Red", "Cyan", "Amber", "Purple", "White" };

        private PredatorSlider _brightnessSlider = null!, _speedSlider = null!;
        private PredatorSlider _cpuFanSlider = null!, _gpuFanSlider = null!;
        private PredatorSlider? _auxFanSlider;

        private FanCurveForm? _fanCurveForm;
        private bool _fanCurveEnabled;
        private List<Point> _cpuCurvePoints = new() { new(30,10), new(45,15), new(55,30), new(65,50), new(72,65), new(80,80), new(88,92), new(95,100) };
        private List<Point> _gpuCurvePoints = new() { new(30,10), new(45,15), new(55,30), new(65,50), new(72,65), new(80,80), new(88,92), new(95,100) };
        private int _lastCurveCpuSpeed = -1;
        private int _lastCurveGpuSpeed = -1;
        private int _lastCurveAuxSpeed = -1;
        private int? _auxFanRpm;
        private readonly Queue<int> _cpuTempHistory = new(3);
        private readonly Queue<int> _gpuTempHistory = new(3);
        private long _lastCpuDownstepTick = 0;
        private long _lastGpuDownstepTick = 0;
        private long _lastAuxDownstepTick = 0;

        private PredatorButton? _activePowerBtn, _activeFanBtn, _activeDisplayBtn;
        private bool _isUpdatingBattery;

        private PredatorDropDown _cboAcProfile = null!, _cboBatteryProfile = null!;
        private Label _lblAcProfileHdr = null!, _lblBatteryProfileHdr = null!;
        internal static readonly byte[] AcProfileValues = { 0xFF, 0x00, 0x01, 0x04, 0x05 };
        internal static readonly byte[] BatteryProfileValues = { 0xFF, 0x00, 0x01, 0x06 };
        internal static readonly byte[] NitroAcProfileValues = { 0xFF, 0x00, 0x01, 0x04 };
        internal static readonly byte[] NitroBatteryProfileValues = { 0xFF, 0x00, 0x01 };

        private PredatorToggle? _switchCoolBoost;
        private Label? _lblCoolBoost;
        private ToolStripMenuItem? _trayCoolBoost;

        private PredatorToggle _switchBatteryLimit = null!;
        private Label _lblBatteryStatus = null!;
        private Label? _lblBatteryHealth;

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
        private PredatorToggle? _switchLcdOverdrive;
        private Label? _lblLcdOverdrive;

        private PredatorToggle _switchBacklight30s = null!;
        private Label _lblBacklight30s = null!;

        private PredatorToggle _switchWinKeyLock = null!;
        private ToolStripMenuItem? _trayWinKey;
        private Label? _lblJelliEnabled;
        private PredatorToggle? _switchJelliEnabled;
        private ToolStripMenuItem? _trayJelliToggle;
        private Label? _lblOptimizeServices;
        private PredatorToggle? _switchOptimizeServices;
        private Label? _lblOptimizeCounter;
        private PredatorButton? _btnAutoRepair;
        private Label _lblWinKeyLock = null!;

        private PredatorToggle? _switchBootSound;
        private Label? _lblBootSound;
        private PredatorToggle? _switchUsbCharging;
        private Label? _lblUsbCharging;
        private PredatorToggle? _switchAutoCoolBoost;
        private Label? _lblAutoCoolBoost;
        private PredatorDropDown? _cboTurboAction;
        private PredatorDropDown? _cboPredatorAction;

        private GameOverlayForm? _overlayForm;
        private ToolStripMenuItem? _trayOverlay;
        private PredatorToggle? _switchOverlay;
        private Label? _lblOverlay;
        private PredatorButton _btnOverlayLight = null!;
        private PredatorButton _btnOverlayDefault = null!;
        private PredatorButton _btnOverlayFull = null!;
        private PredatorButton _btnOverlayComplete = null!;
        private PredatorButton _btnOverlaySettings = null!;

        private static readonly string[] RgbModeNames = { "Static", "Breathing", "Neon", "Wave", "Shifting", "Zoom", "Meteor", "Twinkling", "Off" };

        private ToolStripMenuItem _trayPowerQuiet = null!, _trayPowerBal = null!, _trayPowerPerf = null!,
                                  _trayPowerTurbo = null!, _trayPowerEco = null!;
        private ToolStripMenuItem _trayFanAuto = null!, _trayFanMax = null!, _trayFanCustom = null!;
        private ToolStripMenuItem _trayDisplay60 = null!;
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

        #region Constructor & Lifecycle

        public Form1(SingleInstanceIpc? ipc = null, WmiController? hardware = null)
        {
            _wmi = hardware ?? new WmiController();
            _ipc = ipc;
            InitializeComponent();
            DoubleBuffered = true;

            _dpiScale = DeviceDpi / 96f;

            _internalDisplayGdiName = DisplayCcdController.GetInternalDisplayGdiName();
            _maxHz = DisplayCcdController.GetMaxRefreshRate(_internalDisplayGdiName);

            BuildUI();
            BuildTrayMenu();
            SetupSystemTray();

            int currentHz = DisplayCcdController.GetCurrentRefreshRate(_internalDisplayGdiName);
            if (currentHz <= 60 || _btnMaxHz == null || _trayDisplayMax == null)
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
            ApplyCapabilityUi();
            StartFanSafetyWatchdog();

            try
            {
                _predatorKeyHook = new PredatorKeyHook(_wmi.Capabilities.ChassisFamily);
                _predatorKeyHook.PredatorKeyPressed += () =>
                {
                    if (InvokeRequired) BeginInvoke(new Action(HandlePredatorOrNitroKey));
                    else HandlePredatorOrNitroKey();
                };
                _predatorKeyHook.ModeKeyPressed += () =>
                {
                    if (InvokeRequired) BeginInvoke(new Action(HandleTurboOrModeKey));
                    else HandleTurboOrModeKey();
                };
            }
            catch (Exception ex)
            {
                Program.Report(ex, false);
            }

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
            SystemEvents.SessionEnding += OnSessionEnding;
            FormClosed += (s, e) =>
            {
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                SystemEvents.SessionEnding -= OnSessionEnding;
                ThemeManager.ThemeChanged -= OnThemeChanged;
                try { _overlayForm?.Dispose(); } catch { }
                try { NvmlGpuMonitor.Shutdown(); } catch { }
            };

            ThemeManager.ThemeChanged += OnThemeChanged;
            ApplyCurrentTheme();

            int jelliPref = 1;
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PredatorControl");
                if (k != null) jelliPref = GetInt(k, "JelliEnabled", 1, 0, 1);
            }
            catch { }

            if (jelliPref == 0 || Environment.GetCommandLineArgs().Contains("--legacy-ui"))
            {
                _jelliFallback = true;
                Opacity = 1;
            }
            else
            {
                Opacity = 0;
            }

            Shown += async (s, e) =>
            {
                EnableDarkTitleBar();
                Updater.ShowPendingNotes(this);
                if (Environment.CommandLine.Contains("-hidden")) HideApp();
                CheckPredatorSenseConflict();
                UpdateBatteryHealthUI();

                if (IsServicesOptimizationEnabled())
                    OptimizeAcerServices();

                if (_wmi.Keyboard.SupportsRgb && LoadState("KeyboardLightingConfigured", 0) == 1)
                _ = Task.Run(async () =>
                {
                    int[] delays = { 1000, 3000, 6000, 10000 };
                    foreach (int ms in delays)
                    {
                        await Task.Delay(ms);
                        if (IsDisposed) break;
                        BeginInvoke(() =>
                        {
                            try
                            {
                                if (!IsDisposed)
                                {
                                    string active = RgbProfileManager.GetActiveProfile();
                                    var profiles = RgbProfileManager.LoadProfiles();
                                    var prof = profiles.FirstOrDefault(p => string.Equals(p.Name, active, StringComparison.OrdinalIgnoreCase));
                                    if (prof != null)
                                    {
                                        ApplyRgbProfile(prof);
                                    }
                                    else if (_rgbDropDown != null)
                                    {
                                        ApplyRgbModeFromDropdown(SelectedRgbMode);
                                    }
                                }
                            }
                            catch { }
                        });
                    }
                });

                if (!_jelliFallback)
                {
                    await StartJelliAsync();
                }

                // Trim working set 3 seconds after startup once JIT compilation and WebView2 initialization settle.
                _ = Task.Run(async () =>
                {
                    await Task.Delay(3000);
                    if (!IsDisposed) Program.TrimWorkingSet(forceGc: true);
                });
            };
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason is CloseReason.WindowsShutDown or CloseReason.ApplicationExitCall or CloseReason.TaskManagerClosing)
            {
                IsShuttingDown = true;
                _isClosing = true;
            }

            if (!_isClosing)
            {
                e.Cancel = true;
                HideApp();
            }
            else
            {
                IsShuttingDown = true;
                SystemEvents.SessionEnding -= OnSessionEnding;
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                ThemeManager.ThemeChanged -= OnThemeChanged;
                _trayIcon.Visible = false;
                try { _colorPicker?.Dispose(); } catch { }
                try { _telemetryService?.Dispose(); } catch { }
                try { _predatorKeyHook?.Dispose(); } catch { }
                try { _gameSync?.Dispose(); } catch { }
                try { _jelli?.Dispose(); } catch { }
                try { _overlayForm?.Dispose(); } catch { }
                try { _wmi?.RecoverAutomaticFans(); } catch { }
                try { _wmi?.Dispose(); } catch { }
                try { _ipc?.Dispose(); } catch { }
                base.OnFormClosing(e);
            }
        }

        private void OnSessionEnding(object? sender, SessionEndingEventArgs e)
        {
            IsShuttingDown = true;
            _isClosing = true;
            try { _jelli?.Dispose(); } catch { }
            try { _telemetryService?.Dispose(); } catch { }
            try { _predatorKeyHook?.Dispose(); } catch { }
            try { _gameSync?.Dispose(); } catch { }
            try { _overlayForm?.Dispose(); } catch { }
            try { NvmlGpuMonitor.Shutdown(); } catch { }
            try { _wmi?.Dispose(); } catch { }
            try { _ipc?.Dispose(); } catch { }
        }

        private void CheckPredatorSenseConflict()
        {
            try
            {
                var procs = Process.GetProcessesByName("PredatorSense")
                    .Concat(Process.GetProcessesByName("NitroSense"))
                    .ToArray();
                if (procs.Length > 0)
                {
                    string senseName = procs[0].ProcessName;
                    var res = MessageBox.Show(
                        $"{senseName} is currently running in the background.\n\n" +
                        $"Running both {senseName} and Predator Control simultaneously causes hardware conflicts on the laptop's Embedded Controller (EC), causing keyboard lighting and sensor glitches.\n\n" +
                        $"Would you like to close {senseName} now to ensure smooth operation?",
                        $"{senseName} Conflict Detected",
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

        #endregion

        #region SCM Service Manager (Bloatware Optimization & Auto-Repair)

        private bool IsServicesOptimizationEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PredatorControl");
                if (key != null && key.GetValue("ServicesOptimized") is int v)
                    return v == 1;

                return false;
            }
            catch { return false; }
        }

        private static void SetServicesOptimizationEnabled(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\PredatorControl");
                key.SetValue("ServicesOptimized", enabled ? 1 : 0);
            }
            catch { }
        }

        internal static string[] GetBloatServices(AcerChassisFamily chassis) =>
            chassis == AcerChassisFamily.Unsupported ? Array.Empty<string>() : OemServiceManager.OptimizationCandidates.ToArray();

        private static void RunScmCommand(string exe, string args)
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = SecureLaunch.System32(exe),
                    Arguments = args,
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                p?.WaitForExit(3000);
            }
            catch { }
        }

        private static void SetServiceStartup(string serviceName, bool disable)
        {
            string mode = disable ? "disabled" : "auto";
            RunScmCommand("sc.exe", $"config \"{serviceName}\" start= {mode}");
            try
            {
                using var reg = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}", true);
                reg?.SetValue("Start", disable ? 4 : 2, RegistryValueKind.DWord);
            }
            catch { }
        }

        private static void StopServiceSafe(string serviceName)
        {
            try
            {
                using var sc = new ServiceController(serviceName);
                if (sc.Status != ServiceControllerStatus.Stopped && sc.Status != ServiceControllerStatus.StopPending)
                {
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(2));
                }
            }
            catch { }
            RunScmCommand("net.exe", $"stop \"{serviceName}\" /y");
        }

        private static void StartServiceSafe(string serviceName)
        {
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
            RunScmCommand("net.exe", $"start \"{serviceName}\"");
        }

        private (int installed, int disabled, int running, int stopped) GetBloatwareCounts()
        {
            int installed = 0;
            int disabled = 0;
            int running = 0;
            int stopped = 0;

            foreach (var svcName in GetBloatServices(_wmi.ChassisFamily))
            {
                try
                {
                    using var sc = new ServiceController(svcName);
                    var status = sc.Status;
                    installed++;

                    bool isDisabled = false;
                    try
                    {
                        isDisabled = (sc.StartType == ServiceStartMode.Disabled);
                    }
                    catch
                    {
                        using var reg = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{svcName}");
                        if (reg != null)
                        {
                            int s = (int)(reg.GetValue("Start", 2) ?? 2);
                            isDisabled = (s == 4);
                        }
                    }

                    if (isDisabled)
                        disabled++;

                    if (status == ServiceControllerStatus.Running)
                        running++;
                    else
                        stopped++;
                }
                catch
                {
                    try
                    {
                        using var reg = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{svcName}");
                        if (reg != null)
                        {
                            installed++;
                            int s = (int)(reg.GetValue("Start", 2) ?? 2);
                            if (s == 4) disabled++;
                            stopped++;
                        }
                    }
                    catch { }
                }
            }

            return (installed, disabled, running, stopped);
        }

        private void UpdateOptimizeCounter()
        {
            if (_lblOptimizeCounter == null || _lblOptimizeCounter.IsDisposed) return;
            try
            {
                var (installed, disabled, running, stopped) = GetBloatwareCounts();
                if (installed == 0)
                {
                    _lblOptimizeCounter.Text = "No Acer OEM services detected";
                    _lblOptimizeCounter.ForeColor = Color.FromArgb(120, 120, 135);
                }
                else if (disabled == installed && running == 0)
                {
                    _lblOptimizeCounter.Text = $"{disabled} of {installed} bloatware services disabled & stopped";
                    _lblOptimizeCounter.ForeColor = Color.FromArgb(0, 230, 180);
                }
                else if (disabled == 0 && running == installed)
                {
                    _lblOptimizeCounter.Text = $"0 of {installed} bloatware services disabled (all {running} running)";
                    _lblOptimizeCounter.ForeColor = Color.FromArgb(120, 120, 135);
                }
                else
                {
                    _lblOptimizeCounter.Text = $"{disabled} of {installed} disabled ({running} running, {stopped} stopped)";
                    _lblOptimizeCounter.ForeColor = Color.FromArgb(255, 189, 46);
                }
            }
            catch { }
        }

        private void OptimizeAcerServices()
        {
            _ = Task.Run(OemServiceManager.OptimizeTelemetryServices);
        }

        private static void RestoreAcerServices()
        {
            Task.Run(() =>
            {
                try
                {
                    foreach (var svcName in OemServiceManager.OptimizationCandidates)
                    {
                        SetServiceStartup(svcName, disable: false);
                        StartServiceSafe(svcName);
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

            if (_jelli != null && !_jelliFallback)
            {
                if (_overlayForm?.Visible == true || !_jelli.DashboardOpen) _jelli.OpenDashboard();
                else _jelli.Collapse();
                return;
            }

            if (Visible && WindowState != FormWindowState.Minimized && Opacity > 0)
            {
                HideApp();
            }
            else
            {
                Opacity = 1;
                ShowApp();
            }
        }

        private GameOverlayForm EnsureOverlayForm()
        {
            if (_overlayForm == null || _overlayForm.IsDisposed)
            {
                _overlayForm = new GameOverlayForm();
                _overlayForm.OverlayStateChanged += (m, s) =>
                {
                    try { if (IsHandleCreated) BeginInvoke(new Action(UpdateOverlayUI)); } catch { }
                };
            }
            return _overlayForm;
        }

        public void SetOverlayVisible(bool visible)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => SetOverlayVisible(visible))); return; }

            if (visible)
            {
                EnsureOverlayForm().ShowOverlay();
            }
            else if (_overlayForm != null && !_overlayForm.IsDisposed)
            {
                _overlayForm.HideOverlay();
            }

            _telemetryService?.SetOverlayActive(visible);
            if (_trayOverlay != null) _trayOverlay.Checked = visible;
            if (_switchOverlay != null && _switchOverlay.Checked != visible) _switchOverlay.Checked = visible;
            SaveState("OverlayEnabled", visible ? 1 : 0);
            _jelli?.SetGaming(visible);
        }

        public void ToggleGameOverlay()
        {
            SetOverlayVisible(_overlayForm == null || _overlayForm.IsDisposed || !_overlayForm.Visible);
        }

        private OverlayMode GetOverlayMode()
        {
            if (_overlayForm != null && !_overlayForm.IsDisposed) return _overlayForm.Mode;
            return (OverlayMode)Math.Clamp(LoadState("OverlayMode", (int)OverlayMode.Default), 0, 3);
        }

        private void SetOverlayMode(OverlayMode mode)
        {
            SaveState("OverlayMode", (int)mode);
            if (_overlayForm != null && !_overlayForm.IsDisposed)
                _overlayForm.SetMode(mode);
            UpdateOverlayUI();
        }

        private void UpdateOverlayUI()
        {
            var mode = GetOverlayMode();

            if (_btnOverlayLight != null) _btnOverlayLight.IsActive = mode == OverlayMode.Light;
            if (_btnOverlayDefault != null) _btnOverlayDefault.IsActive = mode == OverlayMode.Default;
            if (_btnOverlayFull != null) _btnOverlayFull.IsActive = mode == OverlayMode.Full;
            if (_btnOverlayComplete != null) _btnOverlayComplete.IsActive = mode == OverlayMode.Complete;
        }

        private void OpenOverlaySettings()
        {
            using var settingsDlg = new OverlaySettingsForm(EnsureOverlayForm(), SetOverlayVisible);
            settingsDlg.ShowDialog(this);
            UpdateOverlayUI();
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
                else if (id == HOTKEY_ID_OVERLAY)
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
                BeginInvoke(new Action(RefreshExternalMonitorSection));
            }

            base.WndProc(ref m);
        }

        public void ShowApp()
        {
            if (InvokeRequired) { BeginInvoke(new Action(ShowApp)); return; }

            if (_jelli != null && !_jelliFallback) { _jelli.OpenDashboard(); return; }

            Opacity = 1;
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
            UpdateOptimizeCounter();
        }

        public void HideApp()
        {
            if (InvokeRequired) { BeginInvoke(new Action(HideApp)); return; }

            Hide();
            if (_jelli != null && !_jelliFallback)
            {
                if (_jelli.Ready) _jelli.Collapse(); else _jelli.Hide();
                Program.TrimWorkingSet(forceGc: false);
                return;
            }
            _telemetryService?.SetPollingState(false);
            _telemetryService?.SetSensorMask(false);
            Program.TrimWorkingSet(forceGc: false);
        }

        private void SafeInvoke(Action action)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                if (IsHandleCreated)
                {
                    try { BeginInvoke(action); } catch { }
                }
                else
                {
                    EventHandler? handler = null;
                    handler = (s, e) =>
                    {
                        HandleCreated -= handler;
                        try { BeginInvoke(action); } catch { }
                    };
                    HandleCreated += handler;
                }
            }
            else
            {
                action();
            }
        }

        #endregion
    }
}
