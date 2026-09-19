using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PredatorControlApp
{
    [SupportedOSPlatform("windows")]
    public class OverlaySettingsForm : Form
    {
        #region Win32 Dragging

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;

        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        private void TitleBar_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            }
        }

        #endregion

        private readonly GameOverlayForm _overlay;
        private readonly Action<bool> _setOverlayVisible;

        // Mode buttons
        private PredatorButton _btnLight = null!;
        private PredatorButton _btnDefault = null!;
        private PredatorButton _btnFull = null!;
        private PredatorButton _btnComplete = null!;

        // Checkboxes
        private PredatorCheckBox _chkFps = null!;
        private PredatorCheckBox _chkChart = null!;
        private PredatorCheckBox _chkRam = null!;
        private PredatorCheckBox _chkTemp = null!;
        private PredatorCheckBox _chkPower = null!;
        private PredatorCheckBox _chkBattery = null!;
        private PredatorCheckBox _chkFan = null!;
        private PredatorCheckBox _chkLoad = null!;
        private PredatorCheckBox _chkLabels = null!;

        // Sliders
        private Label _lblSizeVal = null!;
        private PredatorSlider _sliderSize = null!;
        private Label _lblTranspVal = null!;
        private PredatorSlider _sliderTransp = null!;

        // Color & Reset
        private PredatorButton _btnCpuColor = null!;
        private PredatorButton _btnGpuColor = null!;
        private PredatorButton _btnReset = null!;

        // Bottom
        private PredatorCheckBox _chkOverlayMaster = null!;
        private PredatorCheckBox _chkOnlyInGames = null!;

        private bool _suppressSync = false;

        public OverlaySettingsForm(GameOverlayForm overlay, Action<bool> setOverlayVisible)
        {
            _overlay = overlay;
            _setOverlayVisible = setOverlayVisible;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(20, 22, 28);
            ForeColor = Color.White;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            Size = new Size(510, 480);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildUI();
            SyncFromOverlay();

            _overlay.OverlayStateChanged += (m, s) =>
            {
                if (!IsDisposed && IsHandleCreated)
                    BeginInvoke(new Action(SyncFromOverlay));
            };
        }

        private void BuildUI()
        {
            Controls.Clear();
            int pad = 18;
            int gap = 8;
            int y = 0;

            // 1. Custom Title Bar
            var titleBar = new Panel
            {
                Height = 36,
                Dock = DockStyle.Top,
                BackColor = Color.FromArgb(16, 18, 22)
            };
            titleBar.MouseDown += TitleBar_MouseDown;

            var lblTitle = new Label
            {
                Text = "Overlay",
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(pad, 8),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            lblTitle.MouseDown += TitleBar_MouseDown;
            titleBar.Controls.Add(lblTitle);

            var lblClose = new Label
            {
                Text = "✕",
                Font = new Font("Segoe UI", 10f, FontStyle.Regular),
                ForeColor = Color.FromArgb(160, 160, 175),
                Location = new Point(Width - pad - 16, 8),
                AutoSize = true,
                Cursor = Cursors.Hand,
                BackColor = Color.Transparent
            };
            lblClose.MouseEnter += (s, e) => lblClose.ForeColor = Color.FromArgb(255, 90, 90);
            lblClose.MouseLeave += (s, e) => lblClose.ForeColor = Color.FromArgb(160, 160, 175);
            lblClose.Click += (s, e) => Close();
            titleBar.Controls.Add(lblClose);

            Controls.Add(titleBar);
            y += 44;

            // 2. Mode Buttons Row: [Light] [Default] [Full] [Complete]
            int modeBtnW = (Width - pad * 2 - gap * 3) / 4;
            int btnH = 32;

            _btnLight = MakeButton("Light", pad, y, modeBtnW, btnH);
            _btnDefault = MakeButton("Default", pad + modeBtnW + gap, y, modeBtnW, btnH);
            _btnFull = MakeButton("Full", pad + (modeBtnW + gap) * 2, y, modeBtnW, btnH);
            _btnComplete = MakeButton("Complete", pad + (modeBtnW + gap) * 3, y, modeBtnW, btnH);

            _btnLight.Click += (s, e) => { _overlay.SetMode(OverlayMode.Light); SyncFromOverlay(); };
            _btnDefault.Click += (s, e) => { _overlay.SetMode(OverlayMode.Default); SyncFromOverlay(); };
            _btnFull.Click += (s, e) => { _overlay.SetMode(OverlayMode.Full); SyncFromOverlay(); };
            _btnComplete.Click += (s, e) => { _overlay.SetMode(OverlayMode.Complete); SyncFromOverlay(); };

            Controls.Add(_btnLight);
            Controls.Add(_btnDefault);
            Controls.Add(_btnFull);
            Controls.Add(_btnComplete);
            y += btnH + 16;

            // 3. Checkboxes in 3 Columns
            int colW = (Width - pad * 2 - gap * 2) / 3;
            int chkH = 26;

            // Column 1
            int col1X = pad;
            _chkFps = new PredatorCheckBox { Text = "FPS", Location = new Point(col1X, y), Size = new Size(colW, chkH) };
            _chkChart = new PredatorCheckBox { Text = "Chart", Location = new Point(col1X, y + chkH + 4), Size = new Size(colW, chkH) };
            _chkRam = new PredatorCheckBox { Text = "RAM", Location = new Point(col1X, y + (chkH + 4) * 2), Size = new Size(colW, chkH) };

            // Column 2
            int col2X = pad + colW + gap;
            _chkTemp = new PredatorCheckBox { Text = "Temperatures", Location = new Point(col2X, y), Size = new Size(colW, chkH) };
            _chkPower = new PredatorCheckBox { Text = "Power", Location = new Point(col2X, y + chkH + 4), Size = new Size(colW, chkH) };
            _chkBattery = new PredatorCheckBox { Text = "Battery", Location = new Point(col2X, y + (chkH + 4) * 2), Size = new Size(colW, chkH) };

            // Column 3
            int col3X = pad + (colW + gap) * 2;
            _chkFan = new PredatorCheckBox { Text = "Fan", Location = new Point(col3X, y), Size = new Size(colW, chkH) };
            _chkLoad = new PredatorCheckBox { Text = "Load", Location = new Point(col3X, y + chkH + 4), Size = new Size(colW, chkH) };
            _chkLabels = new PredatorCheckBox { Text = "Labels", Location = new Point(col3X, y + (chkH + 4) * 2), Size = new Size(colW, chkH) };

            // Wire Checkbox change handlers
            _chkFps.CheckedChanged += OnCheckboxChanged;
            _chkChart.CheckedChanged += OnCheckboxChanged;
            _chkRam.CheckedChanged += OnCheckboxChanged;
            _chkTemp.CheckedChanged += OnCheckboxChanged;
            _chkPower.CheckedChanged += OnCheckboxChanged;
            _chkBattery.CheckedChanged += OnCheckboxChanged;
            _chkFan.CheckedChanged += OnCheckboxChanged;
            _chkLoad.CheckedChanged += OnCheckboxChanged;
            _chkLabels.CheckedChanged += OnCheckboxChanged;

            Controls.Add(_chkFps);
            Controls.Add(_chkChart);
            Controls.Add(_chkRam);
            Controls.Add(_chkTemp);
            Controls.Add(_chkPower);
            Controls.Add(_chkBattery);
            Controls.Add(_chkFan);
            Controls.Add(_chkLoad);
            Controls.Add(_chkLabels);

            y += (chkH + 4) * 3 + 12;

            // 4. Sliders Row: Size (Left) & Transparency (Right)
            int sliderColW = (Width - pad * 2 - gap * 2) / 2;

            // Size Header
            var lblSizeHdr = new Label
            {
                Text = "Size",
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                ForeColor = Color.FromArgb(200, 200, 215),
                Location = new Point(pad, y),
                AutoSize = true
            };
            _lblSizeVal = new Label
            {
                Text = $"{_overlay.ScalePercent}%",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(pad + sliderColW - 45, y),
                AutoSize = true,
                TextAlign = ContentAlignment.TopRight
            };
            Controls.Add(lblSizeHdr);
            Controls.Add(_lblSizeVal);

            // Transparency Header
            var lblTranspHdr = new Label
            {
                Text = "Transparency",
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                ForeColor = Color.FromArgb(200, 200, 215),
                Location = new Point(pad + sliderColW + gap * 2, y),
                AutoSize = true
            };
            _lblTranspVal = new Label
            {
                Text = $"{_overlay.TransparencyPercent}%",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(Width - pad - 45, y),
                AutoSize = true,
                TextAlign = ContentAlignment.TopRight
            };
            Controls.Add(lblTranspHdr);
            Controls.Add(_lblTranspVal);

            y += 20;

            // Size Slider
            _sliderSize = new PredatorSlider
            {
                Location = new Point(pad, y),
                Size = new Size(sliderColW, 26),
                Minimum = 50,
                Maximum = 200,
                Value = Math.Clamp(_overlay.ScalePercent, 50, 200)
            };
            _sliderSize.ValueChanged += (s, e) =>
            {
                _lblSizeVal.Text = $"{_sliderSize.Value}%";
                _overlay.SetScale(_sliderSize.Value);
            };
            Controls.Add(_sliderSize);

            // Transparency Slider
            _sliderTransp = new PredatorSlider
            {
                Location = new Point(pad + sliderColW + gap * 2, y),
                Size = new Size(sliderColW, 26),
                Minimum = 10,
                Maximum = 100,
                Value = Math.Clamp(_overlay.TransparencyPercent, 10, 100)
            };
            _sliderTransp.ValueChanged += (s, e) =>
            {
                _lblTranspVal.Text = $"{_sliderTransp.Value}%";
                _overlay.SetTransparency(_sliderTransp.Value);
            };
            Controls.Add(_sliderTransp);

            y += 36;

            // 5. Buttons: [ CPU Color ■ ] [ GPU Color ■ ] [ Reset ]
            int colBtnW = (Width - pad * 2 - gap * 2) / 3;
            _btnCpuColor = MakeButton("CPU Color", pad, y, colBtnW, 30);
            _btnCpuColor.ColorIndicator = _overlay.CpuColor;
            _btnCpuColor.Click += PickCpuColor;

            _btnGpuColor = MakeButton("GPU Color", pad + colBtnW + gap, y, colBtnW, 30);
            _btnGpuColor.ColorIndicator = _overlay.GpuColor;
            _btnGpuColor.Click += PickGpuColor;

            _btnReset = MakeButton("Reset", pad + (colBtnW + gap) * 2, y, colBtnW, 30);
            _btnReset.Click += (s, e) =>
            {
                _overlay.ResetToDefaults();
                SyncFromOverlay();
            };

            Controls.Add(_btnCpuColor);
            Controls.Add(_btnGpuColor);
            Controls.Add(_btnReset);

            y += 38;

            // 6. Shortcut Tips Box
            string[] tips =
            {
                "•   Ctrl + Shift + Alt + O  -  Toggle overlay",
                "•   Ctrl + Shift + Alt + Mouse Drag  -  Move overlay",
                "•   Ctrl + Shift + Alt + Mouse Click  -  Switch mode",
                "•   Ctrl + Shift + Alt + Wheel  -  Resize overlay",
                "•   Ctrl + Shift + Alt + Wheel Click  -  Reset size"
            };

            int tipY = y;
            foreach (var tip in tips)
            {
                var lblTip = new Label
                {
                    Text = tip,
                    Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                    ForeColor = Color.FromArgb(135, 140, 155),
                    Location = new Point(pad + 4, tipY),
                    AutoSize = true
                };
                Controls.Add(lblTip);
                tipY += 16;
            }

            y = tipY + 10;

            // 7. Bottom Row: [✓] Overlay       [ ] Overlay only in games
            _chkOverlayMaster = new PredatorCheckBox
            {
                Text = "Overlay",
                Location = new Point(pad, y),
                Size = new Size(130, 26),
                Checked = _overlay.Visible
            };
            _chkOverlayMaster.CheckedChanged += (s, e) =>
            {
                if (_suppressSync) return;
                _setOverlayVisible(_chkOverlayMaster.Checked);
            };
            Controls.Add(_chkOverlayMaster);

            _chkOnlyInGames = new PredatorCheckBox
            {
                Text = "Overlay only in games",
                Location = new Point(Width - pad - 180, y),
                Size = new Size(180, 26),
                Checked = _overlay.OverlayOnlyInGames
            };
            _chkOnlyInGames.CheckedChanged += (s, e) =>
            {
                if (_suppressSync) return;
                _overlay.OverlayOnlyInGames = _chkOnlyInGames.Checked;
                _overlay.SaveSettings();
            };
            Controls.Add(_chkOnlyInGames);
        }

        private void OnCheckboxChanged(object? sender, EventArgs e)
        {
            if (_suppressSync) return;

            _overlay.ShowFps = _chkFps.Checked;
            _overlay.ShowChart = _chkChart.Checked;
            _overlay.ShowRam = _chkRam.Checked;
            _overlay.ShowTemperatures = _chkTemp.Checked;
            _overlay.ShowPower = _chkPower.Checked;
            _overlay.ShowBattery = _chkBattery.Checked;
            _overlay.ShowFan = _chkFan.Checked;
            _overlay.ShowLoad = _chkLoad.Checked;
            _overlay.ShowLabels = _chkLabels.Checked;

            _overlay.SaveSettings();
            _overlay.RefreshLayout();
        }

        public void SyncFromOverlay()
        {
            _suppressSync = true;
            try
            {
                // Mode buttons highlight
                _btnLight.IsActive = (_overlay.Mode == OverlayMode.Light);
                _btnDefault.IsActive = (_overlay.Mode == OverlayMode.Default);
                _btnFull.IsActive = (_overlay.Mode == OverlayMode.Full);
                _btnComplete.IsActive = (_overlay.Mode == OverlayMode.Complete);

                // Checkboxes
                _chkFps.Checked = _overlay.ShowFps;
                _chkChart.Checked = _overlay.ShowChart;
                _chkRam.Checked = _overlay.ShowRam;
                _chkTemp.Checked = _overlay.ShowTemperatures;
                _chkPower.Checked = _overlay.ShowPower;
                _chkBattery.Checked = _overlay.ShowBattery;
                _chkFan.Checked = _overlay.ShowFan;
                _chkLoad.Checked = _overlay.ShowLoad;
                _chkLabels.Checked = _overlay.ShowLabels;

                // Sliders
                if (_sliderSize != null && _sliderSize.Value != _overlay.ScalePercent)
                    _sliderSize.Value = Math.Clamp(_overlay.ScalePercent, 50, 200);
                if (_lblSizeVal != null)
                    _lblSizeVal.Text = $"{_overlay.ScalePercent}%";

                if (_sliderTransp != null && _sliderTransp.Value != _overlay.TransparencyPercent)
                    _sliderTransp.Value = Math.Clamp(_overlay.TransparencyPercent, 10, 100);
                if (_lblTranspVal != null)
                    _lblTranspVal.Text = $"{_overlay.TransparencyPercent}%";

                // Colors
                if (_btnCpuColor != null) _btnCpuColor.ColorIndicator = _overlay.CpuColor;
                if (_btnGpuColor != null) _btnGpuColor.ColorIndicator = _overlay.GpuColor;

                // Bottom toggles
                if (_chkOverlayMaster != null) _chkOverlayMaster.Checked = _overlay.Visible;
                if (_chkOnlyInGames != null) _chkOnlyInGames.Checked = _overlay.OverlayOnlyInGames;
            }
            finally
            {
                _suppressSync = false;
            }
        }

        private void PickCpuColor(object? sender, EventArgs e)
        {
            using var dlg = new ColorDialog
            {
                Color = _overlay.CpuColor,
                FullOpen = true
            };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _overlay.CpuColor = dlg.Color;
                _btnCpuColor.ColorIndicator = dlg.Color;
                _overlay.SaveSettings();
                _overlay.Invalidate();
            }
        }

        private void PickGpuColor(object? sender, EventArgs e)
        {
            using var dlg = new ColorDialog
            {
                Color = _overlay.GpuColor,
                FullOpen = true
            };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _overlay.GpuColor = dlg.Color;
                _btnGpuColor.ColorIndicator = dlg.Color;
                _overlay.SaveSettings();
                _overlay.Invalidate();
            }
        }

        private static PredatorButton MakeButton(string text, int x, int y, int w, int h)
        {
            return new PredatorButton
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, h),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Regular)
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            using var borderPen = new Pen(Color.FromArgb(50, 52, 62), 1f);
            g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
        }
    }
}
