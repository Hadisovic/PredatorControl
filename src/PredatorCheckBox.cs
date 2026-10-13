using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.Versioning;

namespace PredatorControlApp
{
    [SupportedOSPlatform("windows")]
    public class PredatorCheckBox : Control
    {
        private bool _checked;
        private bool _isHovered;
        private Color _checkColor = Color.FromArgb(56, 182, 255); // Sky Cyan matching Image 1

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked != value)
                {
                    _checked = value;
                    CheckedChanged?.Invoke(this, EventArgs.Empty);
                    Invalidate();
                }
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color CheckColor
        {
            get => _checkColor;
            set { _checkColor = value; Invalidate(); }
        }

        public event EventHandler? CheckedChanged;

        public PredatorCheckBox()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.Selectable |
                ControlStyles.SupportsTransparentBackColor, true);

            BackColor = Color.Transparent;
            ForeColor = Color.FromArgb(225, 225, 235);
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            Size = new Size(130, 26);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _isHovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _isHovered = false;
            Invalidate();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Checked = !Checked;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space)
            {
                Checked = !Checked;
                e.Handled = true;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            int boxSize = 16;
            int boxY = (Height - boxSize) / 2;
            int boxX = 2;

            var boxRect = new Rectangle(boxX, boxY, boxSize, boxSize);

            // Box background & border
            using (var path = GetRoundedPath(boxRect, 3))
            {
                if (_checked)
                {
                    using var fillBrush = new SolidBrush(_checkColor);
                    g.FillPath(fillBrush, path);

                    using var checkPen = new Pen(Color.FromArgb(12, 14, 18), 2.2f)
                    {
                        StartCap = LineCap.Round,
                        EndCap = LineCap.Round
                    };
                    // Checkmark
                    g.DrawLine(checkPen, boxX + 4f, boxY + 8f, boxX + 7f, boxY + 11.5f);
                    g.DrawLine(checkPen, boxX + 7f, boxY + 11.5f, boxX + 12f, boxY + 4.5f);
                }
                else
                {
                    using var bgBrush = new SolidBrush(Color.FromArgb(28, 30, 36));
                    g.FillPath(bgBrush, path);

                    Color borderCol = _isHovered ? Color.FromArgb(120, 130, 150) : Color.FromArgb(70, 75, 88);
                    using var borderPen = new Pen(borderCol, 1.2f);
                    g.DrawPath(borderPen, path);
                }
            }

            // Text
            if (!string.IsNullOrEmpty(Text))
            {
                Color textCol = _isHovered ? Color.White : ForeColor;
                using var textBrush = new SolidBrush(textCol);
                var textRect = new Rectangle(boxX + boxSize + 8, 0, Width - (boxX + boxSize + 8), Height);
                using var sf = new StringFormat
                {
                    LineAlignment = StringAlignment.Center,
                    Alignment = StringAlignment.Near
                };
                g.DrawString(Text, Font, textBrush, textRect, sf);
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
    }
}
