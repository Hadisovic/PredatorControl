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
        Full = 2      // Full: Default + GPU/CPU Load % with bars + VRAM/RAM with bars
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
        private const int VK_MENU = 0x12; // Alt key

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

        #endregion

        private readonly EtwFpsMonitor _fpsMonitor;
        private readonly System.Windows.Forms.Timer _renderTimer = new();
        private readonly System.Windows.Forms.Timer _keyStateTimer = new();

        private bool _isClickThrough = true;
        private float _dpiScale = 1.0f;
        private float _fontDpiScale = 0f;

        // Current display mode and scale
        public OverlayMode Mode { get; private set; } = OverlayMode.Default;
        public int ScalePercent { get; private set; } = 100;
        public event Action<OverlayMode, int>? OverlayStateChanged;

        internal int? CurrentFps => _fps;

        // Telemetry State
        private int? _fps;
        private int? _cpuTemp;
        private int? _gpuTemp;
        private int? _cpuFanRpm;
        private int? _gpuFanRpm;
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

        // Visual Colors (matching G-Helper reference screenshots & Jelli aesthetic)
        private static readonly Color GpuGreen = Color.FromArgb(255, 0, 229, 117);  // Neon Mint/Green (#00E575)
        private static readonly Color CpuTeal = Color.FromArgb(255, 0, 180, 216);   // Sky Cyan/Blue (#00B4D8 / #38B6FF)
        private static readonly Color DimGpu = Color.FromArgb(170, 0, 190, 95);
        private static readonly Color DimCpu = Color.FromArgb(170, 0, 150, 185);
        private static readonly Color DimText = Color.FromArgb(160, 160, 175, 190);
        private static readonly Color BorderColor = Color.FromArgb(50, 255, 255, 255);
        private static readonly Color ActiveBorderColor = Color.FromArgb(255, 255, 184, 0); // Amber feedback while interactive
        private static readonly Color ChartBg = Color.FromArgb(220, 6, 8, 12);
        private static readonly Color OverlayBg = Color.FromArgb(10, 12, 16);

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
            BackColor = OverlayBg;
            Opacity = 0.88; // Translucent dark glass look matching G-Helper & Jelli

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.DoubleBuffer |
                     ControlStyles.OptimizedDoubleBuffer, true);

            _fpsMonitor = new EtwFpsMonitor();

            Task.Run(() =>
            {
                try
                {
                    _fpsMonitor.Start(0);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ETW session startup exception: {ex.Message}");
                }
            });

            _renderTimer.Interval = 250;
            _renderTimer.Tick += (s, e) => OnRenderTick();

            _keyStateTimer.Interval = 80;
            _keyStateTimer.Tick += (s, e) => CheckDragModifierKeys();

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
            Hide();
        }

        public void CycleMode()
        {
            var nextMode = (OverlayMode)(((int)Mode + 1) % 3);
            SetMode(nextMode);
        }

        public void SetMode(OverlayMode mode)
        {
            Mode = mode;
            SaveSettings();
            UpdateDpiAndLayout();
            ShowToast(mode.ToString().ToUpperInvariant());
            OverlayStateChanged?.Invoke(Mode, ScalePercent);
            Invalidate();
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

        public void ShowToast(string text)
        {
            _toastText = text;
            _toastExpiry = DateTime.UtcNow.AddMilliseconds(1200);
            Invalidate();
        }

        public void UpdateSnapshot(TelemetrySnapshot snapshot)
        {
            if (!Visible) return;

            _cpuTemp = snapshot.CpuTemp;
            _gpuTemp = snapshot.GpuTemp;
            _cpuFanRpm = snapshot.CpuFanRpm;
            _gpuFanRpm = snapshot.GpuFanRpm;
            _cpuUsage = snapshot.CpuUsage;
            _gpuUsage = snapshot.GpuUsage;
            _gpuPowerW = snapshot.GpuPowerW;
            _cpuPowerW = snapshot.CpuPowerW;
            _vramUsedGb = snapshot.VramUsedGb;
            _vramTotalGb = snapshot.VramTotalGb;
            _ramUsedGb = snapshot.RamUsedGb;
            _ramTotalGb = snapshot.RamTotalGb;
            _batteryPercent = snapshot.BatteryPercent;
            _isCharging = snapshot.IsCharging;
            _isPluggedIn = snapshot.PowerLine == PowerLineStatus.Online;

            // Push to 60-second rolling history
            if (_cpuTemp.HasValue && _cpuTemp.Value > 0)
                _cpuHistory[_historyHead] = _cpuTemp.Value;
            else
                _cpuHistory[_historyHead] = 0;

            if (_gpuTemp.HasValue && _gpuTemp.Value > 0)
                _gpuHistory[_historyHead] = _gpuTemp.Value;
            else
                _gpuHistory[_historyHead] = 0;

            _historyHead = (_historyHead + 1) % HistoryLength;
            if (_historyCount < HistoryLength) _historyCount++;

            Invalidate();
        }

        private void OnRenderTick()
        {
            try
            {
                IntPtr fgWnd = GetForegroundWindow();
                if (fgWnd != IntPtr.Zero)
                {
                    GetWindowThreadProcessId(fgWnd, out uint procId);
                    if (procId > 0)
                    {
                        try
                        {
                            using var proc = Process.GetProcessById((int)procId);
                            if (!ExcludedProcesses.Contains(proc.ProcessName))
                                _fpsMonitor.TargetPid = (int)procId;
                            else
                                _fpsMonitor.TargetPid = 0;
                        }
                        catch
                        {
                            _fpsMonitor.TargetPid = (int)procId;
                        }
                    }
                }
            }
            catch { }

            double sampled = _fpsMonitor.SampleFps();
            if (sampled > 0.5)
                _fps = (int)Math.Round(sampled);
            else
                _fps = null;

            Invalidate();
        }

        private void CheckDragModifierKeys()
        {
            // Support Ctrl + Shift + Alt and Ctrl + Shift
            bool ctrl = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
            bool shift = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
            bool alt = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;

            bool interactiveRequested = (ctrl && shift && alt) || (ctrl && shift);

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

        private float GetTotalScale()
        {
            // Fully dynamic to display scale (DPI) and user scale percent
            return _dpiScale * (ScalePercent / 100.0f);
        }

        private void UpdateDpiAndLayout()
        {
            if (!IsHandleCreated) return;

            // Authoritative monitor DPI via PerMonitorV2
            float formDpi = DeviceDpi > 0 ? DeviceDpi : 96.0f;
            _dpiScale = formDpi / 96.0f;
            if (_dpiScale < 0.5f) _dpiScale = 1.0f;

            float scale = GetTotalScale();
            EnsureFonts();

            // Calculate precise required size using Graphics.MeasureString so no clipping ever occurs
            int w, h;
            using (var g = CreateGraphics())
            {
                float calculatedW = CalculateRequiredWidth(g, scale);
                w = Math.Max((int)Math.Ceiling(calculatedW), (int)Math.Ceiling(140f * scale));
                h = (int)Math.Ceiling(46f * scale);
            }

            Size = new Size(w, h);

            int radius = Math.Max(4, (int)(8 * scale));
            IntPtr rgn = CreateRoundRectRgn(0, 0, w + 1, h + 1, radius, radius);
            SetWindowRgn(Handle, rgn, true);

            if (!_hasCustomPosition)
            {
                ApplyCorner();
            }
        }

        private float CalculateRequiredWidth(Graphics g, float scale)
        {
            float curX = 10f * scale;

            // Col 1: Big FPS counter (measured with dummy 3 digits "144")
            var fpsSize = g.MeasureString("144", _fontFps!);
            curX += Math.Max(38f * scale, fpsSize.Width + 6f * scale);

            // Col 2: GPU/CPU stats
            if (Mode == OverlayMode.Light)
            {
                var lblSz = g.MeasureString("GPU: ", _fontLabel!);
                var tempSz = g.MeasureString("88°", _fontMain!);
                var pwrSz = g.MeasureString(" 140.0W", _fontMain!);
                curX += lblSz.Width + tempSz.Width + pwrSz.Width + 14f * scale;
            }
            else
            {
                var lblSz = g.MeasureString("GPU: ", _fontLabel!);
                var tempSz = g.MeasureString("88°", _fontMain!);
                var rpmSz = g.MeasureString(" 4800", _fontMain!);
                var supSz = g.MeasureString("RPM", _fontSuperscript!);
                curX += lblSz.Width + tempSz.Width + rpmSz.Width + supSz.Width + 16f * scale;

                // Col 3: Sparkline chart
                float chartW = 86f * scale;
                curX += chartW + 10f * scale;

                // Col 4: Power
                var pwrSz = g.MeasureString("140.0W", _fontMain!);
                curX += pwrSz.Width + 10f * scale;

                if (Mode == OverlayMode.Full)
                {
                    // Col 5: Load %
                    var loadSz = g.MeasureString("100%", _fontMain!);
                    float barW = 4.0f * scale;
                    curX += loadSz.Width + 3f * scale + barW + 12f * scale;

                    // Col 6: VRAM / RAM
                    var memSz = g.MeasureString("32.0GB", _fontMain!);
                    curX += memSz.Width + 3f * scale + barW + 12f * scale;
                }
            }

            return curX;
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

        private void LoadPersistedSettings()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PredatorControl");
                if (key != null)
                {
                    int modeVal = (int)(key.GetValue("OverlayMode", 1) ?? 1);
                    Mode = (OverlayMode)Math.Clamp(modeVal, 0, 2);

                    int scaleVal = (int)(key.GetValue("OverlayScale", 100) ?? 100);
                    ScalePercent = Math.Clamp(scaleVal, 50, 300);
                }
            }
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\PredatorControl");
                if (key != null)
                {
                    key.SetValue("OverlayMode", (int)Mode, RegistryValueKind.DWord);
                    key.SetValue("OverlayScale", ScalePercent, RegistryValueKind.DWord);
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
                }
            }
            catch { }
        }

        internal void Configure(JelliSettings settings)
        {
            _jelliSettings = settings;

            // If settings contains overlay mode or scale, apply them
            if (!string.IsNullOrEmpty(settings.OverlayMode))
            {
                Mode = settings.OverlayMode.ToLowerInvariant() switch
                {
                    "light" => OverlayMode.Light,
                    "full" => OverlayMode.Full,
                    _ => OverlayMode.Default
                };
            }

            if (settings.OverlayScale >= 50 && settings.OverlayScale <= 300)
            {
                ScalePercent = settings.OverlayScale;
            }

            if (IsHandleCreated) UpdateDpiAndLayout();
            ApplyCorner();
            Invalidate();
        }

        private void ApplyCorner()
        {
            var screen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == _jelliSettings.OverlayMonitor) ?? Screen.PrimaryScreen ?? Screen.AllScreens[0];
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

            // ── COLUMN 1: BIG BOLD FPS COUNTER ──────────────────────────────────────────
            string fpsText = _fps.HasValue ? _fps.Value.ToString() : "--";
            using (var fpsBrush = new SolidBrush(GpuGreen))
            {
                var fpsSize = g.MeasureString(fpsText, _fontFps!);
                float fpsY = (h - fpsSize.Height) / 2f;
                g.DrawString(fpsText, _fontFps!, fpsBrush, curX, fpsY);
                curX += Math.Max(38f * scale, fpsSize.Width + 6f * scale);
            }

            // ── COLUMN 2: HARDWARE STATS (GPU & CPU) ────────────────────────────────────
            using (var gpuBrush = new SolidBrush(GpuGreen))
            using (var cpuBrush = new SolidBrush(CpuTeal))
            using (var dimGpuBrush = new SolidBrush(DimGpu))
            using (var dimCpuBrush = new SolidBrush(DimCpu))
            {
                string gpuTemp = _gpuTemp.HasValue && _gpuTemp.Value > 0 ? $"{_gpuTemp.Value}°" : "--°";
                string cpuTemp = _cpuTemp.HasValue && _cpuTemp.Value > 0 ? $"{_cpuTemp.Value}°" : "--°";

                if (Mode == OverlayMode.Light)
                {
                    string gpuPower = _gpuPowerW.HasValue ? $"{_gpuPowerW.Value:F1}W" : (_isPluggedIn ? "--W" : "BAT");
                    string cpuPower = _cpuPowerW.HasValue && _cpuPowerW.Value > 0 ? $"{_cpuPowerW.Value:F1}W" : (_batteryPercent.HasValue ? $"{_batteryPercent.Value:F0}%" : "--W");

                    g.DrawString("GPU: ", _fontLabel!, gpuBrush, curX, row1Y + 1 * scale);
                    float lblW = g.MeasureString("GPU: ", _fontLabel!).Width;

                    g.DrawString(gpuTemp, _fontMain!, gpuBrush, curX + lblW, row1Y);
                    float tempW = g.MeasureString(gpuTemp, _fontMain!).Width;

                    g.DrawString($"  {gpuPower}", _fontMain!, gpuBrush, curX + lblW + tempW, row1Y);

                    g.DrawString("CPU: ", _fontLabel!, cpuBrush, curX, row2Y + 1 * scale);
                    g.DrawString(cpuTemp, _fontMain!, cpuBrush, curX + lblW, row2Y);
                    g.DrawString($"  {cpuPower}", _fontMain!, cpuBrush, curX + lblW + tempW, row2Y);

                    float pwrW = Math.Max(g.MeasureString($"  {gpuPower}", _fontMain!).Width, g.MeasureString($"  {cpuPower}", _fontMain!).Width);
                    curX += lblW + tempW + pwrW + 12f * scale;
                }
                else
                {
                    string gpuRpm = _gpuFanRpm.HasValue && _gpuFanRpm.Value > 0 ? $" {_gpuFanRpm.Value}" : " --";
                    string cpuRpm = _cpuFanRpm.HasValue && _cpuFanRpm.Value > 0 ? $" {_cpuFanRpm.Value}" : " --";

                    // GPU Row
                    g.DrawString("GPU: ", _fontLabel!, gpuBrush, curX, row1Y + 1 * scale);
                    float lblW = g.MeasureString("GPU: ", _fontLabel!).Width;

                    g.DrawString(gpuTemp, _fontMain!, gpuBrush, curX + lblW, row1Y);
                    float tempW = g.MeasureString(gpuTemp, _fontMain!).Width;

                    g.DrawString(gpuRpm, _fontMain!, gpuBrush, curX + lblW + tempW, row1Y);
                    float rpmW = g.MeasureString(gpuRpm, _fontMain!).Width;

                    g.DrawString("RPM", _fontSuperscript!, dimGpuBrush, curX + lblW + tempW + rpmW, row1Y + 1.2f * scale);
                    float supW = g.MeasureString("RPM", _fontSuperscript!).Width;

                    // CPU Row
                    g.DrawString("CPU: ", _fontLabel!, cpuBrush, curX, row2Y + 1 * scale);
                    g.DrawString(cpuTemp, _fontMain!, cpuBrush, curX + lblW, row2Y);
                    g.DrawString(cpuRpm, _fontMain!, cpuBrush, curX + lblW + tempW, row2Y);
                    g.DrawString("RPM", _fontSuperscript!, dimCpuBrush, curX + lblW + tempW + rpmW, row2Y + 1.2f * scale);

                    curX += lblW + tempW + rpmW + supW + 14f * scale;
                }
            }

            float chartX = 0f;
            float chartW = 0f;
            bool hasChart = false;

            // ── COLUMN 3: ROLLING 60-SECOND SPARKLINE GRAPH (Default & Full only) ───────
            if (Mode != OverlayMode.Light)
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

                // ── COLUMN 4: POWER DRAW (W) / BATTERY ──────────────────────────────────
                using (var gpuBrush = new SolidBrush(GpuGreen))
                using (var cpuBrush = new SolidBrush(CpuTeal))
                {
                    string gpuPower = _gpuPowerW.HasValue ? $"{_gpuPowerW.Value:F1}W" : (_isPluggedIn ? "--W" : "BAT");
                    string cpuPower = _cpuPowerW.HasValue && _cpuPowerW.Value > 0 ? $"{_cpuPowerW.Value:F1}W" : (_batteryPercent.HasValue ? $"{_batteryPercent.Value:F0}%" : "--W");

                    g.DrawString(gpuPower, _fontMain!, gpuBrush, curX, row1Y);
                    g.DrawString(cpuPower, _fontMain!, cpuBrush, curX, row2Y);

                    float powerW = Math.Max(g.MeasureString(gpuPower, _fontMain!).Width, g.MeasureString(cpuPower, _fontMain!).Width);
                    curX += powerW + 10f * scale;
                }
            }

            // ── COLUMNS 5 & 6: LOAD % & VRAM/RAM WITH MINI BARS (Full Mode only) ─────────
            if (Mode == OverlayMode.Full)
            {
                float barW = 3.5f * scale;
                float barH = 12f * scale;

                // Column 5: GPU & CPU Load % + vertical segmented bar
                int gpuVal = Math.Clamp(_gpuUsage ?? 0, 0, 100);
                int cpuVal = Math.Clamp(_cpuUsage ?? 0, 0, 100);
                string gpuPct = $"{gpuVal}%";
                string cpuPct = $"{cpuVal}%";

                using (var gpuBrush = new SolidBrush(GpuGreen))
                using (var cpuBrush = new SolidBrush(CpuTeal))
                {
                    g.DrawString(gpuPct, _fontMain!, gpuBrush, curX, row1Y);
                    g.DrawString(cpuPct, _fontMain!, cpuBrush, curX, row2Y);

                    float pctW = Math.Max(g.MeasureString(gpuPct, _fontMain!).Width, g.MeasureString(cpuPct, _fontMain!).Width);
                    float barX = curX + pctW + 3f * scale;

                    DrawMiniBar(g, barX, row1Y + 1 * scale, barW, barH, gpuVal, GpuGreen, DimGpu, scale);
                    DrawMiniBar(g, barX, row2Y + 1 * scale, barW, barH, cpuVal, CpuTeal, DimCpu, scale);

                    curX = barX + barW + 10f * scale;
                }

                // Column 6: VRAM (GPU) & RAM (CPU) GB + vertical segmented bar
                float vramGb = _vramUsedGb ?? 0f;
                float vramMax = _vramTotalGb ?? 8.0f;
                int vramPct = (int)Math.Clamp((vramGb / Math.Max(1f, vramMax)) * 100f, 0, 100);

                float ramGb = _ramUsedGb ?? 0f;
                float ramMax = _ramTotalGb ?? 16.0f;
                int ramPct = (int)Math.Clamp((ramGb / Math.Max(1f, ramMax)) * 100f, 0, 100);

                string vramText = $"{vramGb:F1}GB";
                string ramText = $"{ramGb:F1}GB";

                using (var gpuBrush = new SolidBrush(GpuGreen))
                using (var cpuBrush = new SolidBrush(CpuTeal))
                {
                    g.DrawString(vramText, _fontMain!, gpuBrush, curX, row1Y);
                    g.DrawString(ramText, _fontMain!, cpuBrush, curX, row2Y);

                    float memW = Math.Max(g.MeasureString(vramText, _fontMain!).Width, g.MeasureString(ramText, _fontMain!).Width);
                    float barX = curX + memW + 3f * scale;

                    DrawMiniBar(g, barX, row1Y + 1 * scale, barW, barH, vramPct, GpuGreen, DimGpu, scale);
                    DrawMiniBar(g, barX, row2Y + 1 * scale, barW, barH, ramPct, CpuTeal, DimCpu, scale);
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

            var gpuPts = new List<PointF>();
            var cpuPts = new List<PointF>();

            int count = Math.Min(_historyCount, HistoryLength);
            float stepX = w / (HistoryLength - 1);

            for (int i = 0; i < count; i++)
            {
                int ringIdx = (_historyHead - count + i + HistoryLength) % HistoryLength;
                float px = x + (HistoryLength - count + i) * stepX;

                float gTemp = _gpuHistory[ringIdx];
                if (gTemp > 0)
                {
                    float normG = Math.Clamp((gTemp - minTemp) / tempRange, 0f, 1f);
                    float pyG = y + h - (normG * h);
                    gpuPts.Add(new PointF(px, pyG));
                }

                float cTemp = _cpuHistory[ringIdx];
                if (cTemp > 0)
                {
                    float normC = Math.Clamp((cTemp - minTemp) / tempRange, 0f, 1f);
                    float pyC = y + h - (normC * h);
                    cpuPts.Add(new PointF(px, pyC));
                }
            }

            // Shaded gradient fill beneath CPU line
            if (cpuPts.Count >= 2)
            {
                using var fillPath = new GraphicsPath();
                fillPath.AddLine(cpuPts[0].X, y + h, cpuPts[0].X, cpuPts[0].Y);
                for (int i = 1; i < cpuPts.Count; i++)
                    fillPath.AddLine(cpuPts[i - 1], cpuPts[i]);
                fillPath.AddLine(cpuPts[^1].X, cpuPts[^1].Y, cpuPts[^1].X, y + h);
                fillPath.CloseFigure();

                using var fillBrush = new LinearGradientBrush(
                    new PointF(x, y), new PointF(x, y + h),
                    Color.FromArgb(40, 0, 180, 216), Color.FromArgb(0, 0, 180, 216));
                g.FillPath(fillBrush, fillPath);
            }

            // Crisp line strokes
            using (var gpuPen = new Pen(GpuGreen, 1.3f * scale))
            using (var cpuPen = new Pen(CpuTeal, 1.3f * scale))
            {
                if (gpuPts.Count >= 2) g.DrawLines(gpuPen, gpuPts.ToArray());
                if (cpuPts.Count >= 2) g.DrawLines(cpuPen, cpuPts.ToArray());
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
