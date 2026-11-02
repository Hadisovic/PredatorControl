// ====================================================================================================
// PREDATOR CONTROL · CLASSIC DASHBOARD UI CONSTRUCTION & RESPONSIVE DPI LAYOUT
// File: src/Form1.UI.cs
// ====================================================================================================

using System.Runtime.Versioning;

namespace PredatorControlApp
{
    [SupportedOSPlatform("windows")]
    public partial class Form1
    {
        #region UI Building & Responsive Layout

        private void BuildUI()
        {
            Controls.Clear();
            _separators.Clear();
            var theme = ThemeManager.Current;
            BackColor = theme.FormBg;
            ForeColor = Color.White;

            int initialW = S(470);
            int workH = Screen.PrimaryScreen?.WorkingArea.Height ?? S(1000);
            ClientSize = new Size(initialW, Math.Max(S(500), Math.Min(S(980), workH - 40)));

            FormBorderStyle = FormBorderStyle.Sizable;
            ControlBox = false;
            Text = string.Empty;
            MinimumSize = new Size(S(460), S(520));
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
                Text = _wmi.ChassisFamily == AcerChassisFamily.Nitro ? "Nitro Control" : "Predator Control",
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
                Text = "\u25CF",
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
                Text = "\u25CF",
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
            _lblCpuHdr = MakeLabel("CPU:", pad, y, FontBody, Color.FromArgb(120, 120, 135));
            _lblCpuHdr.AutoSize = true;
            _lblCpuTemp = MakeLabel("--\u00b0C", _lblCpuHdr.Right + S(6), y, FontBodyBold, Color.White);
            _lblCpuTemp.AutoSize = true;

            int midX = ClientSize.Width / 2;
            _lblGpuHdr = MakeLabel("GPU:", midX + S(10), y, FontBody, Color.FromArgb(120, 120, 135));
            _lblGpuHdr.AutoSize = true;
            _lblGpuTemp = MakeLabel("--\u00b0C", _lblGpuHdr.Right + S(6), y, FontBodyBold, Color.White);
            _lblGpuTemp.AutoSize = true;

            y += S(24);
            _lblCpuFanHdr = MakeLabel("CPU FAN:", pad, y, FontBody, Color.FromArgb(120, 120, 135));
            _lblCpuFanHdr.AutoSize = true;
            _lblCpuRpm = MakeLabel("N/A", _lblCpuFanHdr.Right + S(6), y, FontBodyBold, Color.White);
            _lblCpuRpm.AutoSize = true;

            _lblGpuFanHdr = MakeLabel("GPU FAN:", midX + S(10), y, FontBody, Color.FromArgb(120, 120, 135));
            _lblGpuFanHdr.AutoSize = true;
            _lblGpuRpm = MakeLabel("N/A", _lblGpuFanHdr.Right + S(6), y, FontBodyBold, Color.White);
            _lblGpuRpm.AutoSize = true;

            if (_wmi.HasAuxFan)
            {
                y += S(20);
                _lblAuxFanHdr = MakeLabel("AUX FAN:", pad, y, FontBody, Color.FromArgb(120, 120, 135));
                _lblAuxFanHdr.AutoSize = true;
                _lblAuxRpm = MakeLabel("N/A", _lblAuxFanHdr.Right + S(6), y, FontBodyBold, Color.White);
                _lblAuxRpm.AutoSize = true;
            }

            y += S(24);
            _lblFanHdr = MakeLabel("Fan speed:", pad, y, FontBody, Color.FromArgb(120, 120, 135));
            _lblFanHdr.AutoSize = true;
            _lblFanStatus = MakeLabel("Auto", _lblFanHdr.Right + S(6), y, FontBodyBold, Color.White);
            _lblFanStatus.AutoSize = true;

            _lblPowerHdr = MakeLabel("Power:", midX + S(10), y, FontBody, Color.FromArgb(120, 120, 135));
            _lblPowerHdr.AutoSize = true;
            _lblPowerStatus = MakeLabel("Checking...", _lblPowerHdr.Right + S(6), y, FontBodyBold, Color.White);
            _lblPowerStatus.AutoSize = true;

            _lblCpuHdr.SizeChanged += (s, e) => RepositionTelemetryLabels();
            _lblCpuFanHdr.SizeChanged += (s, e) => RepositionTelemetryLabels();
            _lblFanHdr.SizeChanged += (s, e) => RepositionTelemetryLabels();
            _lblGpuHdr.SizeChanged += (s, e) => RepositionTelemetryLabels();
            _lblGpuFanHdr.SizeChanged += (s, e) => RepositionTelemetryLabels();
            _lblPowerHdr.SizeChanged += (s, e) => RepositionTelemetryLabels();
            if (_lblAuxFanHdr != null)
                _lblAuxFanHdr.SizeChanged += (s, e) => RepositionTelemetryLabels();
            RepositionTelemetryLabels();

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
            string balancedText = "Balanced";
            _btnQuiet = MakeButton("Quiet", pad, y, btnW, btnH);
            _btnBalanced = MakeButton(balancedText, pad + (btnW + gap), y, btnW, btnH);
            _btnPerform = MakeButton("Perf", pad + (btnW + gap) * 2, y, btnW, btnH);
            _btnTurbo = MakeButton("Turbo", pad + (btnW + gap) * 3, y, btnW, btnH);
            _btnEco = MakeButton("Eco", pad + (btnW + gap) * 4, y, btnW, btnH);

            _btnQuiet.Click += (s, e) => ApplyPowerMode(0x00, _btnQuiet);
            _btnBalanced.Click += (s, e) => ApplyPowerMode(0x01, _btnBalanced);
            _btnPerform.Click += (s, e) => ApplyPowerMode(0x04, _btnPerform);
            _btnTurbo.Click += (s, e) => ApplyPowerMode(0x05, _btnTurbo);
            _btnEco.Click += (s, e) => ApplyPowerMode(UsesWindowsPowerPlans ? (byte)2 : (byte)6, _btnEco);
            LayoutPowerControls(pad, contentW, gap);
            if (_wmi.Capabilities.PowerModeCount == 0)
                MakeLabel("No verified power controls · export a compatibility report", pad, y, FontBody, Color.Gray);

            y += btnH + S(14);
            if (_wmi.Capabilities.PowerPlanNotice is string planNotice)
            {
                _lblPowerPlanNotice = MakeLabel(planNotice, pad, y, FontBody, Color.Gray);
                _lblPowerPlanNotice.AutoSize = false; _lblPowerPlanNotice.Size = new Size(contentW, S(42));
                y += S(48);
            }
            int profileDropW = (contentW - gap) / 2;
            _lblAcProfileHdr = MakeLabel("ON AC POWER:", pad, y, FontSectionHeader, Color.FromArgb(120, 120, 135));
            _lblBatteryProfileHdr = MakeLabel("ON BATTERY:", pad + profileDropW + gap, y, FontSectionHeader, Color.FromArgb(120, 120, 135));

            y += S(20);
            _cboAcProfile = new PredatorDropDown { Location = new Point(pad, y), Size = new Size(profileDropW, S(30)) };
            _contentPanel.Controls.Add(_cboAcProfile);

            _cboBatteryProfile = new PredatorDropDown { Location = new Point(pad + profileDropW + gap, y), Size = new Size(profileDropW, S(30)) };
            PopulateAutoModes();
            _contentPanel.Controls.Add(_cboBatteryProfile);

            _cboAcProfile.SelectedIndexChanged += (s, e) =>
            {
                SaveState("AutoPowerACValue", _acModeValues[_cboAcProfile.SelectedIndex]);
                if (_isPluggedIn == true) ApplyPowerRules(true);
            };
            _cboBatteryProfile.SelectedIndexChanged += (s, e) =>
            {
                SaveState("AutoPowerBatteryValue", _batteryModeValues[_cboBatteryProfile.SelectedIndex]);
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

            // Custom fan controls grouped inside collapsible _pnlCustomFan
            int fanSliderW = _wmi.HasAuxFan ? (contentW - gap * 2) / 3 : (contentW - gap) / 2;
            int subBtnW = (contentW - gap) / 2;
            int customH = S(22) + S(28) + S(8) + btnH;
            _customFanShiftHeight = customH + S(12);

            _pnlCustomFan = new Panel
            {
                Location = new Point(pad, y),
                Size = new Size(contentW, customH),
                Visible = false,
                BackColor = ThemeManager.Current.FormBg
            };
            _contentPanel.Controls.Add(_pnlCustomFan);

            _lblCpuFanSpeedHdr = MakeLabelIn(_pnlCustomFan, "CPU FAN: 50%", 0, 0, FontSectionHeader, Color.FromArgb(120, 120, 135));
            _lblGpuFanSpeedHdr = MakeLabelIn(_pnlCustomFan, "GPU FAN: 50%", fanSliderW + gap, 0, FontSectionHeader, Color.FromArgb(120, 120, 135));

            _cpuFanSlider = new PredatorSlider
            {
                Location = new Point(0, S(22)),
                Size = new Size(fanSliderW, S(28)),
                Minimum = 10, Maximum = 100, Value = 50,
            };
            _gpuFanSlider = new PredatorSlider
            {
                Location = new Point(fanSliderW + gap, S(22)),
                Size = new Size(fanSliderW, S(28)),
                Minimum = 10, Maximum = 100, Value = 50,
            };
            _pnlCustomFan.Controls.Add(_cpuFanSlider);
            _pnlCustomFan.Controls.Add(_gpuFanSlider);

            if (_wmi.HasAuxFan)
            {
                _lblAuxFanSpeedHdr = MakeLabelIn(_pnlCustomFan, "AUX FAN: 50%", (fanSliderW + gap) * 2, 0, FontSectionHeader, Color.FromArgb(120, 120, 135));
                _auxFanSlider = new PredatorSlider
                {
                    Location = new Point((fanSliderW + gap) * 2, S(22)),
                    Size = new Size(fanSliderW, S(28)),
                    Minimum = 10, Maximum = 100, Value = 50,
                };
                _pnlCustomFan.Controls.Add(_auxFanSlider);

                _auxFanSlider.ValueChanged += (s, e) => { if (_lblAuxFanSpeedHdr != null) _lblAuxFanSpeedHdr.Text = $"AUX FAN: {_auxFanSlider.Value}%"; };
                _auxFanSlider.ValueCommitted += (s, e) =>
                {
                    int auxVal = _auxFanSlider.Value;
                    SaveState("FanSpeedAux", auxVal);
                    _ = Task.Run(() => _wmi.SetAuxFanSpeed((byte)auxVal));
                };
            }

            _cpuFanSlider.ValueChanged += (s, e) => _lblCpuFanSpeedHdr.Text = $"CPU FAN: {_cpuFanSlider.Value}%";
            _gpuFanSlider.ValueChanged += (s, e) => _lblGpuFanSpeedHdr.Text = $"GPU FAN: {_gpuFanSlider.Value}%";

            _cpuFanSlider.ValueCommitted += (s, e) =>
            {
                int cpuVal = _cpuFanSlider.Value;
                SaveState("FanSpeedCpu", cpuVal);
                _ = Task.Run(() => _wmi.SetCpuFanSpeed((byte)cpuVal));
            };
            _gpuFanSlider.ValueCommitted += (s, e) =>
            {
                int gpuVal = _gpuFanSlider.Value;
                SaveState("FanSpeedGpu", gpuVal);
                _ = Task.Run(() => _wmi.SetGpuFanSpeed((byte)gpuVal));
            };

            int subBtnY = S(22) + S(28) + S(8);
            _btnFixedSpeed = MakeButtonIn(_pnlCustomFan, "Fixed Speed", 0, subBtnY, subBtnW, btnH);
            _btnFanCurve = MakeButtonIn(_pnlCustomFan, "Curve", subBtnW + gap, subBtnY, subBtnW, btnH);

            _btnFixedSpeed.Click += (s, e) =>
            {
                _fanCurveEnabled = false;
                _lastCurveCpuSpeed = -1;
                _lastCurveGpuSpeed = -1;
                _lastCurveAuxSpeed = -1;
                HighlightBtn(_btnFixedSpeed, ref _activeCustomSubBtn);
                SaveState("FanCurveEnabled", 0);

                if (_fanCurveForm != null && !_fanCurveForm.IsDisposed)
                    _fanCurveForm.Close();

                _cpuFanSlider.Enabled = true;
                _gpuFanSlider.Enabled = true;
                int cpuVal = _cpuFanSlider.Value;
                int gpuVal = _gpuFanSlider.Value;
                int? auxVal = null;
                if (_auxFanSlider != null)
                {
                    _auxFanSlider.Enabled = true;
                    auxVal = _auxFanSlider.Value;
                }

                _ = Task.Run(() =>
                {
                    _wmi.SetFanSpeed((byte)cpuVal, (byte)gpuVal);
                    if (auxVal.HasValue) _wmi.SetAuxFanSpeed((byte)auxVal.Value);
                });
            };

            _btnFanCurve.Click += (s, e) =>
            {
                HighlightBtn(_btnFanCurve, ref _activeCustomSubBtn);
                _cpuFanSlider.Enabled = false;
                _gpuFanSlider.Enabled = false;
                if (_auxFanSlider != null) _auxFanSlider.Enabled = false;
                OpenFanCurveEditor();
            };

            // Acer CoolBoost: placed directly under fan buttons (shifts down when Custom is expanded)
            if (_wmi.Capabilities.SupportsCoolBoost)
            {
                int coolH = S(30);
                _lblCoolBoost = MakeLabel("Acer CoolBoost", pad, y, FontBody, Color.FromArgb(120, 120, 135));
                CenterV(_lblCoolBoost, y, coolH);

                _switchCoolBoost = new PredatorToggle
                {
                    Location = new Point(ClientSize.Width - pad - S(48), y),
                    Size = new Size(S(48), coolH),
                    Checked = _wmi.CoolBoost
                };
                _contentPanel.Controls.Add(_switchCoolBoost);

                _switchCoolBoost.CheckedChanged += async (s, e) =>
                    await ChangeHardwareToggle(_switchCoolBoost, "CoolBoostEnabled", HardwareFeature.CoolBoost, value => _wmi.SetCoolBoost(value));
                y += coolH + S(14);
            }

            // NOTEBOOK DISPLAY REFRESH RATE
            y += S(6);
            string displayTitle = "NOTEBOOK DISPLAY REFRESH RATE";
            _lblDisplayHdr = MakeLabel(displayTitle, pad, y, FontSectionHeader, Color.FromArgb(120, 120, 135));

            y += S(24);
            if (_maxHz <= 60)
            {
                _btn60Hz = MakeButton("60 Hz", pad, y, contentW, btnH);
                _btnMaxHz = null;
                _btn60Hz.Click += (s, e) => ApplyDisplayMode(60, _btn60Hz);
            }
            else
            {
                int dispBtnW = (contentW - gap) / 2;
                _btn60Hz = MakeButton("60 Hz", pad, y, dispBtnW, btnH);
                _btnMaxHz = MakeButton($"{_maxHz} Hz (Max)", pad + dispBtnW + gap, y, dispBtnW, btnH);

                _btn60Hz.Click += (s, e) => ApplyDisplayMode(60, _btn60Hz);
                _btnMaxHz.Click += (s, e) => ApplyDisplayMode(_maxHz, _btnMaxHz);
            }

            y += btnH + S(12);
            int switchH = S(30);
            if (_wmi.Capabilities.SupportsLcdOverdrive)
            {
                _lblLcdOverdrive = MakeLabel("LCD Overdrive (3ms Response Boost)", pad, y, FontBody, Color.FromArgb(120, 120, 135));
                CenterV(_lblLcdOverdrive, y, switchH);

                _switchLcdOverdrive = new PredatorToggle
                {
                    Location = new Point(ClientSize.Width - pad - S(48), y),
                    Size = new Size(S(48), switchH)
                };
                _contentPanel.Controls.Add(_switchLcdOverdrive);

                _switchLcdOverdrive.CheckedChanged += async (s, e) =>
                    await ChangeHardwareToggle(_switchLcdOverdrive, "LcdOverdrive", HardwareFeature.LcdOverdrive, value => _wmi.SetLcdOverdrive(value));
                y += switchH + S(16);
            }
            else
            {
                y += S(10);
            }

            AddSeparator(y);

            // GPU WORKING MODE (MUX SWITCH)
            if (_wmi.Capabilities.SupportsGpuMux)
            {
                _lblGpuModeHdr = MakeLabel("GPU WORKING MODE (MUX SWITCH)", pad, y, FontSectionHeader, Color.FromArgb(120, 120, 135));

                y += S(24);
                int gpuBtnW = (contentW - 2 * gap) / 3;
                _btnGpuOptimus = MakeButton("Optimus", pad, y, gpuBtnW, btnH);
                _btnGpuDiscrete = MakeButton("Discrete GPU", pad + gpuBtnW + gap, y, gpuBtnW, btnH);
                _btnGpuAuto = MakeButton("Auto (Adv)", pad + (gpuBtnW + gap) * 2, y, gpuBtnW, btnH);

                _btnGpuOptimus.Click += (s, e) => ApplyGpuMode(AcerAgentClient.GPU_MODE_OPTIMUS, _btnGpuOptimus, "Optimus (Hybrid)");
                _btnGpuDiscrete.Click += (s, e) => ApplyGpuMode(AcerAgentClient.GPU_MODE_DISCRETE, _btnGpuDiscrete, "Discrete GPU Only");
                _btnGpuAuto.Click += (s, e) => ApplyGpuMode(AcerAgentClient.GPU_MODE_AUTO, _btnGpuAuto, "Auto (Advanced Optimus)");

                HighlightBtn(_btnGpuOptimus, ref _activeGpuModeBtn);
                _currentGpuMode = AcerAgentClient.GPU_MODE_OPTIMUS;

                y += btnH + S(8);
                _lblGpuRestartNotice = MakeLabel("Requires system restart to take effect in firmware", pad, y, FontBody, Color.FromArgb(120, 120, 135));

                y += S(22);
                AddSeparator(y);
            }

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

            RefreshExternalMonitorSection();

            y += _pnlExternalMonitors.Height + S(8);

            // BATTERY CHARGE LIMIT
            y += btnH + S(28);
            MakeSectionHeader("BATTERY CHARGE LIMIT", pad, y);

            y += S(24);
            switchH = S(30);
            _lblBatteryStatus = MakeLabel("Full Charge (100%)", pad, y, FontBody, Color.FromArgb(120, 120, 135));
            CenterV(_lblBatteryStatus, y, switchH);

            _switchBatteryLimit = new PredatorToggle
            {
                Location = new Point(ClientSize.Width - pad - S(48), y),
                Size = new Size(S(48), switchH)
            };
            _contentPanel.Controls.Add(_switchBatteryLimit);

            _switchBatteryLimit.CheckedChanged += (s, e) => ApplyBatteryLimit(_switchBatteryLimit.Checked);

            y += switchH + S(6);
            _lblBatteryHealth = MakeLabel("Battery Diagnostics: Calculating...", pad, y, FontBody, Color.FromArgb(120, 120, 135));
            _lblBatteryHealth.AutoSize = true;
            _lblBatteryHealth.MaximumSize = new Size(contentW, 0);
            _contentPanel.Controls.Add(_lblBatteryHealth);

            UpdateBatteryHealthUI();

            y += S(22);
            int batBtnW = S(210), batBtnH = S(28);
            var btnBatteryReport = MakeButton("📊  View Full Battery Report", pad, y, batBtnW, batBtnH);
            btnBatteryReport.Click += (s, e) =>
            {
                BatteryHealthMonitor.OpenBatteryReport();
                UpdateBatteryHealthUI(forceRefresh: true);
            };
            _contentPanel.Controls.Add(btnBatteryReport);

            y += batBtnH + S(6);
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
            if (!_wmi.Capabilities.SupportsRgbLighting)
            {
                MakeSectionHeader("KEYBOARD BACKLIGHT", pad, y);
                y += S(24);
                var keyboard = _wmi.Keyboard;
                MakeLabel(keyboard.SupportsPower ? "On/off keyboard backlight" : "Keyboard type unverified", pad, y, FontBody, Color.FromArgb(140, 140, 155));
                y += S(28);
                _lblKeyboardPower = MakeLabel("Keyboard backlight (unverified)", pad, y, FontBody, Color.FromArgb(180, 190, 200));
                _switchKeyboardPower = new PredatorToggle
                {
                    Location = new Point(ClientSize.Width - pad - S(48), y),
                    Size = new Size(S(48), switchH), Enabled = keyboard.SupportsPower
                };
                _contentPanel.Controls.Add(_switchKeyboardPower);
                _switchKeyboardPower.CheckedChanged += async (_, _) =>
                {
                    if (_restoringLighting || _updatingKeyboard) return;
                    await ChangeKeyboardFromClassicAsync(true, _switchKeyboardPower.Checked);
                };
                y += S(28);
                _brightnessSlider = new PredatorSlider { Minimum = 0, Maximum = 100, Value = 100 };

                MakeLabel(keyboard.Evidence, pad, y, FontBody, Color.Gray);
                y += S(44);
                _rgbDropDown = new PredatorDropDown();
                foreach (var name in RgbModeNames) _rgbDropDown.Items.Add(name);
                SelectRgbMode(0);
                _cboRgbProfiles = new PredatorDropDown();
                _speedSlider = new PredatorSlider();
            }
            else
            {
                MakeSectionHeader("KEYBOARD RGB LIGHTING", pad, y);

                y += S(24);
                int dropH = S(34);
                _rgbDropDown = new PredatorDropDown { Location = new Point(pad, y), Size = new Size(contentW, dropH) };
                _rgbModeValues = _wmi.Keyboard.Effects?.ToArray() ?? [];
                foreach (int effect in _rgbModeValues) _rgbDropDown.Items.Add(RgbModeNames[effect]);
                _rgbDropDown.SelectedIndex = _rgbModeValues.Length > 0 ? 0 : -1;
                _contentPanel.Controls.Add(_rgbDropDown);

                // 4-Zone Keyboard Lighting Controls
                y += dropH + S(16);
                MakeLabel($"KEYBOARD {_wmi.Keyboard.ZoneCount}-ZONE CONFIG:", pad, y, FontSectionHeader, Color.FromArgb(120, 120, 135));

                y += S(20);
                int zoneBtnW = (contentW - _wmi.Keyboard.ZoneCount * gap) / Math.Max(1, _wmi.Keyboard.ZoneCount + 1);
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
                    _btnZones[z].Visible = z < _wmi.Keyboard.ZoneCount;
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
                    _btnPresetColors[p].ColorIndicator = PresetColors[p];
                    _btnPresetColors[p].Click += (s, e) => ApplyPresetColor(PresetColors[pIdx]);
                }

                y += S(28) + S(12);
                _btnCustomColor = MakeButton("\uD83C\uDFA8  Custom Color...", pad, y, contentW, btnH);
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

                // Saved 4-Zone RGB Profiles
                y += btnH + S(16);
                MakeLabel("SAVED RGB PROFILES:", pad, y, FontSectionHeader, Color.FromArgb(120, 120, 135));

                y += S(20);
                int profBtnW = S(68);
                int profDropW = contentW - (profBtnW * 3 + gap * 3);
                _cboRgbProfiles = new PredatorDropDown { Location = new Point(pad, y), Size = new Size(profDropW, S(30)) };
                _cboRgbProfiles.SelectedIndexChanged += (s, e) =>
                {
                    if (_isApplyingRgbProfile) return;
                    int idx = _cboRgbProfiles.SelectedIndex;
                    var profiles = RgbProfileManager.LoadProfiles();
                    if (idx >= 0 && idx < profiles.Count)
                    {
                        ApplyRgbProfile(profiles[idx]);
                    }
                };
                _contentPanel.Controls.Add(_cboRgbProfiles);

                _btnSaveRgbProfile = MakeButton("Save", pad + profDropW + gap, y, profBtnW, S(30));
                _btnSaveRgbProfile.Click += (s, e) => SaveCurrentRgbProfile();

                _btnNewRgbProfile = MakeButton("+ New", pad + profDropW + gap + profBtnW + gap, y, profBtnW, S(30));
                _btnNewRgbProfile.Click += (s, e) => CreateNewRgbProfile();

                _btnDeleteRgbProfile = MakeButton("Delete", pad + profDropW + gap + (profBtnW + gap) * 2, y, profBtnW, S(30));
                _btnDeleteRgbProfile.Click += (s, e) => DeleteCurrentRgbProfile();

                y += S(30) + S(16);
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
                    if (_restoringLighting) return;
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
                    if (_restoringLighting) return;
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
                    if (_isGameSyncOverriding || _restoringLighting) return;
                    int mode = SelectedRgbMode;
                    ApplyRgbModeFromDropdown(mode);
                };

                UpdateRgbControlsState(SelectedRgbMode);
            }

            y += S(28) + S(12);
            _lblBacklight30s = MakeLabel("Backlight 30-Sec Idle Sleep", pad, y, FontBody, Color.FromArgb(120, 120, 135));
            CenterV(_lblBacklight30s, y, switchH);

            _switchBacklight30s = new PredatorToggle
            {
                Location = new Point(ClientSize.Width - pad - S(48), y),
                Size = new Size(S(48), switchH)
            };
            _contentPanel.Controls.Add(_switchBacklight30s);

            _switchBacklight30s.Enabled = _wmi.Keyboard.SupportsIdleTimeout;
            _switchBacklight30s.CheckedChanged += async (_, _) =>
            {
                if (_restoringLighting || _updatingKeyboard) return;
                await ChangeKeyboardFromClassicAsync(false, _switchBacklight30s.Checked);
            };

            // SYSTEM & HARDWARE CONTROLS
            y += switchH + S(22);
            AddSeparator(y);

            y += S(18);
            MakeSectionHeader("SYSTEM & HARDWARE CONTROLS", pad, y);

            // In-Game Gaming Overlay HUD
            y += S(24);
            _lblOverlay = MakeLabel("Gaming Overlay HUD (Ctrl+Shift+O)", pad, y, FontBody, Color.FromArgb(120, 120, 135));
            CenterV(_lblOverlay, y, switchH);

            _switchOverlay = new PredatorToggle
            {
                Location = new Point(ClientSize.Width - pad - S(48), y),
                Size = new Size(S(48), switchH)
            };
            _contentPanel.Controls.Add(_switchOverlay);
            _switchOverlay.CheckedChanged += (s, e) => SetOverlayVisible(_switchOverlay.Checked);

            // Overlay Display Mode (Light, Default, Full, Complete)
            y += switchH + S(12);
            MakeLabel("DISPLAY MODE:", pad, y, FontSectionHeader, Color.FromArgb(120, 120, 135));

            y += S(20);
            int modeBtnW = (contentW - gap * 3) / 4;
            _btnOverlayLight = MakeButton("Light", pad, y, modeBtnW, btnH);
            _btnOverlayDefault = MakeButton("Default", pad + modeBtnW + gap, y, modeBtnW, btnH);
            _btnOverlayFull = MakeButton("Full", pad + (modeBtnW + gap) * 2, y, modeBtnW, btnH);
            _btnOverlayComplete = MakeButton("Complete", pad + (modeBtnW + gap) * 3, y, modeBtnW, btnH);

            _btnOverlayLight.Click += (s, e) => SetOverlayMode(OverlayMode.Light);
            _btnOverlayDefault.Click += (s, e) => SetOverlayMode(OverlayMode.Default);
            _btnOverlayFull.Click += (s, e) => SetOverlayMode(OverlayMode.Full);
            _btnOverlayComplete.Click += (s, e) => SetOverlayMode(OverlayMode.Complete);

            _contentPanel.Controls.Add(_btnOverlayLight);
            _contentPanel.Controls.Add(_btnOverlayDefault);
            _contentPanel.Controls.Add(_btnOverlayFull);
            _contentPanel.Controls.Add(_btnOverlayComplete);

            y += btnH + S(8);
            _btnOverlaySettings = MakeButton("\u2699 Overlay Options", pad, y, contentW, S(28));
            _btnOverlaySettings.Click += (s, e) => OpenOverlaySettings();
            _contentPanel.Controls.Add(_btnOverlaySettings);

            y += S(28) + S(14);
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
                if (_changingHardwareToggle) return;
                bool lockKeys = _switchWinKeyLock.Checked;
                if (_trayWinKey != null) _trayWinKey.Checked = lockKeys;
                PredatorKeyHook.SetWinKeyLocked(lockKeys);
                if (_loadingPreferences) return;
                SaveState("LockWinKey", lockKeys ? 1 : 0);
                if (_wmi.Capabilities.SupportsWinKey)
                {
                    Task.Run(() =>
                    {
                        try
                        {
                            _wmi.SetWinKeyLock(lockKeys);
                            if (!IsDisposed && IsHandleCreated) BeginInvoke(() => _jelli?.PublishState());
                        }
                        catch { }
                    });
                }
                else
                {
                    _jelli?.PublishState();
                }
            };

            // Jelli Desktop Companion Toggle
            y += switchH + S(12);
            _lblJelliEnabled = MakeLabel("Jelli Companion (Desktop Creature)", pad, y, FontBody, Color.FromArgb(120, 120, 135));
            CenterV(_lblJelliEnabled, y, switchH);

            _switchJelliEnabled = new PredatorToggle
            {
                Location = new Point(ClientSize.Width - pad - S(48), y),
                Size = new Size(S(48), switchH),
                Checked = !_jelliFallback
            };
            _contentPanel.Controls.Add(_switchJelliEnabled);

            _switchJelliEnabled.CheckedChanged += (s, e) =>
            {
                if ((!_jelliFallback) != _switchJelliEnabled.Checked)
                    SetJelliEnabled(_switchJelliEnabled.Checked);
            };

            // Boot Sound (Startup Chime)
            if (_wmi.Supports(AcerFeature.BootSound))
            {
                y += switchH + S(12);
                _lblBootSound = MakeLabel("Startup Animation & Sound", pad, y, FontBody, Color.FromArgb(120, 120, 135));
                CenterV(_lblBootSound, y, switchH);

                _switchBootSound = new PredatorToggle
                {
                    Location = new Point(ClientSize.Width - pad - S(48), y),
                    Size = new Size(S(48), switchH),
                    Checked = _wmi.GetBootSound()
                };
                _contentPanel.Controls.Add(_switchBootSound);

                var tipBoot = new ToolTip();
                tipBoot.SetToolTip(_lblBootSound, "Startup Animation & Sound:\nControls the boot logo animation and sound chime on startup (POST Animation & Sound in BIOS).");
                tipBoot.SetToolTip(_switchBootSound, "Startup Animation & Sound:\nControls the boot logo animation and sound chime on startup (POST Animation & Sound in BIOS).");

                _switchBootSound.CheckedChanged += async (s, e) =>
                    await ChangeHardwareToggle(_switchBootSound, "BootSound", HardwareFeature.BootSound, value => _wmi.SetBootSound(value));
            }

            // Power-off USB Charging
            if (_wmi.Supports(AcerFeature.UsbCharging))
            {
                y += switchH + S(12);
                _lblUsbCharging = MakeLabel("Power-off USB Charging", pad, y, FontBody, Color.FromArgb(120, 120, 135));
                CenterV(_lblUsbCharging, y, switchH);

                _switchUsbCharging = new PredatorToggle
                {
                    Location = new Point(ClientSize.Width - pad - S(48), y),
                    Size = new Size(S(48), switchH),
                    Checked = _wmi.GetUsbCharging()
                };
                _contentPanel.Controls.Add(_switchUsbCharging);

                var tipUsb = new ToolTip();
                tipUsb.SetToolTip(_lblUsbCharging, "Power-off USB Charging:\nKeeps the USB port with the battery/lightning icon powered when the laptop is off or sleeping to charge external devices (phone, earbuds, etc.).");
                tipUsb.SetToolTip(_switchUsbCharging, "Power-off USB Charging:\nKeeps the USB port with the battery/lightning icon powered when the laptop is off or sleeping to charge external devices (phone, earbuds, etc.).");

                _switchUsbCharging.CheckedChanged += async (s, e) =>
                    await ChangeHardwareToggle(_switchUsbCharging, "UsbCharging", HardwareFeature.UsbCharging, value => _wmi.SetUsbCharging(value));
            }

            // Auto-CoolBoost Sync
            if (_wmi.Supports(AcerFeature.CoolBoost))
            {
                y += switchH + S(12);
                _lblAutoCoolBoost = MakeLabel("Auto-CoolBoost on Performance / Turbo", pad, y, FontBody, Color.FromArgb(120, 120, 135));
                CenterV(_lblAutoCoolBoost, y, switchH);

                _switchAutoCoolBoost = new PredatorToggle
                {
                    Location = new Point(ClientSize.Width - pad - S(48), y),
                    Size = new Size(S(48), switchH),
                    Checked = LoadState("AutoCoolBoost", 0) == 1
                };
                _contentPanel.Controls.Add(_switchAutoCoolBoost);

                _switchAutoCoolBoost.CheckedChanged += (s, e) =>
                {
                    SaveState("AutoCoolBoost", _switchAutoCoolBoost.Checked ? 1 : 0);
                };
            }

            // Hardware Key Action Customization
            y += switchH + S(16);
            MakeLabel("HARDWARE KEY ACTIONS:", pad, y, FontSectionHeader, Color.FromArgb(120, 120, 135));

            y += S(22);
            int keyDropW = (contentW - gap) / 2;
            var lblTurboKey = MakeLabel("Turbo / Mode Key:", pad, y, FontBody, Color.FromArgb(140, 140, 155));
            var lblPredatorKey = MakeLabel(_wmi.ChassisFamily == AcerChassisFamily.Nitro ? "Nitro 'N' Key:" : "Predator Key:", pad + keyDropW + gap, y, FontBody, Color.FromArgb(140, 140, 155));

            y += S(20);
            _cboTurboAction = new PredatorDropDown
            {
                Location = new Point(pad, y),
                Size = new Size(keyDropW, S(30))
            };
            _cboTurboAction.Items.Add("Cycle Power Modes");
            _cboTurboAction.Items.Add("Toggle Max Fans");
            _cboTurboAction.Items.Add("Instant Turbo Mode");
            _cboTurboAction.SelectedIndex = Math.Clamp(LoadState("TurboKeyAction", 0), 0, 2);
            _cboTurboAction.SelectedIndexChanged += (s, e) => SaveState("TurboKeyAction", _cboTurboAction.SelectedIndex);
            _contentPanel.Controls.Add(_cboTurboAction);

            _cboPredatorAction = new PredatorDropDown
            {
                Location = new Point(pad + keyDropW + gap, y),
                Size = new Size(keyDropW, S(30))
            };
            _cboPredatorAction.Items.Add("Toggle Window");
            _cboPredatorAction.Items.Add("Toggle In-Game HUD");
            _cboPredatorAction.Items.Add("Toggle Max Fans");
            _cboPredatorAction.SelectedIndex = Math.Clamp(LoadState("PredatorKeyAction", 0), 0, 2);
            _cboPredatorAction.SelectedIndexChanged += (s, e) => SaveState("PredatorKeyAction", _cboPredatorAction.SelectedIndex);
            _contentPanel.Controls.Add(_cboPredatorAction);
            y += S(30) + S(10);

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
                _lblGameSyncStatus.Text = _switchGameSync.Checked ? "Active \u2014 Monitoring" : "Disabled";
            };

            y += syncSwitchH + S(10);
            _btnConfigureGames = MakeButton("\uD83C\uDFAE  Configure Executables", pad, y, contentW, btnH);
            _btnConfigureGames.Click += (s, e) =>
            {
                using var form = new GameSyncForm(_gameSync, _maxHz, _wmi.Capabilities);
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

            _btnCheckUpdates = MakeButton("\u2B07  Check for Updates", ClientSize.Width - pad - updBtnW, y, updBtnW, updBtnH);
            _btnCheckUpdates.Click += async (s, e) => await CheckForUpdatesAsync();

            // OPTIMIZE SERVICES (F-5)
            y += updBtnH + S(24);
            AddSeparator(y);

            y += S(20);
            MakeSectionHeader("SYSTEM OPTIMIZATION", pad, y);

            y += S(24);
            int optSwitchH = S(30);
            _lblOptimizeServices = MakeLabel("Optimize Acer Services (Disable OEM Bloatware)", pad, y, FontBody, Color.FromArgb(120, 120, 135));
            CenterV(_lblOptimizeServices, y, optSwitchH);

            _switchOptimizeServices = new PredatorToggle
            {
                Location = new Point(ClientSize.Width - pad - S(48), y),
                Size = new Size(S(48), optSwitchH),
                Checked = IsServicesOptimizationEnabled()
            };
            _contentPanel.Controls.Add(_switchOptimizeServices);

            y += optSwitchH + S(2);
            _lblOptimizeCounter = MakeLabel(string.Empty, pad, y, FontBody, Color.FromArgb(120, 120, 135));
            UpdateOptimizeCounter();

            y += S(24);
            int repairBtnW = S(240), repairBtnH = S(30);
            _btnAutoRepair = MakeButton("\uD83D\uDEE0  Auto-Repair OEM Services", pad, y, repairBtnW, repairBtnH);
            _btnAutoRepair.Click += async (s, e) =>
            {
                _btnAutoRepair.Enabled = false;
                _btnAutoRepair.Text = "\u23F3  Repairing...";
                if (_lblOptimizeCounter != null)
                {
                    _lblOptimizeCounter.Text = "Scanning DriverStore and repairing OEM services...";
                    _lblOptimizeCounter.ForeColor = Color.FromArgb(255, 189, 46);
                }

                await Task.Run(() =>
                {
                    try
                    {
                        OemServiceManager.AutoRepairForChassis(_wmi.ChassisFamily, msg =>
                        {
                            try
                            {
                                if (!IsDisposed && _lblOptimizeCounter != null)
                                    BeginInvoke(() => _lblOptimizeCounter.Text = msg);
                            }
                            catch { }
                        }, forcePrompt: true);
                    }
                    catch { }
                });

                if (!IsDisposed)
                {
                    _btnAutoRepair.Enabled = true;
                    _btnAutoRepair.Text = "\uD83D\uDEE0  Auto-Repair OEM Services";
                    await Task.Run(() => _wmi.RefreshCompatibility());
                    ApplyCapabilityUi();
                    UpdateOptimizeCounter();
                }
            };

            _switchOptimizeServices.CheckedChanged += (s, e) =>
            {
                bool enabled = _switchOptimizeServices.Checked;
                SetServicesOptimizationEnabled(enabled);
                if (enabled)
                {
                    OptimizeAcerServices();
                }
                else
                {
                    RestoreAcerServices();
                }
                UpdateOptimizeCounter();
                Task.Delay(1500).ContinueWith(_ =>
                {
                    try { if (!IsDisposed) BeginInvoke(new Action(UpdateOptimizeCounter)); } catch { }
                });
            };

            LoadRgbProfilesIntoUi();

            _contentPanel.AutoScrollMinSize = new Size(0, y + repairBtnH + S(50));
            PerformCustomLayout();
            UpdateScrollMinSize();
        }

        private void RefreshExternalMonitorSection()
        {
            if (_pnlExternalMonitors == null) return;

            _externalMonitors = DisplayCcdController.GetExternalMonitors();
            bool hasExternal = _externalMonitors.Count > 0;

            if (_lblExternalDisplayHdr != null)
                _lblExternalDisplayHdr.Visible = hasExternal;

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

                var cbo = new PredatorDropDown
                {
                    Location = new Point(contentW - dropW - btnW - gap, rowY),
                    Size = new Size(dropW, rowH)
                };
                foreach (int hz in mon.SupportedHz)
                    cbo.Items.Add($"{hz} Hz");

                int curIdx = mon.SupportedHz.IndexOf(mon.CurrentHz);
                cbo.SelectedIndex = curIdx >= 0 ? curIdx : 0;
                _pnlExternalMonitors.Controls.Add(cbo);

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
                    BeginInvoke(new Action(RefreshExternalMonitorSection));
                };
                _pnlExternalMonitors.Controls.Add(applyBtn);

                totalH = rowY + rowH;
            }

            _pnlExternalMonitors.Height = totalH + rowGap;
            foreach (Control c in _pnlExternalMonitors.Controls)
            {
                if (c is Label lbl2) lbl2.ForeColor = theme.TextSecondary;
            }
        }

        private void ApplyExternalDisplayMode(string gdiName, int hz)
        {
            DisplayCcdController.SetExternalRefreshRate(gdiName, hz);
        }

        private void MakeSectionHeader(string label, int x, int y)
        {
            MakeLabel(label, x, y, FontSectionHeader, Color.FromArgb(120, 120, 135));
        }

        private Label MakeLabelIn(Control parent, string text, int x, int y, Font font, Color color)
        {
            var lbl = new Label
            {
                Text = text, Location = new Point(x, y), AutoSize = true, Font = font, ForeColor = color, BackColor = Color.Transparent,
                UseMnemonic = false
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

        private void RepositionTelemetryLabels()
        {
            if (_contentPanel == null || _lblCpuHdr == null) return;
            int pad = S(24);
            int midX = ClientSize.Width / 2;

            _lblCpuHdr.Left = pad;
            if (_lblCpuTemp != null)
                _lblCpuTemp.Left = Math.Max(_lblCpuHdr.Right + S(6), pad + S(42));

            _lblCpuFanHdr.Left = pad;
            if (_lblCpuRpm != null)
                _lblCpuRpm.Left = Math.Max(_lblCpuFanHdr.Right + S(6), pad + S(74));

            if (_lblAuxFanHdr != null)
            {
                _lblAuxFanHdr.Left = pad;
                if (_lblAuxRpm != null)
                    _lblAuxRpm.Left = Math.Max(_lblAuxFanHdr.Right + S(6), pad + S(74));
            }

            _lblFanHdr.Left = pad;
            if (_lblFanStatus != null)
                _lblFanStatus.Left = Math.Max(_lblFanHdr.Right + S(6), pad + S(80));

            if (_lblGpuHdr != null) _lblGpuHdr.Left = midX + S(10);
            if (_lblGpuTemp != null && _lblGpuHdr != null)
                _lblGpuTemp.Left = Math.Max(_lblGpuHdr.Right + S(6), midX + S(46));

            if (_lblGpuFanHdr != null) _lblGpuFanHdr.Left = midX + S(10);
            if (_lblGpuRpm != null && _lblGpuFanHdr != null)
                _lblGpuRpm.Left = Math.Max(_lblGpuFanHdr.Right + S(6), midX + S(74));

            if (_lblPowerHdr != null) _lblPowerHdr.Left = midX + S(10);
            if (_lblPowerStatus != null && _lblPowerHdr != null)
                _lblPowerStatus.Left = Math.Max(_lblPowerHdr.Right + S(6), midX + S(58));
        }

        private void PerformCustomLayout()
        {
            if (_contentPanel == null || _lblTitle == null) return;

            int pad = S(24);
            int gap = S(6);
            int contentW = Math.Max(S(280), ClientSize.Width - pad * 2);

            foreach (var sep in _separators)
            {
                sep.Left = pad;
                sep.Width = contentW;
            }

            RepositionTelemetryLabels();

            int themeBtnW = (contentW - 2 * gap) / 3;
            if (_btnThemeTeal != null) { _btnThemeTeal.Left = pad; _btnThemeTeal.Width = themeBtnW; }
            if (_btnThemeCrimson != null) { _btnThemeCrimson.Left = pad + themeBtnW + gap; _btnThemeCrimson.Width = themeBtnW; }
            if (_btnThemeOled != null) { _btnThemeOled.Left = pad + (themeBtnW + gap) * 2; _btnThemeOled.Width = contentW - (_btnThemeOled.Left - pad); }

            LayoutPowerControls(pad, contentW, gap);

            int profileDropW = (contentW - gap) / 2;
            if (_cboAcProfile != null) { _cboAcProfile.Left = pad; _cboAcProfile.Width = profileDropW; }
            if (_lblBatteryProfileHdr != null) _lblBatteryProfileHdr.Left = pad + profileDropW + gap;
            if (_cboBatteryProfile != null) { _cboBatteryProfile.Left = pad + profileDropW + gap; _cboBatteryProfile.Width = contentW - profileDropW - gap; }

            int fanBtnW = (contentW - 2 * gap) / 3;
            if (_btnAutoFan != null) { _btnAutoFan.Left = pad; _btnAutoFan.Width = fanBtnW; }
            if (_btnMaxFan != null) { _btnMaxFan.Left = pad + (fanBtnW + gap); _btnMaxFan.Width = fanBtnW; }
            if (_btnCustomFan != null) { _btnCustomFan.Left = pad + (fanBtnW + gap) * 2; _btnCustomFan.Width = contentW - (_btnCustomFan.Left - pad); }

            if (_pnlCustomFan != null)
            {
                _pnlCustomFan.Left = pad;
                _pnlCustomFan.Width = contentW;
                if (_wmi.HasAuxFan)
                {
                    int fanSliderW = (contentW - gap * 2) / 3;
                    if (_lblGpuFanSpeedHdr != null) _lblGpuFanSpeedHdr.Left = fanSliderW + gap;
                    if (_cpuFanSlider != null) _cpuFanSlider.Width = fanSliderW;
                    if (_gpuFanSlider != null) { _gpuFanSlider.Left = fanSliderW + gap; _gpuFanSlider.Width = fanSliderW; }
                    if (_lblAuxFanSpeedHdr != null) _lblAuxFanSpeedHdr.Left = (fanSliderW + gap) * 2;
                    if (_auxFanSlider != null) { _auxFanSlider.Left = (fanSliderW + gap) * 2; _auxFanSlider.Width = contentW - (_auxFanSlider.Left); }
                }
                else
                {
                    int fanSliderW = (contentW - gap) / 2;
                    if (_lblGpuFanSpeedHdr != null) _lblGpuFanSpeedHdr.Left = fanSliderW + gap;
                    if (_cpuFanSlider != null) _cpuFanSlider.Width = fanSliderW;
                    if (_gpuFanSlider != null) { _gpuFanSlider.Left = fanSliderW + gap; _gpuFanSlider.Width = contentW - fanSliderW - gap; }
                }

                int subBtnW = (contentW - gap) / 2;
                if (_btnFixedSpeed != null) { _btnFixedSpeed.Left = 0; _btnFixedSpeed.Width = subBtnW; }
                if (_btnFanCurve != null) { _btnFanCurve.Left = subBtnW + gap; _btnFanCurve.Width = contentW - subBtnW - gap; }
            }

            if (_maxHz <= 60 || _btnMaxHz == null)
            {
                if (_btn60Hz != null)
                {
                    _btn60Hz.Left = pad;
                    _btn60Hz.Width = contentW;
                }
            }
            else
            {
                int dispBtnW = (contentW - gap) / 2;
                if (_btn60Hz != null) { _btn60Hz.Left = pad; _btn60Hz.Width = dispBtnW; }
                if (_btnMaxHz != null) { _btnMaxHz.Left = pad + dispBtnW + gap; _btnMaxHz.Width = contentW - dispBtnW - gap; }
            }

            int gpuBtnW = (contentW - 2 * gap) / 3;
            if (_btnGpuOptimus != null) { _btnGpuOptimus.Left = pad; _btnGpuOptimus.Width = gpuBtnW; }
            if (_btnGpuDiscrete != null) { _btnGpuDiscrete.Left = pad + gpuBtnW + gap; _btnGpuDiscrete.Width = gpuBtnW; }
            if (_btnGpuAuto != null) { _btnGpuAuto.Left = pad + (gpuBtnW + gap) * 2; _btnGpuAuto.Width = contentW - (_btnGpuAuto.Left - pad); }

            if (_pnlExternalMonitors != null && _pnlExternalMonitors.Visible)
            {
                _pnlExternalMonitors.Left = pad;
                _pnlExternalMonitors.Width = contentW;
                int extDropW = S(110);
                int extBtnW = S(70);
                int extGap = S(6);
                foreach (Control c in _pnlExternalMonitors.Controls)
                {
                    if (c is PredatorButton pb && pb.Text == "Apply")
                    {
                        pb.Left = contentW - extBtnW;
                        pb.Width = extBtnW;
                    }
                    else if (c is PredatorDropDown pdd)
                    {
                        pdd.Left = contentW - extDropW - extBtnW - extGap;
                        pdd.Width = extDropW;
                    }
                    else if (c is Label lbl)
                    {
                        lbl.Left = 0;
                        lbl.Width = Math.Max(10, contentW - extDropW - extBtnW - extGap * 2);
                    }
                }
            }

            int switchRight = pad + contentW;
            void PlaceToggle(PredatorToggle? sw)
            {
                if (sw != null) sw.Left = switchRight - sw.Width;
            }

            PlaceToggle(_switchCoolBoost);
            PlaceToggle(_switchLcdOverdrive);
            PlaceToggle(_switchBacklight30s);
            PlaceToggle(_switchKeyboardPower);
            PlaceToggle(_switchBatteryLimit);
            PlaceToggle(_switchStartWithWindows);
            PlaceToggle(_switchWinKeyLock);
            PlaceToggle(_switchJelliEnabled);
            PlaceToggle(_switchBootSound);
            PlaceToggle(_switchUsbCharging);
            PlaceToggle(_switchAutoCoolBoost);
            PlaceToggle(_switchOptimizeServices);
            PlaceToggle(_switchOverlay);
            PlaceToggle(_switchGameSync);

            if (_lblBatteryHealth != null)
            {
                _lblBatteryHealth.Left = pad;
                _lblBatteryHealth.MaximumSize = new Size(contentW, 0);
            }

            if (_cboTurboAction != null && _cboPredatorAction != null)
            {
                int keyDropW = (contentW - gap) / 2;
                _cboTurboAction.Left = pad;
                _cboTurboAction.Width = keyDropW;
                _cboPredatorAction.Left = pad + keyDropW + gap;
                _cboPredatorAction.Width = contentW - keyDropW - gap;
            }

            int overlayModeBtnW = (contentW - 3 * gap) / 4;
            if (_btnOverlayLight != null) { _btnOverlayLight.Left = pad; _btnOverlayLight.Width = overlayModeBtnW; }
            if (_btnOverlayDefault != null) { _btnOverlayDefault.Left = pad + overlayModeBtnW + gap; _btnOverlayDefault.Width = overlayModeBtnW; }
            if (_btnOverlayFull != null) { _btnOverlayFull.Left = pad + (overlayModeBtnW + gap) * 2; _btnOverlayFull.Width = overlayModeBtnW; }
            if (_btnOverlayComplete != null) { _btnOverlayComplete.Left = pad + (overlayModeBtnW + gap) * 3; _btnOverlayComplete.Width = contentW - (_btnOverlayComplete.Left - pad); }
            if (_btnOverlaySettings != null) { _btnOverlaySettings.Left = pad; _btnOverlaySettings.Width = contentW; }

            if (_rgbDropDown != null) { _rgbDropDown.Left = pad; _rgbDropDown.Width = contentW; }

            int zoneBtnW = (contentW - _wmi.Keyboard.ZoneCount * gap) / Math.Max(1, _wmi.Keyboard.ZoneCount + 1);
            if (_btnAllZones != null) { _btnAllZones.Left = pad; _btnAllZones.Width = zoneBtnW; }
            for (int z = 0; z < 4; z++)
            {
                if (_btnZones[z] != null)
                {
                    _btnZones[z].Left = pad + (zoneBtnW + gap) * (z + 1);
                    _btnZones[z].Width = (z == 3) ? (contentW - (_btnZones[z].Left - pad)) : zoneBtnW;
                }
            }

            int preW = (contentW - 5 * gap) / 6;
            for (int i = 0; i < 6; i++)
            {
                if (_btnPresetColors[i] != null)
                {
                    _btnPresetColors[i].Left = pad + (preW + gap) * i;
                    _btnPresetColors[i].Width = (i == 5) ? (contentW - (_btnPresetColors[i].Left - pad)) : preW;
                }
            }

            if (_btnCustomColor != null) { _btnCustomColor.Left = pad; _btnCustomColor.Width = contentW; }

            int midX = ClientSize.Width / 2;
            int sliderW = _wmi.Capabilities.SupportsRgbLighting ? (contentW - gap * 4) / 2 : contentW;
            if (_brightnessSlider != null) { _brightnessSlider.Left = pad; _brightnessSlider.Width = sliderW; }
            if (_lblSpeedHdr != null) _lblSpeedHdr.Left = midX + S(10);
            if (_speedSlider != null) { _speedSlider.Left = midX + S(10); _speedSlider.Width = sliderW; }

            if (_btnAutoRepair != null) { _btnAutoRepair.Left = pad; _btnAutoRepair.Width = contentW; }

            if (_btnConfigureGames != null) { _btnConfigureGames.Left = pad; _btnConfigureGames.Width = contentW; }
            if (_btnCheckUpdates != null) _btnCheckUpdates.Left = pad + contentW - _btnCheckUpdates.Width;

            UpdateScrollMinSize();
            _contentPanel.Invalidate(true);
        }

        private void UpdateCustomFanLayout(bool isCustom)
        {
            if (_isCustomFanExpanded == isCustom || _pnlCustomFan == null) return;
            _isCustomFanExpanded = isCustom;

            int delta = isCustom ? _customFanShiftHeight : -_customFanShiftHeight;
            int threshold = _pnlCustomFan.Top;

            _contentPanel.SuspendLayout();
            try
            {
                _pnlCustomFan.Visible = isCustom;
                foreach (Control c in _contentPanel.Controls)
                {
                    if (c != _pnlCustomFan && c.Top >= threshold)
                    {
                        c.Top += delta;
                    }
                }
                _contentPanel.AutoScrollMinSize = new Size(0, _contentPanel.AutoScrollMinSize.Height + delta);
            }
            finally
            {
                _contentPanel.ResumeLayout(true);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            PerformCustomLayout();

            if (WindowState == FormWindowState.Minimized)
            {
                _telemetryService?.SetPollingState(false);
            }
            else if (WindowState == FormWindowState.Normal)
            {
                _telemetryService?.SetPollingState(true);
            }
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            if (_contentPanel != null)
            {
                _contentPanel.AutoScrollPosition = new Point(0, 0);
            }
            base.OnDpiChanged(e);
            _dpiScale = e.DeviceDpiNew / 96f;
            _contentPanel?.SetDpiScale(_dpiScale);

            if (!e.SuggestedRectangle.IsEmpty)
            {
                Bounds = e.SuggestedRectangle;
            }

            MinimumSize = new Size(S(460), S(520));
            MaximumSize = new Size(S(700), S(1200));
            if (Width < S(460)) Width = S(460);

            FitFormToCurrentScreen();
            PerformCustomLayout();
            UpdateScrollMinSize();
            _contentPanel?.Invalidate(true);
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            float currentDpiScale = DeviceDpi / 96f;
            if (Math.Abs(currentDpiScale - _dpiScale) > 0.01f)
            {
                _dpiScale = currentDpiScale;
                _contentPanel?.SetDpiScale(_dpiScale);
                MinimumSize = new Size(S(460), S(520));
                MaximumSize = new Size(S(700), S(1200));
                if (Width < S(460)) Width = S(460);
                FitFormToCurrentScreen();
                PerformCustomLayout();
                UpdateScrollMinSize();
                _contentPanel?.Invalidate(true);
            }
        }

        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            FitFormToCurrentScreen();
            PerformCustomLayout();
            UpdateScrollMinSize();
        }

        private void FitFormToCurrentScreen()
        {
            try
            {
                var screen = Screen.FromControl(this);
                if (screen != null && Height > screen.WorkingArea.Height)
                {
                    Height = Math.Max(S(500), screen.WorkingArea.Height - 40);
                    if (Top < screen.WorkingArea.Top) Top = screen.WorkingArea.Top;
                }
            }
            catch { }
        }

        private void UpdateScrollMinSize()
        {
            if (_contentPanel == null || _contentPanel.IsDisposed) return;
            int maxBottom = 0;
            int dispY = _contentPanel.DisplayRectangle.Y;
            foreach (Control c in _contentPanel.Controls)
            {
                if (c.Visible)
                {
                    int unscrolledBottom = (c.Top - dispY) + c.Height;
                    if (unscrolledBottom > maxBottom)
                        maxBottom = unscrolledBottom;
                }
            }
            if (maxBottom > 0)
            {
                _contentPanel.AutoScrollMinSize = new Size(0, maxBottom + S(40));
            }
        }

        private byte GetMappedSpeed() => (byte)Math.Clamp(Math.Round(_speedSlider.Value * 9.0 / 100.0), 1, 9);

        private void HighlightBtn(PredatorButton btn, ref PredatorButton? tracker)
        {
            if (tracker != null) tracker.IsActive = false;
            btn.IsActive = true;
            tracker = btn;
        }

        #endregion
    }
}
