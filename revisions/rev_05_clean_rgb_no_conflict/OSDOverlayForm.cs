using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PredatorControlApp
{
    [SupportedOSPlatform("windows")]
    public sealed class OSDOverlayForm : Form
    {
        private static OSDOverlayForm? _instance;
        private byte _currentMode = 0x01; // Default Balanced
        private bool _onBattery = false;
        private readonly System.Windows.Forms.Timer _dismissTimer = new();
        private readonly System.Windows.Forms.Timer _fadeTimer = new();
        private double _targetOpacity = 0.95;
        private float _dpiScale = 1.0f;
        private Font? _tagFont;
        private Font? _titleFont;
        private Font? _subFont;
        private float _fontDpiScale = 0f;

        private void EnsureFonts()
        {
            if (_fontDpiScale != _dpiScale || _tagFont == null || _titleFont == null || _subFont == null)
            {
                _tagFont?.Dispose();
                _titleFont?.Dispose();
                _subFont?.Dispose();

                _tagFont = new Font("Segoe UI", 7.5f * _dpiScale, FontStyle.Bold);
                _titleFont = new Font("Segoe UI", 13.5f * _dpiScale, FontStyle.Bold);
                _subFont = new Font("Segoe UI", 8.5f * _dpiScale, FontStyle.Regular);
                _fontDpiScale = _dpiScale;
            }
        }

        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_TOPMOST = 0x00000008;

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_SHOWNOACTIVATE = 4;

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int cx, int cy);

        [DllImport("user32.dll")]
        private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

        public OSDOverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(14, 18, 24);

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.DoubleBuffer |
                     ControlStyles.OptimizedDoubleBuffer, true);

            _dismissTimer.Interval = 1800; // Stay visible for 1.8 seconds
            _dismissTimer.Tick += (s, e) =>
            {
                _dismissTimer.Stop();
                _fadeTimer.Start();
            };

            _fadeTimer.Interval = 20; // 50 fps fade
            _fadeTimer.Tick += (s, e) =>
            {
                if (Opacity > 0.05)
                {
                    Opacity -= 0.12;
                }
                else
                {
                    _fadeTimer.Stop();
                    Opacity = 0;
                    Hide();
                }
            };
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

        public static void ShowMode(byte mode, bool onBattery = false)
        {
            try
            {
                if (_instance == null || _instance.IsDisposed)
                {
                    _instance = new OSDOverlayForm();
                }

                if (_instance.InvokeRequired)
                {
                    _instance.BeginInvoke(new Action(() => _instance.InternalShow(mode, onBattery)));
                }
                else
                {
                    _instance.InternalShow(mode, onBattery);
                }
            }
            catch { }
        }

        private void InternalShow(byte mode, bool onBattery)
        {
            _currentMode = mode;
            _onBattery = onBattery;

            _fadeTimer.Stop();
            _dismissTimer.Stop();

            // Calculate DPI and sizing
            using (var g = CreateGraphics())
            {
                _dpiScale = g.DpiX / 96.0f;
                if (_dpiScale < 1.0f) _dpiScale = 1.0f;
            }

            int w = (int)(380 * _dpiScale);
            int h = (int)(110 * _dpiScale);
            Size = new Size(w, h);

            // Set rounded region
            int radius = (int)(16 * _dpiScale);
            IntPtr rgn = CreateRoundRectRgn(0, 0, w, h, radius, radius);
            SetWindowRgn(Handle, rgn, true);

            // Center on active screen, upper-third (16% from top)
            var screen = Screen.FromPoint(Cursor.Position);
            int x = screen.Bounds.Left + (screen.Bounds.Width - w) / 2;
            int y = screen.Bounds.Top + (int)(screen.Bounds.Height * 0.16f);
            Location = new Point(x, y);

            Opacity = _targetOpacity;
            ShowWindow(Handle, SW_SHOWNOACTIVATE);
            Visible = true;
            Invalidate();

            _dismissTimer.Start();
        }

        private (string title, string subtitle, Color primaryColor, Color glowColor) GetModeInfo(byte mode)
        {
            return mode switch
            {
                0x00 => (
                    "QUIET MODE",
                    "Whisper Quiet Fans \u2022 Low Power & Acoustics",
                    Color.FromArgb(0, 229, 255),       // Neon Cyan
                    Color.FromArgb(50, 0, 229, 255)
                ),
                0x04 => (
                    "PERFORMANCE MODE",
                    "High Performance Boost \u2022 Aggressive Fan Curves",
                    Color.FromArgb(255, 145, 0),      // Vibrant Amber / Gold
                    Color.FromArgb(50, 255, 145, 0)
                ),
                0x05 => (
                    "TURBO MODE",
                    _onBattery ? "AC Adapter Required for Full Turbo" : "Maximum Fan RPM \u2022 Full CPU/GPU Overclock",
                    Color.FromArgb(255, 23, 68),       // Fiery Crimson Red
                    Color.FromArgb(55, 255, 23, 68)
                ),
                0x06 => (
                    "ECO MODE",
                    "Maximum Battery Life \u2022 Energy Saver",
                    Color.FromArgb(46, 213, 115),      // Vibrant Emerald / Eco Green
                    Color.FromArgb(50, 46, 213, 115)
                ),
                _ => (
                    "BALANCED MODE",
                    "Dynamic Thermal Balance \u2022 Everyday Gaming",
                    Color.FromArgb(0, 230, 118),       // Emerald Green
                    Color.FromArgb(50, 0, 230, 118)
                )
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var (title, subtitle, primaryColor, glowColor) = GetModeInfo(_currentMode);

            // 1. Draw glowing background gradient
            using (var bgBrush = new SolidBrush(Color.FromArgb(16, 20, 28)))
            {
                g.FillRectangle(bgBrush, ClientRectangle);
            }

            // Subtle top glow gradient
            using (var glowBrush = new LinearGradientBrush(
                new Point(0, 0),
                new Point(0, (int)(35 * _dpiScale)),
                glowColor,
                Color.Transparent))
            {
                g.FillRectangle(glowBrush, 0, 0, Width, (int)(35 * _dpiScale));
            }

            // 2. Draw border
            using (var borderPen = new Pen(primaryColor, 1.8f * _dpiScale))
            {
                int r = (int)(16 * _dpiScale);
                using var path = GetRoundedPath(new Rectangle(1, 1, Width - 2, Height - 2), r);
                g.DrawPath(borderPen, path);
            }

            // 3. Draw Icon Badge
            int iconBoxSize = (int)(56 * _dpiScale);
            int iconLeft = (int)(24 * _dpiScale);
            int iconTop = (Height - iconBoxSize) / 2;
            var iconRect = new Rectangle(iconLeft, iconTop, iconBoxSize, iconBoxSize);

            // Icon background circle
            using (var circleBrush = new SolidBrush(Color.FromArgb(28, primaryColor.R, primaryColor.G, primaryColor.B)))
            using (var circlePen = new Pen(Color.FromArgb(100, primaryColor.R, primaryColor.G, primaryColor.B), 1.2f))
            {
                g.FillEllipse(circleBrush, iconRect);
                g.DrawEllipse(circlePen, iconRect);
            }

            // Draw Mode Vector Icon inside badge
            DrawModeIcon(g, iconRect, _currentMode, primaryColor);

            // 4. Draw Typography
            int textLeft = iconRect.Right + (int)(18 * _dpiScale);
            int textTop = (int)(24 * _dpiScale);

            EnsureFonts();

            // Small header tag: "PREDATOR PROFILE"
            using (var tagBrush = new SolidBrush(Color.FromArgb(160, 175, 195)))
            {
                g.DrawString("PREDATOR PERFORMANCE", _tagFont!, tagBrush, textLeft, textTop);
            }

            // Title: "TURBO MODE", etc.
            using (var titleBrush = new SolidBrush(Color.White))
            {
                g.DrawString(title, _titleFont!, titleBrush, textLeft, textTop + (int)(16 * _dpiScale));
            }

            // Subtitle
            using (var subBrush = new SolidBrush(Color.FromArgb(170, 185, 205)))
            {
                g.DrawString(subtitle, _subFont!, subBrush, textLeft, textTop + (int)(42 * _dpiScale));
            }

            // Bottom accent dot indicator
            int dotY = Height - (int)(10 * _dpiScale);
            using (var dotBrush = new SolidBrush(primaryColor))
            {
                g.FillEllipse(dotBrush, Width / 2 - (int)(3 * _dpiScale), dotY, (int)(6 * _dpiScale), (int)(6 * _dpiScale));
            }
        }

        private void DrawModeIcon(Graphics g, Rectangle rect, byte mode, Color color)
        {
            using var pen = new Pen(color, 2.2f * _dpiScale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var fillBrush = new SolidBrush(color);

            float cx = rect.X + rect.Width / 2f;
            float cy = rect.Y + rect.Height / 2f;
            float s = (rect.Width * 0.45f);

            switch (mode)
            {
                case 0x00: // Quiet: Feather / Acoustic Whisper wave
                    var p1 = new PointF(cx - s * 0.7f, cy + s * 0.3f);
                    var p2 = new PointF(cx - s * 0.2f, cy - s * 0.6f);
                    var p3 = new PointF(cx + s * 0.3f, cy + s * 0.5f);
                    var p4 = new PointF(cx + s * 0.8f, cy - s * 0.2f);
                    g.DrawBezier(pen, p1, p2, p3, p4);

                    using (var leafPen = new Pen(color, 1.8f * _dpiScale))
                    {
                        g.DrawArc(leafPen, cx - s * 0.5f, cy - s * 0.5f, s, s, 210, 120);
                        g.DrawArc(leafPen, cx - s * 0.5f, cy - s * 0.5f, s, s, 30, 120);
                    }
                    break;

                case 0x04: // Performance: Fast Tachometer
                    g.DrawArc(pen, cx - s * 0.8f, cy - s * 0.8f, s * 1.6f, s * 1.6f, 150, 240);
                    float nx = cx + (float)(s * 0.7f * Math.Cos(-Math.PI / 4.0));
                    float ny = cy + (float)(s * 0.7f * Math.Sin(-Math.PI / 4.0));
                    g.DrawLine(pen, cx, cy, nx, ny);
                    g.FillEllipse(fillBrush, cx - 3, cy - 3, 6, 6);
                    break;

                case 0x05: // Turbo: Twin Lightning Bolts
                    var bolt1 = new PointF[]
                    {
                        new(cx - s * 0.2f, cy - s * 0.85f),
                        new(cx + s * 0.3f, cy - s * 0.85f),
                        new(cx - s * 0.05f, cy - s * 0.1f),
                        new(cx + s * 0.35f, cy - s * 0.1f),
                        new(cx - s * 0.3f, cy + s * 0.9f),
                        new(cx - s * 0.1f, cy + s * 0.15f),
                        new(cx - s * 0.4f, cy + s * 0.15f)
                    };
                    g.FillPolygon(fillBrush, bolt1);
                    break;

                case 0x06: // Eco: Energy saving leaf with stem & veins
                    using (var ecoPen = new Pen(color, 2.0f * _dpiScale) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    {
                        var pStart = new PointF(cx - s * 0.65f, cy + s * 0.65f);
                        var pEnd = new PointF(cx + s * 0.65f, cy - s * 0.65f);
                        var cp1 = new PointF(cx - s * 0.65f, cy - s * 0.2f);
                        var cp2 = new PointF(cx + s * 0.2f, cy - s * 0.65f);
                        var cp3 = new PointF(cx + s * 0.65f, cy + s * 0.2f);
                        var cp4 = new PointF(cx - s * 0.2f, cy + s * 0.65f);

                        g.DrawBezier(ecoPen, pStart, cp1, cp2, pEnd);
                        g.DrawBezier(ecoPen, pEnd, cp3, cp4, pStart);
                        g.DrawLine(ecoPen, pStart, pEnd);
                        g.DrawLine(ecoPen, cx - s * 0.15f, cy + s * 0.15f, cx - s * 0.15f, cy - s * 0.15f);
                        g.DrawLine(ecoPen, cx + s * 0.15f, cy - s * 0.15f, cx + s * 0.35f, cy - s * 0.05f);
                    }
                    break;

                default: // Balanced: Balanced Gauge / Scales
                    g.DrawArc(pen, cx - s * 0.8f, cy - s * 0.8f, s * 1.6f, s * 1.6f, 150, 240);
                    g.DrawLine(pen, cx, cy, cx, cy - s * 0.75f);
                    g.FillEllipse(fillBrush, cx - 3, cy - 3, 6, 6);
                    break;
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
                _dismissTimer.Dispose();
                _fadeTimer.Dispose();
                _tagFont?.Dispose();
                _titleFont?.Dispose();
                _subFont?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
