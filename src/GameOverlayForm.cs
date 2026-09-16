using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace PredatorControlApp
{
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

        #endregion

        private readonly EtwFpsMonitor _fpsMonitor;
        private readonly System.Windows.Forms.Timer _renderTimer = new();
        private readonly System.Windows.Forms.Timer _keyStateTimer = new();

        private bool _isClickThrough = true;
        private float _dpiScale = 1.0f;
        private float _fontDpiScale = 0f;

        // Telemetry State
        private int? _fps;
        private int? _cpuTemp;
        private int? _gpuTemp;
        private int? _cpuFanRpm;
        private int? _gpuFanRpm;
        private int? _cpuUsage;
        private int? _gpuUsage;
        private float? _gpuPowerW;
        private float? _batteryPercent;
        private bool _isCharging;

        // 60-second rolling history for sparkline chart
        private const int HistoryLength = 60;
        private readonly float[] _cpuHistory = new float[HistoryLength];
        private readonly float[] _gpuHistory = new float[HistoryLength];
        private int _historyHead = 0;
        private int _historyCount = 0;

        // Visual Colors (matching G-Helper reference screenshot)
        private static readonly Color GpuGreen = Color.FromArgb(255, 0, 255, 128); // Vibrant Neon Green
        private static readonly Color CpuTeal = Color.FromArgb(255, 0, 229, 255);  // Neon Cyan / Teal
        private static readonly Color DimText = Color.FromArgb(200, 160, 175, 195);
        private static readonly Color BorderColor = Color.FromArgb(70, 0, 229, 255);
        private static readonly Color ChartBg = Color.FromArgb(220, 8, 12, 18);
        private static readonly Color OverlayBg = Color.FromArgb(12, 16, 24); // Opaque Form.BackColor (Opacity handles DWM translucency)

        private Font? _fontFps;
        private Font? _fontMain;
        private Font? _fontSmall;
        private Font? _fontLabel;

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
            Opacity = 0.94; // Translucent dark glass look via native Windows DWM alpha

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.DoubleBuffer |
                     ControlStyles.OptimizedDoubleBuffer, true);

            _fpsMonitor = new EtwFpsMonitor();

            // Start ETW session in background thread
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

            // Fast 250ms render tick for silky-smooth FPS and metric updates
            _renderTimer.Interval = 250;
            _renderTimer.Tick += (s, e) => OnRenderTick();

            // Key state timer (checks Ctrl + Shift every 100ms to toggle click-through)
            _keyStateTimer.Interval = 100;
            _keyStateTimer.Tick += (s, e) => CheckDragModifierKeys();

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
        }

        public void ToggleOverlay()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(ToggleOverlay));
                return;
            }

            if (Visible)
            {
                HideOverlay();
            }
            else
            {
                ShowOverlay();
            }
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
            _batteryPercent = snapshot.BatteryPercent;
            _isCharging = snapshot.IsCharging;

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
            // 1. Update foreground window tracking
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
                            {
                                _fpsMonitor.TargetPid = (int)procId;
                            }
                            else
                            {
                                _fpsMonitor.TargetPid = 0;
                            }
                        }
                        catch
                        {
                            _fpsMonitor.TargetPid = (int)procId;
                        }
                    }
                }
            }
            catch { }

            // 2. Sample FPS from ETW provider
            double sampled = _fpsMonitor.SampleFps();
            if (sampled > 0.5)
            {
                _fps = (int)Math.Round(sampled);
            }
            else
            {
                _fps = null;
            }

            Invalidate();
        }

        private void CheckDragModifierKeys()
        {
            // If user holds Ctrl + Shift, make the overlay interactive so they can click and drag it
            bool ctrlPressed = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
            bool shiftPressed = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
            bool shouldAllowClick = ctrlPressed && shiftPressed;

            if (shouldAllowClick && _isClickThrough)
            {
                SetClickThrough(false);
                Cursor = Cursors.SizeAll;
                Invalidate();
            }
            else if (!shouldAllowClick && !_isClickThrough)
            {
                SetClickThrough(true);
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        private void SetClickThrough(bool clickThrough)
        {
            if (!IsHandleCreated) return;
            _isClickThrough = clickThrough;
            int style = GetWindowLong(Handle, GWL_EXSTYLE);
            if (clickThrough)
            {
                SetWindowLong(Handle, GWL_EXSTYLE, style | WS_EX_TRANSPARENT);
            }
            else
            {
                SetWindowLong(Handle, GWL_EXSTYLE, style & ~WS_EX_TRANSPARENT);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && !_isClickThrough)
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
                SavePosition();
                CheckDragModifierKeys();
            }
        }

        private void UpdateDpiAndLayout()
        {
            using (var g = CreateGraphics())
            {
                _dpiScale = g.DpiX / 96.0f;
                if (_dpiScale < 1.0f) _dpiScale = 1.0f;
            }

            // Dimensions calibrated to G-Helper layout
            int w = (int)(430 * _dpiScale);
            int h = (int)(52 * _dpiScale);
            Size = new Size(w, h);

            int radius = (int)(8 * _dpiScale);
            IntPtr rgn = CreateRoundRectRgn(0, 0, w + 1, h + 1, radius, radius);
            SetWindowRgn(Handle, rgn, true);

            EnsureFonts();
        }

        private void EnsureFonts()
        {
            if (_fontDpiScale != _dpiScale || _fontFps == null || _fontMain == null || _fontSmall == null || _fontLabel == null)
            {
                _fontFps?.Dispose();
                _fontMain?.Dispose();
                _fontSmall?.Dispose();
                _fontLabel?.Dispose();

                _fontFps = new Font("Segoe UI", 20f * _dpiScale, FontStyle.Bold);
                _fontMain = new Font("Segoe UI", 8.5f * _dpiScale, FontStyle.Bold);
                _fontSmall = new Font("Segoe UI", 7.0f * _dpiScale, FontStyle.Regular);
                _fontLabel = new Font("Segoe UI", 7.0f * _dpiScale, FontStyle.Bold);
                _fontDpiScale = _dpiScale;
            }
        }

        private void LoadPersistedPosition()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PredatorControl");
                if (key != null)
                {
                    int x = (int)(key.GetValue("OverlayX", -1) ?? -1);
                    int y = (int)(key.GetValue("OverlayY", -1) ?? -1);
                    if (x >= 0 && y >= 0)
                    {
                        var screen = Screen.FromPoint(new Point(x, y)).WorkingArea;
                        if (screen.Contains(x, y))
                        {
                            Location = new Point(x, y);
                            return;
                        }
                    }
                }
            }
            catch { }

            // Default: top-left corner of primary screen with padding
            var primary = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
            Location = new Point(primary.Left + 28, primary.Top + 28);
        }

        private void SavePosition()
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\PredatorControl");
                key?.SetValue("OverlayX", Location.X, RegistryValueKind.DWord);
                key?.SetValue("OverlayY", Location.Y, RegistryValueKind.DWord);
            }
            catch { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            EnsureFonts();

            int w = Width;
            int h = Height;

            // 1. Dark glass background
            using (var bgBrush = new SolidBrush(OverlayBg))
            {
                g.FillRectangle(bgBrush, 0, 0, w, h);
            }

            // 2. Rounded border (highlight in orange while user drags)
            Color border = !_isClickThrough ? Color.FromArgb(255, 200, 50) : BorderColor;
            using (var borderPen = new Pen(border, 1.2f * _dpiScale))
            {
                int r = (int)(8 * _dpiScale);
                using var path = GetRoundedPath(new Rectangle(0, 0, w - 1, h - 1), r);
                g.DrawPath(borderPen, path);
            }

            float padX = 10 * _dpiScale;
            float curX = padX;

            // ── COLUMN 1: BIG BOLD FPS COUNTER ──────────────────────────────────────────
            string fpsText = _fps.HasValue ? _fps.Value.ToString() : "--";
            using (var fpsBrush = new SolidBrush(GpuGreen))
            {
                var fpsSize = g.MeasureString(fpsText, _fontFps!);
                float fpsY = (h - fpsSize.Height) / 2f;
                g.DrawString(fpsText, _fontFps!, fpsBrush, curX, fpsY);
                curX += Math.Max(52 * _dpiScale, fpsSize.Width + 8 * _dpiScale);
            }

            // ── COLUMN 2: GPU & CPU TEMP + FAN RPM ───────────────────────────────────────
            float row1Y = 7 * _dpiScale;
            float row2Y = 27 * _dpiScale;

            // GPU Row
            using (var gpuBrush = new SolidBrush(GpuGreen))
            using (var dimBrush = new SolidBrush(DimText))
            {
                string gpuHeader = "GPU: ";
                string gpuTemp = _gpuTemp.HasValue && _gpuTemp.Value > 0 ? $"{_gpuTemp.Value}°" : "--°";
                string gpuRpm = _gpuFanRpm.HasValue && _gpuFanRpm.Value > 0 ? $" {_gpuFanRpm.Value}" : " --";

                g.DrawString(gpuHeader, _fontLabel!, gpuBrush, curX, row1Y + 1 * _dpiScale);
                float headerW = g.MeasureString(gpuHeader, _fontLabel!).Width;

                g.DrawString(gpuTemp, _fontMain!, gpuBrush, curX + headerW, row1Y);
                float tempW = g.MeasureString(gpuTemp, _fontMain!).Width;

                g.DrawString(gpuRpm, _fontMain!, gpuBrush, curX + headerW + tempW, row1Y);
                float rpmW = g.MeasureString(gpuRpm, _fontMain!).Width;

                g.DrawString("RPM", _fontSmall!, dimBrush, curX + headerW + tempW + rpmW, row1Y + 2 * _dpiScale);
            }

            // CPU Row
            using (var cpuBrush = new SolidBrush(CpuTeal))
            using (var dimBrush = new SolidBrush(DimText))
            {
                string cpuHeader = "CPU: ";
                string cpuTemp = _cpuTemp.HasValue && _cpuTemp.Value > 0 ? $"{_cpuTemp.Value}°" : "--°";
                string cpuRpm = _cpuFanRpm.HasValue && _cpuFanRpm.Value > 0 ? $" {_cpuFanRpm.Value}" : " --";

                g.DrawString(cpuHeader, _fontLabel!, cpuBrush, curX, row2Y + 1 * _dpiScale);
                float headerW = g.MeasureString(cpuHeader, _fontLabel!).Width;

                g.DrawString(cpuTemp, _fontMain!, cpuBrush, curX + headerW, row2Y);
                float tempW = g.MeasureString(cpuTemp, _fontMain!).Width;

                g.DrawString(cpuRpm, _fontMain!, cpuBrush, curX + headerW + tempW, row2Y);
                float rpmW = g.MeasureString(cpuRpm, _fontMain!).Width;

                g.DrawString("RPM", _fontSmall!, dimBrush, curX + headerW + tempW + rpmW, row2Y + 2 * _dpiScale);
            }

            curX += 118 * _dpiScale;

            // ── COLUMN 3: ROLLING 60-SECOND SPARKLINE GRAPH ──────────────────────────────
            float chartW = 90 * _dpiScale;
            float chartH = 34 * _dpiScale;
            float chartX = curX;
            float chartY = (h - chartH) / 2f;

            using (var chartBgBrush = new SolidBrush(ChartBg))
            using (var chartBorderPen = new Pen(Color.FromArgb(40, 255, 255, 255), 1f))
            {
                g.FillRectangle(chartBgBrush, chartX, chartY, chartW, chartH);
                g.DrawRectangle(chartBorderPen, chartX, chartY, chartW, chartH);
            }

            DrawSparklines(g, chartX, chartY, chartW, chartH);
            curX += chartW + 10 * _dpiScale;

            // ── COLUMN 4: POWER DRAW (W) / BATTERY ──────────────────────────────────────
            using (var gpuBrush = new SolidBrush(GpuGreen))
            using (var cpuBrush = new SolidBrush(CpuTeal))
            {
                string gpuPower = _gpuPowerW.HasValue && _gpuPowerW.Value > 0 ? $"{_gpuPowerW.Value:F1}W" : "dGPU";
                g.DrawString(gpuPower, _fontMain!, gpuBrush, curX, row1Y);

                string secondLine;
                if (_batteryPercent.HasValue && _batteryPercent.Value < 100)
                {
                    secondLine = _isCharging ? $"{_batteryPercent.Value:F0}%⚡" : $"{_batteryPercent.Value:F0}%";
                }
                else
                {
                    secondLine = "AC";
                }
                g.DrawString(secondLine, _fontMain!, cpuBrush, curX, row2Y);
            }

            curX += 50 * _dpiScale;

            // ── COLUMN 5: UTILIZATION % & MINI BARS ─────────────────────────────────────
            float barW = 4 * _dpiScale;
            float barH = 14 * _dpiScale;

            // GPU Usage Bar
            int gpuVal = Math.Clamp(_gpuUsage ?? 0, 0, 100);
            using (var gpuBrush = new SolidBrush(GpuGreen))
            using (var barBgBrush = new SolidBrush(Color.FromArgb(40, 0, 255, 128)))
            {
                float barX = curX;
                g.FillRectangle(barBgBrush, barX, row1Y, barW, barH);
                float filledH = barH * (gpuVal / 100f);
                g.FillRectangle(gpuBrush, barX, row1Y + (barH - filledH), barW, filledH);

                string gpuPct = $"{gpuVal}%";
                g.DrawString(gpuPct, _fontSmall!, gpuBrush, barX + barW + 3 * _dpiScale, row1Y);
            }

            // CPU Usage Bar
            int cpuVal = Math.Clamp(_cpuUsage ?? 0, 0, 100);
            using (var cpuBrush = new SolidBrush(CpuTeal))
            using (var barBgBrush = new SolidBrush(Color.FromArgb(40, 0, 229, 255)))
            {
                float barX = curX;
                g.FillRectangle(barBgBrush, barX, row2Y, barW, barH);
                float filledH = barH * (cpuVal / 100f);
                g.FillRectangle(cpuBrush, barX, row2Y + (barH - filledH), barW, filledH);

                string cpuPct = $"{cpuVal}%";
                g.DrawString(cpuPct, _fontSmall!, cpuBrush, barX + barW + 3 * _dpiScale, row2Y);
            }
        }

        private void DrawSparklines(Graphics g, float x, float y, float w, float h)
        {
            if (_historyCount < 2) return;

            int count = Math.Min(_historyCount, HistoryLength);
            float minTemp = 35f;
            float maxTemp = 95f;

            var gpuPts = new List<PointF>(count);
            var cpuPts = new List<PointF>(count);

            for (int i = 0; i < count; i++)
            {
                int idx = (_historyHead - count + i + HistoryLength) % HistoryLength;
                float px = x + (i / (float)(count - 1)) * w;

                float gVal = Math.Clamp(_gpuHistory[idx], minTemp, maxTemp);
                float gNorm = (gVal - minTemp) / (maxTemp - minTemp);
                float gy = y + h - (gNorm * (h - 4)) - 2;
                gpuPts.Add(new PointF(px, gy));

                float cVal = Math.Clamp(_cpuHistory[idx], minTemp, maxTemp);
                float cNorm = (cVal - minTemp) / (maxTemp - minTemp);
                float cy = y + h - (cNorm * (h - 4)) - 2;
                cpuPts.Add(new PointF(px, cy));
            }

            using (var gpuPen = new Pen(GpuGreen, 1.4f * _dpiScale))
            using (var cpuPen = new Pen(CpuTeal, 1.4f * _dpiScale))
            {
                if (gpuPts.Count >= 2) g.DrawLines(gpuPen, gpuPts.ToArray());
                if (cpuPts.Count >= 2) g.DrawLines(cpuPen, cpuPts.ToArray());
            }
        }

        private static GraphicsPath GetRoundedPath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
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
            }
            base.Dispose(disposing);
        }
    }
}
