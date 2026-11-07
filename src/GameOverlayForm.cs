using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace PredatorControlApp
{
    public enum OverlayMode
    {
        Light = 0,    // Light: FPS + GPU (temp, power) + CPU (temp, power)
        Default = 1,  // Default: Light + Fan RPMs + 60s Sparkline Graph + GPU/CPU Power Column
        Full = 2,     // Full: Default + GPU/CPU Load % with bars + VRAM/RAM with bars
        Complete = 3  // Complete: All metrics including Battery, Labels, Detailed stats
    }

    [SupportedOSPlatform("windows")]
    public sealed class GameOverlayForm : Form
    {
        #region Win32 Interop

        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_TOPMOST = 0x00000008;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int GWL_EXSTYLE = -20;

        private const int VK_CONTROL = 0x11;
        private const int VK_SHIFT = 0x10;

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;

        private const int SW_SHOWNOACTIVATE = 4;

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int cx, int cy);

        [DllImport("user32.dll")]
        private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        #endregion

        private readonly EtwFpsMonitor _fpsMonitor;
        private readonly System.Windows.Forms.Timer _renderTimer = new();
        private readonly System.Windows.Forms.Timer _keyStateTimer = new();

        private bool _isClickThrough = true;
        private float _dpiScale = 1.0f;
        private float _fontDpiScale = 0f;
        private float _layoutFit = 1f;
        private Task? _fpsWorker;
        private uint _foregroundPid;
        private bool _foregroundExcluded = true;
        private long _sampledAt;
        private long _historyAt;
        private bool _toastVisible;
        private float _fpsWidth, _labelWidth, _temperatureWidth, _rpmWidth, _clockWidth, _powerWidth, _loadWidth, _memoryWidth;

        // Current display mode and scale
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public OverlayMode Mode { get; private set; } = OverlayMode.Default;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int ScalePercent { get; private set; } = 100;

        public event Action<OverlayMode, int>? OverlayStateChanged;

        // Element visibility toggles
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool ShowFps { get; set; } = true;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool ShowTemperatures { get; set; } = true;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool ShowFan { get; set; } = true;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool ShowCpuClock { get; set; }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool ShowGpuClock { get; set; }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool ShowChart { get; set; } = true;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool ShowPower { get; set; } = true;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool ShowLoad { get; set; } = true;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool ShowRam { get; set; } = false;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool ShowBattery { get; set; } = true;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool ShowLabels { get; set; } = true;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color CpuColor { get; set; } = Color.FromArgb(255, 0, 180, 216);

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color GpuColor { get; set; } = Color.FromArgb(255, 0, 229, 117);

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int TransparencyPercent { get; set; } = 88;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool OverlayOnlyInGames { get; set; } = false;

        internal int? CurrentFps => _fps;

        // Telemetry State
        private int? _fps;
        private int? _cpuTemp;
        private int? _gpuTemp;
        private int? _cpuFanRpm;
        private int? _gpuFanRpm;
        private int? _cpuClockMhz;
        private int? _gpuClockMhz;
        private int? _cpuUsage;
        private int? _gpuUsage;
        private float? _gpuPowerW;
        private float? _cpuPowerW;
        private float? _vramUsedGb;
        private float? _vramTotalGb;
        private float? _ramUsedGb;
        private float? _ramTotalGb;
        private float? _batteryPercent;
        private bool _isCharging;
        private bool _isPluggedIn;

        // 60-second rolling history for sparkline chart
        private const int HistoryLength = 60;
        private readonly float[] _cpuHistory = new float[HistoryLength];
        private readonly float[] _gpuHistory = new float[HistoryLength];
        private int _historyHead = 0;
        private int _historyCount = 0;

        // Visual Colors (matching clean telemetry aesthetic & Jelli styling)
        private static readonly Color GpuGreen = Color.FromArgb(255, 0, 229, 117);  // Neon Mint/Green (#00E575)
        private static readonly Color CpuTeal = Color.FromArgb(255, 0, 180, 216);   // Sky Cyan/Blue (#00B4D8 / #38B6FF)
        private static readonly Color DimGpu = Color.FromArgb(170, 0, 190, 95);
        private static readonly Color DimCpu = Color.FromArgb(170, 0, 150, 185);
        private static readonly Color DimText = Color.FromArgb(160, 160, 175, 190);
        private static readonly Color BorderColor = Color.FromArgb(50, 255, 255, 255);
        private static readonly Color ActiveBorderColor = Color.FromArgb(255, 255, 184, 0); // Amber feedback while interactive
        private static readonly Color ChartBg = Color.FromArgb(220, 6, 8, 12);
        private static readonly Color OverlayBg = Color.FromArgb(10, 12, 16);
        private static readonly StringFormat RightAligned = new() { Alignment = StringAlignment.Far, FormatFlags = StringFormatFlags.NoWrap };

        private Font? _fontFps;
        private Font? _fontMain;
        private Font? _fontSmall;
        private Font? _fontLabel;
        private Font? _fontSuperscript;
        private Font? _fontToast;

        // Gesture state
        private Point _mouseDownPos;
        private DateTime _mouseDownTime;

        // Transient Toast
        private string? _toastText;
        private DateTime _toastExpiry;

        // Jelli Settings & Corner Position
        private JelliSettings _jelliSettings = JelliSettings.Load();
        private bool _hasCustomPosition;

        // Desktop shell executables where FPS monitoring is paused
        private static readonly HashSet<string> ExcludedProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "explorer", "ShellExperienceHost", "SearchHost", "StartMenuExperienceHost",
            "Taskmgr", "SystemSettings", "ApplicationFrameHost", "PredatorControlApp",
            "PredatorControl-standalone", "PredatorControl-win-x64"
        };

        public GameOverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None; // Fonts and dimensions are explicitly scaled together.
            BackColor = OverlayBg;
            Opacity = 0.88; // Translucent dark glass look matching Jelli aesthetic

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.DoubleBuffer |
                     ControlStyles.OptimizedDoubleBuffer, true);

            _fpsMonitor = new EtwFpsMonitor();

            _renderTimer.Interval = 250;
            _renderTimer.Tick += (s, e) => OnRenderTick();

            _keyStateTimer.Interval = 80;
            _keyStateTimer.Tick += (s, e) => CheckDragModifierKeys();

            ApplyModePreset(Mode);
            LoadPersistedSettings();
            LoadPersistedPosition();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_NOACTIVATE;
                cp.ExStyle |= WS_EX_TOOLWINDOW;
                cp.ExStyle |= WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => true;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SetClickThrough(true);
            UpdateDpiAndLayout();
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            _dpiScale = e.DeviceDpiNew / 96.0f;
            UpdateDpiAndLayout();
            Invalidate();
        }

        public void ToggleOverlay()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(ToggleOverlay));
                return;
            }

            if (Visible)
                HideOverlay();
            else
                ShowOverlay();
        }

        public void ShowOverlay()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(ShowOverlay));
                return;
            }

            UpdateDpiAndLayout();
            if (!Visible)
            {
                ShowWindow(Handle, SW_SHOWNOACTIVATE);
                Visible = true;
            }
            if (_fpsWorker == null || _fpsWorker.IsCompleted)
                _fpsWorker = Task.Run(() =>
                {
                    try { _fpsMonitor.Start(0); }
                    catch (Exception ex) { Debug.WriteLine($"FPS tracing unavailable: {ex.Message}"); }
                });
            SetClickThrough(true);
            _renderTimer.Start();
            _keyStateTimer.Start();
            Invalidate();
        }

        public void HideOverlay()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(HideOverlay));
                return;
            }

            _renderTimer.Stop();
            _keyStateTimer.Stop();
            _fpsMonitor.TargetPid = 0;
            _fps = null;
            SetClickThrough(true);
            Hide();
        }

        public void CycleMode()
        {
            var nextMode = (OverlayMode)(((int)Mode + 1) % 4);
            SetMode(nextMode);
        }

        public void SetMode(OverlayMode mode)
        {
            if (!Enum.IsDefined(mode)) return;
            Mode = mode;
            ApplyModePreset(mode);
            SaveSettings();
            UpdateDpiAndLayout();
            ShowToast(mode.ToString().ToUpperInvariant());
            OverlayStateChanged?.Invoke(Mode, ScalePercent);
            Invalidate();
        }

        private void ApplyModePreset(OverlayMode mode)
        {
            switch (mode)
            {
                case OverlayMode.Light:
                    ShowFps = true;
                    ShowTemperatures = true;
                    ShowFan = false;
                    ShowCpuClock = false;
                    ShowGpuClock = false;
                    ShowChart = false;
                    ShowPower = true;
                    ShowLoad = false;
                    ShowRam = false;
                    ShowBattery = false;
                    ShowLabels = true;
                    break;
                case OverlayMode.Default:
                    ShowFps = true;
                    ShowTemperatures = true;
                    ShowFan = true;
                    ShowCpuClock = false;
                    ShowGpuClock = false;
                    ShowChart = true;
                    ShowPower = true;
                    ShowLoad = false;
                    ShowRam = false;
                    ShowBattery = false;
                    ShowLabels = true;
                    break;
                case OverlayMode.Full:
                    ShowFps = true;
                    ShowTemperatures = true;
                    ShowFan = true;
                    ShowCpuClock = false;
                    ShowGpuClock = false;
                    ShowChart = true;
                    ShowPower = true;
                    ShowLoad = true;
                    ShowRam = false;
                    ShowBattery = true;
                    ShowLabels = true;
                    break;
                case OverlayMode.Complete:
                    ShowFps = true;
                    ShowTemperatures = true;
                    ShowFan = true;
                    ShowCpuClock = false;
                    ShowGpuClock = false;
                    ShowChart = true;
                    ShowPower = true;
                    ShowLoad = true;
                    ShowRam = true;
                    ShowBattery = true;
                    ShowLabels = true;
                    break;
            }
        }

        public void SetScale(int scalePercent)
        {
            ScalePercent = Math.Clamp(scalePercent, 50, 300);
            SaveSettings();
            UpdateDpiAndLayout();
            ShowToast($"{ScalePercent}%");
            OverlayStateChanged?.Invoke(Mode, ScalePercent);
            Invalidate();
        }

        public void SetTransparency(int percent)
        {
            TransparencyPercent = Math.Clamp(percent, 10, 100);
            Opacity = TransparencyPercent / 100.0;
            SaveSettings();
            Invalidate();
        }

        public void RefreshLayout()
        {
            if (IsHandleCreated) UpdateDpiAndLayout();
            Invalidate();
        }

        public void ResetToDefaults()
        {
            CpuColor = Color.FromArgb(255, 0, 180, 216);
            GpuColor = Color.FromArgb(255, 0, 229, 117);
            ScalePercent = 100;
            TransparencyPercent = 88;
            Opacity = 0.88;
            OverlayOnlyInGames = false;
            ShowCpuClock = false;
            ShowGpuClock = false;
            _hasCustomPosition = false;
            SetMode(OverlayMode.Default);
            ApplyCorner();
            SaveSettings();
            SavePosition();
            RefreshLayout();
            ShowToast("OVERLAY RESET");
        }

        public void ShowToast(string text)
        {
            _toastText = text;
            _toastExpiry = DateTime.UtcNow.AddMilliseconds(1200);
            _toastVisible = true;
            Invalidate();
        }

        public void UpdateSnapshot(TelemetrySnapshot snapshot)
        {
            if (!Visible) return;
            long now = Stopwatch.GetTimestamp();
            _sampledAt = snapshot.SampledAtTimestamp > 0 ? snapshot.SampledAtTimestamp : now;
            bool fresh = OverlayPolicy.IsFresh(_sampledAt, now, Stopwatch.Frequency);
            if (!fresh) snapshot = new TelemetrySnapshot(null, null, null, null, PowerLineStatus.Unknown);
            bool repaint = ShowTemperatures && (_cpuTemp != snapshot.CpuTemp || _gpuTemp != snapshot.GpuTemp) ||
                ShowFan && (_cpuFanRpm != snapshot.CpuFanRpm || _gpuFanRpm != snapshot.GpuFanRpm) ||
                ShowCpuClock && _cpuClockMhz != OverlayPolicy.ClockMhz(snapshot.CpuClockMhz) ||
                ShowGpuClock && _gpuClockMhz != OverlayPolicy.ClockMhz(snapshot.GpuClockMhz) ||
                ShowLoad && (_cpuUsage != snapshot.CpuUsage || _gpuUsage != snapshot.GpuUsage) ||
                ShowPower && (_cpuPowerW != snapshot.CpuPowerW || _gpuPowerW != snapshot.GpuPowerW) ||
                ShowRam && (_ramUsedGb != snapshot.RamUsedGb || _ramTotalGb != snapshot.RamTotalGb || _vramUsedGb != snapshot.VramUsedGb || _vramTotalGb != snapshot.VramTotalGb) ||
                ShowBattery && (_batteryPercent != snapshot.BatteryPercent || _isCharging != snapshot.IsCharging);

            _cpuTemp = fresh ? snapshot.CpuTemp : null;
            _gpuTemp = fresh ? snapshot.GpuTemp : null;
            _cpuFanRpm = fresh ? snapshot.CpuFanRpm : null;
            _gpuFanRpm = fresh ? snapshot.GpuFanRpm : null;
            _cpuClockMhz = fresh ? OverlayPolicy.ClockMhz(snapshot.CpuClockMhz) : null;
            _gpuClockMhz = fresh ? OverlayPolicy.ClockMhz(snapshot.GpuClockMhz) : null;
            _cpuUsage = fresh ? snapshot.CpuUsage : null;
            _gpuUsage = fresh ? snapshot.GpuUsage : null;
            _gpuPowerW = fresh ? snapshot.GpuPowerW : null;
            _cpuPowerW = fresh ? snapshot.CpuPowerW : null;
            _vramUsedGb = fresh ? snapshot.VramUsedGb : null;
            _vramTotalGb = fresh ? snapshot.VramTotalGb : null;
            _ramUsedGb = fresh ? snapshot.RamUsedGb : null;
            _ramTotalGb = fresh ? snapshot.RamTotalGb : null;
            _batteryPercent = fresh ? snapshot.BatteryPercent : null;
            _isCharging = snapshot.IsCharging;
            _isPluggedIn = snapshot.PowerLine == PowerLineStatus.Online;

            if (repaint) Invalidate();
        }

        private void PushHistory(long now)
        {
            if (_historyAt == 0) _historyAt = now - Stopwatch.Frequency;
            int elapsed = (int)Math.Min(HistoryLength, Math.Max(0, (now - _historyAt) / Stopwatch.Frequency));
            if (elapsed == 0) return;
            for (int i = 0; i < elapsed; i++)
            {
                // A missed second is a gap, never a copy of an old temperature.
                _cpuHistory[_historyHead] = i == elapsed - 1 ? _cpuTemp ?? 0 : 0;
                _gpuHistory[_historyHead] = i == elapsed - 1 ? _gpuTemp ?? 0 : 0;
                _historyHead = (_historyHead + 1) % HistoryLength;
                if (_historyCount < HistoryLength) _historyCount++;
            }
            _historyAt = now;
        }

        private void OnRenderTick()
        {
            bool repaint = false;
            try
            {
                IntPtr fgWnd = GetForegroundWindow();
                GetWindowThreadProcessId(fgWnd, out uint procId);
                if (_foregroundPid != procId)
                {
                    _foregroundPid = procId;
                    _foregroundExcluded = true;
                    if (procId > 0 && procId != Environment.ProcessId)
                    {
                        try
                        {
                            using var proc = Process.GetProcessById((int)procId);
                            _foregroundExcluded = ExcludedProcesses.Contains(proc.ProcessName);
                        }
                        catch { }
                    }
                }
                _fpsMonitor.TargetPid = _foregroundExcluded ? 0 : (int)procId;
                if (!_hasCustomPosition && string.IsNullOrEmpty(_jelliSettings.OverlayMonitor) && !_foregroundExcluded && fgWnd != IntPtr.Zero)
                {
                    var activeScreen = Screen.FromHandle(fgWnd);
                    bool monitorChanged = activeScreen.DeviceName != Screen.FromControl(this).DeviceName;
                    ApplyCorner(activeScreen);
                    if (monitorChanged) UpdateDpiAndLayout();
                }
            }
            catch { _fpsMonitor.TargetPid = 0; }

            long now = Stopwatch.GetTimestamp();
            if (_sampledAt > 0 && !OverlayPolicy.IsFresh(_sampledAt, now, Stopwatch.Frequency))
            {
                _sampledAt = 0;
                _cpuTemp = _gpuTemp = _cpuFanRpm = _gpuFanRpm = _cpuUsage = _gpuUsage = null;
                _gpuPowerW = _cpuPowerW = _vramUsedGb = _vramTotalGb = _ramUsedGb = _ramTotalGb = _batteryPercent = null;
                repaint = true;
            }
            int count = _historyHead;
            PushHistory(now);
            repaint |= ShowChart && count != _historyHead;
            double sampled = _fpsMonitor.SampleFps();
            int? fps = sampled > 0.5 ? (int)Math.Round(sampled) : null;
            repaint |= _fps != fps;
            _fps = fps;
            // Presentation events identify graphical activity, not an arbitrary foreground program name.
            double targetOpacity = OverlayOnlyInGames && (_foregroundExcluded || !_fps.HasValue) && _isClickThrough
                ? 0 : Math.Clamp(TransparencyPercent / 100.0, 0.1, 1.0);
            if (Math.Abs(Opacity - targetOpacity) > 0.01)
                Opacity = targetOpacity;
            if (_toastVisible && DateTime.UtcNow >= _toastExpiry) { _toastVisible = false; repaint = true; }
            if (repaint && Opacity > 0) Invalidate();
        }

        private void CheckDragModifierKeys()
        {
            // Support Ctrl + Shift
            bool ctrl = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
            bool shift = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;

            bool interactiveRequested = ctrl && shift;

            if (interactiveRequested && _isClickThrough)
            {
                SetClickThrough(false);
                ShowToast("OVERLAY REPOSITIONING ACTIVE (DRAG TO MOVE)");
                Invalidate();
            }
            else if (!interactiveRequested && !_isClickThrough)
            {
                SetClickThrough(true);
                Invalidate();
            }
        }

        private void SetClickThrough(bool enable)
        {
            _isClickThrough = enable;
            int exStyle = GetWindowLong(Handle, GWL_EXSTYLE);
            if (enable)
                exStyle |= WS_EX_TRANSPARENT | WS_EX_LAYERED;
            else
                exStyle = (exStyle & ~WS_EX_TRANSPARENT) | WS_EX_LAYERED;

            SetWindowLong(Handle, GWL_EXSTYLE, exStyle);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (!_isClickThrough)
            {
                if (e.Button == MouseButtons.Middle)
                {
                    // Middle click: reset scale to 100%
                    SetScale(100);
                    return;
                }

                if (e.Button == MouseButtons.Left)
                {
                    _mouseDownPos = Cursor.Position;
                    _mouseDownTime = DateTime.UtcNow;

                    ReleaseCapture();
                    SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);

                    // Discriminate click vs drag
                    Point curPos = Cursor.Position;
                    double dist = Math.Sqrt(Math.Pow(curPos.X - _mouseDownPos.X, 2) + Math.Pow(curPos.Y - _mouseDownPos.Y, 2));
                    double duration = (DateTime.UtcNow - _mouseDownTime).TotalMilliseconds;

                    if (dist < 6 && duration < 450)
                    {
                        // Click: cycle display mode (Light, Default, Full)
                        CycleMode();
                    }
                    else
                    {
                        // Drag: save coordinates
                        Location = OverlayPolicy.ClampPosition(Location, Size, Screen.FromControl(this).WorkingArea);
                        _hasCustomPosition = true;
                        SavePosition();
                    }

                    CheckDragModifierKeys();
                }
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!_isClickThrough)
            {
                // Scroll: scale from 50% to 300% in 10% steps
                int step = e.Delta > 0 ? 10 : -10;
                SetScale(ScalePercent + step);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0021) // WM_MOUSEACTIVATE: editing the overlay must not steal game focus.
            {
                m.Result = (IntPtr)3; // MA_NOACTIVATE
                return;
            }
            base.WndProc(ref m);
            if (m.Msg == 0x007E && IsHandleCreated && !IsDisposed) // WM_DISPLAYCHANGE
                BeginInvoke(new Action(UpdateDpiAndLayout));
        }

        private float GetTotalScale()
        {
            // Fully dynamic to display scale (DPI) and user scale percent
            return OverlayPolicy.Scale((int)Math.Round(_dpiScale * 96), ScalePercent) * _layoutFit;
        }

        private void UpdateDpiAndLayout()
        {
            if (!IsHandleCreated) return;

            // Authoritative monitor DPI via PerMonitorV2
            float formDpi = DeviceDpi > 0 ? DeviceDpi : 96.0f;
            _dpiScale = formDpi / 96.0f;
            if (_dpiScale < 0.5f) _dpiScale = 1.0f;

            _layoutFit = 1f;
            float scale = GetTotalScale();
            EnsureFonts();

            // Calculate precise required size using Graphics.MeasureString so no clipping ever occurs
            int w, h;
            using (var g = CreateGraphics())
            {
                float calculatedW = CalculateRequiredWidth(g, scale);
                w = Math.Max((int)Math.Ceiling(calculatedW), (int)Math.Ceiling(140f * scale));
                h = (int)Math.Ceiling(46f * scale);
                var area = Screen.FromControl(this).WorkingArea;
                _layoutFit = Math.Min(1f, Math.Min((area.Width - 16f) / w, (area.Height - 16f) / h));
                if (_layoutFit < 1f)
                {
                    scale = GetTotalScale();
                    EnsureFonts();
                    w = (int)Math.Ceiling(CalculateRequiredWidth(g, scale));
                    h = (int)Math.Ceiling(46f * scale);
                }
            }

            Size = new Size(w, h);

            int radius = Math.Max(4, (int)(8 * scale));
            IntPtr rgn = CreateRoundRectRgn(0, 0, w + 1, h + 1, radius, radius);
            if (rgn != IntPtr.Zero && SetWindowRgn(Handle, rgn, true) == 0) DeleteObject(rgn);

            if (!_hasCustomPosition)
            {
                ApplyCorner();
            }
            else Location = OverlayPolicy.ClampPosition(Location, Size, Screen.FromControl(this).WorkingArea);
        }

        private float CalculateRequiredWidth(Graphics g, float scale)
        {
            float curX = 10f * scale;
            _fpsWidth = Math.Max(38f * scale, g.MeasureString("1000", _fontFps!).Width + 6f * scale);
            _labelWidth = ShowLabels ? g.MeasureString("GPU: ", _fontLabel!).Width : 0f;
            _temperatureWidth = g.MeasureString("125°", _fontMain!).Width;
            _rpmWidth = g.MeasureString(" 15000", _fontMain!).Width;
            _clockWidth = g.MeasureString("10000 MHz", _fontMain!).Width + 2f * scale;
            _powerWidth = g.MeasureString("1000.0W", _fontMain!).Width;
            _loadWidth = g.MeasureString("100%", _fontMain!).Width;
            _memoryWidth = g.MeasureString("1024.0GB", _fontMain!).Width;

            // Col 1: Big FPS counter
            if (ShowFps)
            {
                curX += _fpsWidth;
            }

            // Col 2: GPU/CPU stats
            if (ShowTemperatures || ShowFan || ShowCpuClock || ShowGpuClock)
            {
                float lblW = _labelWidth;
                float tempW = ShowTemperatures ? _temperatureWidth : 0f;
                float rpmW = ShowFan ? _rpmWidth : 0f;
                float supW = ShowFan ? g.MeasureString("RPM", _fontSuperscript!).Width : 0f;
                float fanW = rpmW + supW;
                float clockW = ShowCpuClock || ShowGpuClock ? _clockWidth : 0f;
                float clockGap = clockW > 0 && fanW > 0 ? 4f * scale : 0f;
                curX += lblW + tempW + fanW + clockGap + clockW + 14f * scale;
            }

            // Col 3: Sparkline chart
            if (ShowChart)
            {
                float chartW = 86f * scale;
                curX += chartW + 10f * scale;
            }

            // Col 4: Power / Battery
            if (ShowPower || ShowBattery)
            {
                curX += _powerWidth + 10f * scale;
            }

            // Col 5: Load %
            if (ShowLoad)
            {
                float barW = 3.5f * scale;
                curX += _loadWidth + 3f * scale + barW + 12f * scale;
            }

            // Col 6: VRAM / RAM
            if (ShowRam)
            {
                float barW = 3.5f * scale;
                curX += _memoryWidth + 3f * scale + barW + 12f * scale;
            }

            return Math.Max(curX + 6f * scale, 100f * scale);
        }

        private void EnsureFonts()
        {
            float scale = GetTotalScale();
            if (Math.Abs(_fontDpiScale - scale) > 0.005f || _fontFps == null || _fontMain == null || _fontSmall == null || _fontLabel == null || _fontSuperscript == null || _fontToast == null)
            {
                _fontFps?.Dispose();
                _fontMain?.Dispose();
                _fontSmall?.Dispose();
                _fontLabel?.Dispose();
                _fontSuperscript?.Dispose();
                _fontToast?.Dispose();

                // Using GraphicsUnit.Pixel guarantees exact linear scaling with display scale (DPI) & user scale
                _fontFps = new Font("Segoe UI", Math.Max(10f, 22.0f * scale), FontStyle.Bold, GraphicsUnit.Pixel);
                _fontMain = new Font("Segoe UI", Math.Max(7f, 11.0f * scale), FontStyle.Bold, GraphicsUnit.Pixel);
                _fontSmall = new Font("Segoe UI", Math.Max(6f, 9.5f * scale), FontStyle.Regular, GraphicsUnit.Pixel);
                _fontLabel = new Font("Segoe UI", Math.Max(6f, 10.0f * scale), FontStyle.Bold, GraphicsUnit.Pixel);
                _fontSuperscript = new Font("Segoe UI", Math.Max(5f, 7.5f * scale), FontStyle.Bold, GraphicsUnit.Pixel);
                _fontToast = new Font("Segoe UI", Math.Max(6f, 10.0f * scale), FontStyle.Bold, GraphicsUnit.Pixel);

                _fontDpiScale = scale;
            }
        }

        public void LoadPersistedSettings()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PredatorControl");
                if (key != null)
                {
                    int modeVal = (int)(key.GetValue("OverlayMode", 1) ?? 1);
                    Mode = (OverlayMode)Math.Clamp(modeVal, 0, 3);
                    ApplyModePreset(Mode);

                    int scaleVal = (int)(key.GetValue("OverlayScale", 100) ?? 100);
                    ScalePercent = Math.Clamp(scaleVal, 50, 300);

                    TransparencyPercent = Math.Clamp((int)(key.GetValue("OverlayTransparency", 88) ?? 88), 10, 100);
                    Opacity = Math.Clamp(TransparencyPercent / 100.0, 0.1, 1.0);

                    ShowFps = (int)(key.GetValue("Overlay_ShowFps", 1) ?? 1) == 1;
                    ShowTemperatures = (int)(key.GetValue("Overlay_ShowTemp", 1) ?? 1) == 1;
                    ShowFan = (int)(key.GetValue("Overlay_ShowFan", (Mode != OverlayMode.Light ? 1 : 0)) ?? 1) == 1;
                    ShowCpuClock = (int)(key.GetValue("Overlay_ShowCpuClock", 0) ?? 0) == 1;
                    ShowGpuClock = (int)(key.GetValue("Overlay_ShowGpuClock", 0) ?? 0) == 1;
                    ShowChart = (int)(key.GetValue("Overlay_ShowChart", (Mode != OverlayMode.Light ? 1 : 0)) ?? 1) == 1;
                    ShowPower = (int)(key.GetValue("Overlay_ShowPower", 1) ?? 1) == 1;
                    ShowLoad = (int)(key.GetValue("Overlay_ShowLoad", (Mode >= OverlayMode.Full ? 1 : 0)) ?? 1) == 1;
                    ShowRam = (int)(key.GetValue("Overlay_ShowRam", (Mode == OverlayMode.Complete ? 1 : 0)) ?? 0) == 1;
                    ShowBattery = (int)(key.GetValue("Overlay_ShowBattery", (Mode >= OverlayMode.Full ? 1 : 0)) ?? 1) == 1;
                    ShowLabels = (int)(key.GetValue("Overlay_ShowLabels", 1) ?? 1) == 1;

                    if (key.GetValue("Overlay_CpuColor") is int cpuArgb)
                        CpuColor = Color.FromArgb(cpuArgb);
                    if (key.GetValue("Overlay_GpuColor") is int gpuArgb)
                        GpuColor = Color.FromArgb(gpuArgb);

                    OverlayOnlyInGames = (int)(key.GetValue("Overlay_OnlyInGames", 0) ?? 0) == 1;
                }
            }
            catch { }
        }

        public void SaveSettings()
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\PredatorControl");
                if (key != null)
                {
                    key.SetValue("OverlayMode", (int)Mode, RegistryValueKind.DWord);
                    key.SetValue("OverlayScale", ScalePercent, RegistryValueKind.DWord);
                    key.SetValue("OverlayTransparency", TransparencyPercent, RegistryValueKind.DWord);

                    key.SetValue("Overlay_ShowFps", ShowFps ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("Overlay_ShowTemp", ShowTemperatures ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("Overlay_ShowFan", ShowFan ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("Overlay_ShowCpuClock", ShowCpuClock ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("Overlay_ShowGpuClock", ShowGpuClock ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("Overlay_ShowChart", ShowChart ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("Overlay_ShowPower", ShowPower ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("Overlay_ShowLoad", ShowLoad ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("Overlay_ShowRam", ShowRam ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("Overlay_ShowBattery", ShowBattery ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("Overlay_ShowLabels", ShowLabels ? 1 : 0, RegistryValueKind.DWord);

                    key.SetValue("Overlay_CpuColor", CpuColor.ToArgb(), RegistryValueKind.DWord);
                    key.SetValue("Overlay_GpuColor", GpuColor.ToArgb(), RegistryValueKind.DWord);
                    key.SetValue("Overlay_OnlyInGames", OverlayOnlyInGames ? 1 : 0, RegistryValueKind.DWord);
                }
            }
            catch { }
        }

        private void LoadPersistedPosition()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PredatorControl");
                if (key != null)
                {
                    if (key.GetValue("OverlayCustomPosition") is int custom && custom == 0) { ApplyCorner(); return; }
                    object? xVal = key.GetValue("OverlayX");
                    object? yVal = key.GetValue("OverlayY");
                    if (xVal != null && yVal != null)
                    {
                        int x = (int)xVal;
                        int y = (int)yVal;
                        Screen scr = Screen.FromPoint(new Point(x, y));
                        var bounds = scr.WorkingArea;
                        x = Math.Clamp(x, bounds.Left, Math.Max(bounds.Left, bounds.Right - Width));
                        y = Math.Clamp(y, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - Height));
                        Location = new Point(x, y);
                        _hasCustomPosition = true;
                        return;
                    }
                }
            }
            catch { }

            ApplyCorner();
        }

        private void SavePosition()
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\PredatorControl");
                if (key != null)
                {
                    key.SetValue("OverlayX", Location.X, RegistryValueKind.DWord);
                    key.SetValue("OverlayY", Location.Y, RegistryValueKind.DWord);
                    key.SetValue("OverlayCustomPosition", _hasCustomPosition ? 1 : 0, RegistryValueKind.DWord);
                }
            }
            catch { }
        }

        internal void Configure(JelliSettings settings)
        {
            bool placementChanged = settings.OverlayCorner != _jelliSettings.OverlayCorner || settings.OverlayMonitor != _jelliSettings.OverlayMonitor;
            var previous = _jelliSettings;
            _jelliSettings = settings;

            // If settings contains overlay mode or scale, apply them
            if (!string.IsNullOrEmpty(settings.OverlayMode))
            {
                var mode = settings.OverlayMode.ToLowerInvariant() switch
                {
                    "light" => OverlayMode.Light,
                    "full" => OverlayMode.Full,
                    "complete" => OverlayMode.Complete,
                    _ => OverlayMode.Default
                };
                if (Mode != mode) { Mode = mode; ApplyModePreset(mode); }
            }

            if (previous.OverlayGraphs != settings.OverlayGraphs) ShowChart = Mode != OverlayMode.Light && settings.OverlayGraphs;
            if (previous.OverlayPower != settings.OverlayPower) ShowPower = settings.OverlayPower;
            if (previous.OverlayUsage != settings.OverlayUsage) ShowLoad = Mode >= OverlayMode.Full && settings.OverlayUsage;

            if (settings.OverlayScale >= 50 && settings.OverlayScale <= 300)
            {
                ScalePercent = settings.OverlayScale;
            }

            if (placementChanged) _hasCustomPosition = false;
            if (IsHandleCreated) UpdateDpiAndLayout();
            if (!_hasCustomPosition) ApplyCorner();
            Invalidate();
        }

        private void ApplyCorner(Screen? activeScreen = null)
        {
            var screen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == _jelliSettings.OverlayMonitor) ?? activeScreen ?? Screen.PrimaryScreen ?? Screen.AllScreens[0];
            var area = screen.WorkingArea;
            int pad = (int)(16 * _dpiScale);
            bool right = _jelliSettings.OverlayCorner.EndsWith("Right", StringComparison.Ordinal);
            bool bottom = _jelliSettings.OverlayCorner.StartsWith("bottom", StringComparison.Ordinal);

            int targetX = right ? area.Right - Width - pad : area.Left + pad;
            int targetY = bottom ? area.Bottom - Height - pad : area.Top + pad;

            targetX = Math.Clamp(targetX, area.Left, Math.Max(area.Left, area.Right - Width));
            targetY = Math.Clamp(targetY, area.Top, Math.Max(area.Top, area.Bottom - Height));

            Location = new Point(targetX, targetY);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            float scale = GetTotalScale();
            int w = ClientSize.Width;
            int h = ClientSize.Height;

            // Outer subtle border
            using (var borderPen = new Pen(!_isClickThrough ? ActiveBorderColor : BorderColor, 1.0f))
            using (var borderPath = GetRoundedPath(new Rectangle(0, 0, w - 1, h - 1), (int)(7 * scale)))
            {
                g.DrawPath(borderPen, borderPath);
            }

            float curX = 10f * scale;
            float row1Y = (float)Math.Round(5f * scale);
            float row2Y = (float)Math.Round(23f * scale);

            Color cpuCol = CpuColor;
            Color gpuCol = GpuColor;
            Color dimCpu = Color.FromArgb(170, (int)(CpuColor.R * 0.8), (int)(CpuColor.G * 0.8), (int)(CpuColor.B * 0.8));
            Color dimGpu = Color.FromArgb(170, (int)(GpuColor.R * 0.8), (int)(GpuColor.G * 0.8), (int)(GpuColor.B * 0.8));

            // ── COLUMN 1: BIG BOLD FPS COUNTER ──────────────────────────────────────────
            if (ShowFps)
            {
                string fpsText = _fps.HasValue ? _fps.Value.ToString() : "N/A";
                using (var fpsBrush = new SolidBrush(gpuCol))
                {
                    var fpsSize = g.MeasureString(fpsText, _fontFps!);
                    float fpsY = (h - fpsSize.Height) / 2f;
                    g.DrawString(fpsText, _fontFps!, fpsBrush, new RectangleF(curX, fpsY, _fpsWidth - 6f * scale, h), RightAligned);
                    curX += _fpsWidth;
                }
            }

            // ── COLUMN 2: HARDWARE STATS (GPU & CPU) ────────────────────────────────────
            if (ShowTemperatures || ShowFan || ShowCpuClock || ShowGpuClock)
            {
                using (var gpuBrush = new SolidBrush(gpuCol))
                using (var cpuBrush = new SolidBrush(cpuCol))
                using (var dimGpuBrush = new SolidBrush(dimGpu))
                using (var dimCpuBrush = new SolidBrush(dimCpu))
                {
                    string gpuTemp = _gpuTemp.HasValue && _gpuTemp.Value > 0 ? $"{_gpuTemp.Value}°" : "N/A";
                    string cpuTemp = _cpuTemp.HasValue && _cpuTemp.Value > 0 ? $"{_cpuTemp.Value}°" : "N/A";
                    string gpuRpm = _gpuFanRpm.HasValue ? $" {_gpuFanRpm.Value}" : "N/A";
                    string cpuRpm = _cpuFanRpm.HasValue ? $" {_cpuFanRpm.Value}" : "N/A";
                    string gpuClock = _gpuClockMhz.HasValue ? $"{_gpuClockMhz.Value} MHz" : "N/A";
                    string cpuClock = _cpuClockMhz.HasValue ? $"{_cpuClockMhz.Value} MHz" : "N/A";

                    float lblW = 0f;
                    if (ShowLabels)
                    {
                        g.DrawString("GPU: ", _fontLabel!, gpuBrush, curX, row1Y + 1 * scale);
                        g.DrawString("CPU: ", _fontLabel!, cpuBrush, curX, row2Y + 1 * scale);
                        lblW = _labelWidth;
                    }

                    float tempW = 0f;
                    if (ShowTemperatures)
                    {
                        tempW = _temperatureWidth;
                        DrawValue(g, gpuTemp, gpuBrush, curX + lblW, row1Y, tempW, scale);
                        DrawValue(g, cpuTemp, cpuBrush, curX + lblW, row2Y, tempW, scale);
                    }

                    float fanW = 0f;
                    if (ShowFan)
                    {
                        float rpmW = _rpmWidth;
                        DrawValue(g, gpuRpm, gpuBrush, curX + lblW + tempW, row1Y, rpmW, scale);
                        DrawValue(g, cpuRpm, cpuBrush, curX + lblW + tempW, row2Y, rpmW, scale);

                        g.DrawString("RPM", _fontSuperscript!, dimGpuBrush, curX + lblW + tempW + rpmW, row1Y + 1.2f * scale);
                        g.DrawString("RPM", _fontSuperscript!, dimCpuBrush, curX + lblW + tempW + rpmW, row2Y + 1.2f * scale);
                        float supW = g.MeasureString("RPM", _fontSuperscript!).Width;
                        fanW = rpmW + supW;
                    }

                    float clockW = 0f;
                    if (ShowCpuClock || ShowGpuClock)
                    {
                        clockW = _clockWidth;
                        float clockX = curX + lblW + tempW + fanW + (fanW > 0 ? 4f * scale : 0f);
                        if (ShowGpuClock) DrawValue(g, gpuClock, gpuBrush, clockX, row1Y, clockW, scale);
                        if (ShowCpuClock) DrawValue(g, cpuClock, cpuBrush, clockX, row2Y, clockW, scale);
                    }

                    curX += lblW + tempW + fanW + clockW + (clockW > 0 && fanW > 0 ? 4f * scale : 0f) + 14f * scale;
                }
            }

            float chartX = 0f;
            float chartW = 0f;
            bool hasChart = false;

            // ── COLUMN 3: ROLLING 60-SECOND SPARKLINE GRAPH ───────────────────────────
            if (ShowChart)
            {
                hasChart = true;
                chartW = 86f * scale;
                float chartH = 30f * scale;
                chartX = curX;
                float chartY = (h - chartH) / 2f;

                using (var chartBgBrush = new SolidBrush(ChartBg))
                using (var chartBorderPen = new Pen(Color.FromArgb(30, 255, 255, 255), 1f))
                {
                    g.FillRectangle(chartBgBrush, chartX, chartY, chartW, chartH);
                    g.DrawRectangle(chartBorderPen, chartX, chartY, chartW, chartH);
                }

                DrawSparklines(g, chartX, chartY, chartW, chartH, scale);
                curX += chartW + 10f * scale;
            }

            // ── COLUMN 4: POWER DRAW (W) / BATTERY ──────────────────────────────────
            if (ShowPower || ShowBattery)
            {
                using (var gpuBrush = new SolidBrush(gpuCol))
                using (var cpuBrush = new SolidBrush(cpuCol))
                {
                    string gpuPower = ShowPower ? (_gpuPowerW.HasValue ? $"{_gpuPowerW.Value:F1}W" : "N/A") : "";
                    string cpuPower = ShowPower
                        ? (_cpuPowerW.HasValue && _cpuPowerW.Value > 0 ? $"{_cpuPowerW.Value:F1}W" : (ShowBattery && _batteryPercent.HasValue ? $"BAT {_batteryPercent.Value:F0}%" : "N/A"))
                        : (ShowBattery ? (_batteryPercent.HasValue ? $"BAT {_batteryPercent.Value:F0}%" : "N/A") : "");

                    if (!string.IsNullOrEmpty(gpuPower))
                        DrawValue(g, gpuPower, gpuBrush, curX, row1Y, _powerWidth, scale);
                    if (!string.IsNullOrEmpty(cpuPower))
                        DrawValue(g, cpuPower, cpuBrush, curX, row2Y, _powerWidth, scale);

                    curX += _powerWidth + 10f * scale;
                }
            }

            // ── COLUMN 5: LOAD % WITH MINI BARS ─────────────────────────────────────
            if (ShowLoad)
            {
                float barW = 3.5f * scale;
                float barH = 12f * scale;

                int gpuVal = Math.Clamp(_gpuUsage ?? 0, 0, 100);
                int cpuVal = Math.Clamp(_cpuUsage ?? 0, 0, 100);
                // No real measurement (e.g. NVML unavailable) shows "--" rather than a misleading 0%.
                string gpuPct = _gpuUsage.HasValue ? $"{gpuVal}%" : "N/A";
                string cpuPct = _cpuUsage.HasValue ? $"{cpuVal}%" : "N/A";

                using (var gpuBrush = new SolidBrush(gpuCol))
                using (var cpuBrush = new SolidBrush(cpuCol))
                {
                    DrawValue(g, gpuPct, gpuBrush, curX, row1Y, _loadWidth, scale);
                    DrawValue(g, cpuPct, cpuBrush, curX, row2Y, _loadWidth, scale);

                    float pctW = _loadWidth;
                    float barX = curX + pctW + 3f * scale;

                    if (_gpuUsage.HasValue) DrawMiniBar(g, barX, row1Y + 1 * scale, barW, barH, gpuVal, gpuCol, dimGpu, scale);
                    if (_cpuUsage.HasValue) DrawMiniBar(g, barX, row2Y + 1 * scale, barW, barH, cpuVal, cpuCol, dimCpu, scale);

                    curX = barX + barW + 10f * scale;
                }
            }

            // ── COLUMN 6: VRAM / RAM WITH MINI BARS ─────────────────────────────────
            if (ShowRam)
            {
                float barW = 3.5f * scale;
                float barH = 12f * scale;

                int? vramPct = OverlayPolicy.MemoryPercent(_vramUsedGb, _vramTotalGb);
                int? ramPct = OverlayPolicy.MemoryPercent(_ramUsedGb, _ramTotalGb);
                string vramText = vramPct.HasValue ? $"{_vramUsedGb:F1}GB" : "N/A";
                string ramText = ramPct.HasValue ? $"{_ramUsedGb:F1}GB" : "N/A";

                using (var gpuBrush = new SolidBrush(gpuCol))
                using (var cpuBrush = new SolidBrush(cpuCol))
                {
                    DrawValue(g, vramText, gpuBrush, curX, row1Y, _memoryWidth, scale);
                    DrawValue(g, ramText, cpuBrush, curX, row2Y, _memoryWidth, scale);

                    float memW = _memoryWidth;
                    float barX = curX + memW + 3f * scale;

                    if (vramPct.HasValue) DrawMiniBar(g, barX, row1Y + 1 * scale, barW, barH, vramPct.Value, gpuCol, dimGpu, scale);
                    if (ramPct.HasValue) DrawMiniBar(g, barX, row2Y + 1 * scale, barW, barH, ramPct.Value, cpuCol, dimCpu, scale);
                }
            }

            // ── TRANSIENT TOAST BADGE (Floating feedback on mode cycle or scale) ─────────
            if (!string.IsNullOrEmpty(_toastText) && DateTime.UtcNow < _toastExpiry)
            {
                var toastSize = g.MeasureString(_toastText, _fontToast!);
                float tw = toastSize.Width + 10 * scale;
                float th = toastSize.Height + 3 * scale;
                float tx = (hasChart && chartW >= tw) ? (chartX + (chartW - tw) / 2f) : ((w - tw) / 2f);
                float ty = (h - th) / 2f;

                using var tBg = new SolidBrush(Color.FromArgb(240, 12, 16, 22));
                using var tBorder = new Pen(ActiveBorderColor, 1.0f * scale);
                using var tTextBrush = new SolidBrush(Color.White);
                using var tPath = GetRoundedPath(new Rectangle((int)tx, (int)ty, (int)tw, (int)th), (int)(3 * scale));

                g.FillPath(tBg, tPath);
                g.DrawPath(tBorder, tPath);
                g.DrawString(_toastText, _fontToast!, tTextBrush, tx + 5 * scale, ty + 1.5f * scale);
            }
        }

        private void DrawValue(Graphics g, string text, Brush brush, float x, float y, float width, float scale) =>
            g.DrawString(text, _fontMain!, brush, new RectangleF(x, y, width, 20f * scale), RightAligned);

        private static void DrawMiniBar(Graphics g, float x, float y, float w, float h, int percent, Color activeColor, Color inactiveBg, float scale)
        {
            const int segmentCount = 4;
            float gap = 1.0f * scale;
            float totalGap = (segmentCount - 1) * gap;
            float segH = Math.Max(1.0f, (h - totalGap) / segmentCount);

            int filledSegments = (int)Math.Round((percent / 100.0) * segmentCount);

            using var activeBrush = new SolidBrush(activeColor);
            using var inactiveBrush = new SolidBrush(inactiveBg);

            for (int i = 0; i < segmentCount; i++)
            {
                float segY = y + (segmentCount - 1 - i) * (segH + gap);
                var brush = i < filledSegments ? activeBrush : inactiveBrush;
                g.FillRectangle(brush, x, segY, w, segH);
            }
        }

        private void DrawSparklines(Graphics g, float x, float y, float w, float h, float scale)
        {
            if (_historyCount < 2) return;

            const float minTemp = 35f;
            const float maxTemp = 95f;
            float tempRange = maxTemp - minTemp;

            int count = Math.Min(_historyCount, HistoryLength);
            float stepX = w / (HistoryLength - 1);
            using var gpuPen = new Pen(GpuColor, 1.3f * scale);
            using var cpuPen = new Pen(CpuColor, 1.3f * scale);
            using var fillBrush = new LinearGradientBrush(new PointF(x, y), new PointF(x, y + h),
                Color.FromArgb(40, CpuColor), Color.FromArgb(0, CpuColor));
            using var fillPath = new GraphicsPath();
            PointF? previousGpu = null, previousCpu = null;
            for (int i = 0; i < count; i++)
            {
                int ringIdx = (_historyHead - count + i + HistoryLength) % HistoryLength;
                float px = x + (HistoryLength - count + i) * stepX;
                PointF? gpu = _gpuHistory[ringIdx] > 0
                    ? new PointF(px, y + h - Math.Clamp((_gpuHistory[ringIdx] - minTemp) / tempRange, 0, 1) * h) : null;
                PointF? cpu = _cpuHistory[ringIdx] > 0
                    ? new PointF(px, y + h - Math.Clamp((_cpuHistory[ringIdx] - minTemp) / tempRange, 0, 1) * h) : null;
                if (cpu.HasValue && previousCpu.HasValue)
                {
                    fillPath.Reset();
                    fillPath.AddLine(previousCpu.Value.X, y + h, previousCpu.Value.X, previousCpu.Value.Y);
                    fillPath.AddLine(previousCpu.Value, cpu.Value);
                    fillPath.AddLine(cpu.Value.X, cpu.Value.Y, cpu.Value.X, y + h);
                    fillPath.CloseFigure();
                    g.FillPath(fillBrush, fillPath);
                    g.DrawLine(cpuPen, previousCpu.Value, cpu.Value);
                }
                if (gpu.HasValue && previousGpu.HasValue) g.DrawLine(gpuPen, previousGpu.Value, gpu.Value);
                previousCpu = cpu;
                previousGpu = gpu; // Missing samples break the line instead of bridging an outage.
            }
        }

        private static GraphicsPath GetRoundedPath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2f;

            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _renderTimer.Dispose();
                _keyStateTimer.Dispose();
                _fpsMonitor.Dispose();
                _fontFps?.Dispose();
                _fontMain?.Dispose();
                _fontSmall?.Dispose();
                _fontLabel?.Dispose();
                _fontSuperscript?.Dispose();
                _fontToast?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
