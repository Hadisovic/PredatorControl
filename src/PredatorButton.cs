using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.Versioning;

namespace PredatorControlApp
{
    [SupportedOSPlatform("windows")]
    public class PredatorButton : Control
    {
        private bool _isHover;
        private bool _isActive;
        private Color? _customActiveColor;
        private readonly Action _themeChangedHandler;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsActive
        {
            get => _isActive;
            set { _isActive = value; Invalidate(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color? CustomActiveColor
        {
            get => _customActiveColor;
            set { _customActiveColor = value; Invalidate(); }
        }

        private Color? _colorIndicator;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color? ColorIndicator
        {
            get => _colorIndicator;
            set { _colorIndicator = value; Invalidate(); }
        }

        internal void InvokeAction() { if (Enabled) OnClick(EventArgs.Empty); }

        public PredatorButton()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);

            Font = new Font("Segoe UI", 9.25f, FontStyle.Regular);
            Size = new Size(96, 40);
            Cursor = Cursors.Hand;

            _themeChangedHandler = () => { if (IsHandleCreated && !IsDisposed) Invalidate(); };
            ThemeManager.ThemeChanged += _themeChangedHandler;
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var theme = ThemeManager.Current;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color parentBg = Parent?.BackColor ?? theme.FormBg;
            if (parentBg == Color.Transparent || parentBg.A < 255) parentBg = theme.FormBg;
            g.Clear(parentBg);

            var rect = new Rectangle(1, 1, Width - 3, Height - 3);
            using var path = RoundedRect(rect, 8);

            Color bg, border, textColor;
            float borderWidth;

            if (!Enabled)
            {
                bg = Color.FromArgb(Math.Max(0, theme.CardBg.R - 10), Math.Max(0, theme.CardBg.G - 10), Math.Max(0, theme.CardBg.B - 10));
                border = Color.FromArgb(Math.Max(0, theme.Border.R - 20), Math.Max(0, theme.Border.G - 20), Math.Max(0, theme.Border.B - 20));
                textColor = Color.FromArgb(70, 70, 75);
                borderWidth = 1f;
            }
            else if (_isActive)
            {
                bg = theme.CardActive;
                border = theme.Accent;
                textColor = _customActiveColor ?? theme.Accent;
                borderWidth = 1.6f;
            }
            else if (_isHover)
            {
                bg = theme.CardHover;
                border = theme.BorderHover;
                textColor = Color.White;
                borderWidth = 1f;
            }
            else
            {
                bg = theme.CardBg;
                border = theme.Border;
                textColor = Color.FromArgb(180, 180, 185);
                borderWidth = 1f;
            }

            using (var bgBrush = new SolidBrush(bg))
                g.FillPath(bgBrush, path);

            using (var pen = new Pen(border, borderWidth))
                g.DrawPath(pen, path);

            if (_isActive && Enabled)
            {
                using var glowPen = new Pen(Color.FromArgb(35, theme.Accent.R, theme.Accent.G, theme.Accent.B), 3f);
                g.DrawPath(glowPen, path);
            }

            if (_colorIndicator.HasValue && Enabled)
            {
                int barH = 3;
                var barRect = new Rectangle(rect.X + 8, rect.Bottom - barH - 2, Math.Max(4, rect.Width - 16), barH);
                using var barBrush = new SolidBrush(_colorIndicator.Value);
                using var barPath = RoundedRect(barRect, 1);
                g.FillPath(barBrush, barPath);
            }

            TextRenderer.DrawText(g, Text, Font, ClientRectangle, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            if (Enabled) { _isHover = true; Invalidate(); }
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _isHover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            if (!Enabled) { _isHover = false; Cursor = Cursors.Default; }
            else { Cursor = Cursors.Hand; }
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ThemeManager.ThemeChanged -= _themeChangedHandler;
            }
            base.Dispose(disposing);
        }
    }
}
