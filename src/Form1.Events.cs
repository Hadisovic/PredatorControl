// ====================================================================================================
// PREDATOR CONTROL · HARDWARE ACTION HANDLERS, RGB LIGHTING, BATTERY & UPDATES
// File: src/Form1.Events.cs
// ====================================================================================================

using System.Diagnostics;
using System.Runtime.Versioning;

namespace PredatorControlApp
{
    [SupportedOSPlatform("windows")]
    public partial class Form1
    {
        #region Display Control

        private void ApplyDisplayMode(int hz, PredatorButton? btn)
        {
            if (!DisplayCcdController.SetRefreshRate(hz, _internalDisplayGdiName)) return;

            if (btn == null) btn = _btn60Hz;
            HighlightBtn(btn, ref _activeDisplayBtn);
            var targetTray = (hz <= 60 || _trayDisplayMax == null) ? _trayDisplay60 : _trayDisplayMax;
            CheckTrayItem(targetTray, _trayDisplay60, _trayDisplayMax);
            SaveState("DisplayHz", hz);
        }

        #endregion

        #region GPU Working Mode (MUX Switch)

        private async void ApplyGpuMode(int mode, PredatorButton btn, string modeName)
        {
            if (!_wmi.Capabilities.SupportsGpuMux || mode is < 0 or > 2 || (_wmi.Capabilities.GpuModeMask & (1 << mode)) == 0) return;
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
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = SecureLaunch.System32("shutdown.exe"),
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

        #region Power, Fan & Hotkey Action Handlers (Zero-Lag Optimistic UI)

        public void CyclePowerMode(bool showOsd = true)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => CyclePowerMode(showOsd)));
                return;
            }

            bool isPlugged = _isPluggedIn.GetValueOrDefault(SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online);
            byte currentMode = GetCurrentPowerByte();

            var modes = _wmi.Capabilities.PowerModes.Where(m => _wmi.Capabilities.AllowsPower(m.Value, isPlugged)).ToArray();
            if (modes.Length == 0) return;
            int current = Array.FindIndex(modes, m => m.Value == currentMode);
            var next = modes[(current + 1) % modes.Length];
            ApplyPowerMode(next.Value, PowerByteToBtn(next.Value), showOsd);
        }

        private void CheckUntestedChassisWarning() { }

        private void ApplyPowerMode(byte mode, PredatorButton btn, bool showOsd = false, bool persistPreference = true)
        {
            bool isPlugged = _isPluggedIn.GetValueOrDefault(SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online);
            var modeEntry = _wmi.Capabilities.PowerModes.FirstOrDefault(m => m.Value == mode);
            if (modeEntry == null || !_wmi.Capabilities.AllowsPower(mode, isPlugged))
            {
                _compatibilityTip.SetToolTip(btn, "Power mode is not available on the current power source.");
                _jelli?.PublishState();
                return;
            }

            var prevBtn = _activePowerBtn;
            string prevLabel = _lblPowerStatus.Text;

            // Optimistic zero-lag UI update on the UI thread (<1ms)
            HighlightBtn(btn, ref _activePowerBtn);
            _lblPowerStatus.Text = modeEntry.Label;

            var powerControls = PowerControls().ToArray();
            if (powerControls.FirstOrDefault(c => c.Mode == mode).Tray is ToolStripMenuItem activeTray)
                CheckTrayItem(activeTray, powerControls.Select(c => c.Tray).ToArray());

            if (showOsd)
            {
                OSDOverlayForm.ShowMode(
                    mode,
                    !isPlugged,
                    UsesWindowsPowerPlans || _wmi.Capabilities.ChassisFamily == AcerChassisFamily.Nitro ? _lblPowerStatus.Text : null);
            }

            if (persistPreference) SaveState("Power", mode);
            _jelli?.PublishState();

            int seq = Interlocked.Increment(ref _powerRequestSeq);
            bool autoCoolBoost = IsAutoCoolBoostEnabled() && _wmi.Capabilities.SupportsCoolBoost && GetCurrentFanByte() == 0x01;

            _ = Task.Run(() =>
            {
                var operation = _wmi.SetPowerMode(mode);
                bool ok = operation.Accepted && (!_wmi.Capabilities.Results[HardwareFeature.Power].CanVerify || operation.Confirmed);
                if (!ok)
                {
                    SafeInvoke(() =>
                    {
                        if (seq != Volatile.Read(ref _powerRequestSeq)) return;
                        if (prevBtn != null) HighlightBtn(prevBtn, ref _activePowerBtn);
                        _lblPowerStatus.Text = prevLabel;
                        _compatibilityTip.SetToolTip(btn, operation.Message);
                        _jelli?.PublishState();
                    });
                    return;
                }

                if (autoCoolBoost && seq == Volatile.Read(ref _powerRequestSeq))
                {
                    SafeInvoke(() =>
                    {
                        if (mode is 0x04 or 0x05)
                        {
                            if (_switchCoolBoost != null && !_switchCoolBoost.Checked) _switchCoolBoost.Checked = true;
                            else if (_switchCoolBoost == null) Task.Run(() => _wmi.SetCoolBoost(true));
                        }
                        else if (mode is 0x00 or 0x06)
                        {
                            if (_switchCoolBoost != null && _switchCoolBoost.Checked) _switchCoolBoost.Checked = false;
                            else if (_switchCoolBoost == null) Task.Run(() => _wmi.SetCoolBoost(false));
                        }
                    });
                }

                SafeInvoke(() => _jelli?.PublishState());
            });
        }

        private bool IsAutoCoolBoostEnabled() => LoadState("AutoCoolBoost", 0) == 1;

        private void HandleTurboOrModeKey()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(HandleTurboOrModeKey));
                return;
            }
            int action = LoadState("TurboKeyAction", 0);
            switch (action)
            {
                case 1:
                    ToggleMaxFans();
                    break;
                case 2:
                    bool ac = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
                    var fastest = _wmi.Capabilities.PowerModes.LastOrDefault(m => ac ? m.OnAc : m.OnBattery);
                    if (fastest != null) ApplyPowerMode(fastest.Value, PowerByteToBtn(fastest.Value), true);
                    break;
                default:
                    CyclePowerMode(true);
                    break;
            }
        }

        private void HandlePredatorOrNitroKey()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(HandlePredatorOrNitroKey));
                return;
            }
            int action = LoadState("PredatorKeyAction", 0);
            switch (action)
            {
                case 1:
                    ToggleGameOverlay();
                    break;
                case 2:
                    ToggleMaxFans();
                    break;
                default:
                    ToggleApp();
                    break;
            }
        }

        private void ToggleMaxFans()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(ToggleMaxFans));
                return;
            }
            if (_activeFanBtn == _btnMaxFan)
            {
                ApplyFanMode(0x01, _btnAutoFan);
            }
            else
            {
                ApplyFanMode(0x02, _btnMaxFan);
            }
        }

        private void ToggleCoolBoost()
        {
            if (_switchCoolBoost != null)
            {
                _switchCoolBoost.Checked = !_switchCoolBoost.Checked;
            }
        }

        private void ApplyFanMode(byte mode, PredatorButton btn)
        {
            HardwareFeature feature = mode == 1 ? HardwareFeature.FanAuto : mode == 2 ? HardwareFeature.FanMax : HardwareFeature.FanManual;
            if (!_wmi.Capabilities.CanWrite(feature))
            {
                _lblFanStatus.Text = "Fan control is not available on this model.";
                return;
            }

            var prevBtn = _activeFanBtn;
            string prevLabel = _lblFanStatus.Text;
            bool prevCustom = _isCustomFanExpanded;

            // Optimistic zero-lag UI update on the UI thread (<1ms)
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
            UpdateCustomFanLayout(isCustom);

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

            _jelli?.PublishState();

            int seq = Interlocked.Increment(ref _fanRequestSeq);
            _ = Task.Run(() =>
            {
                var operation = _wmi.SetFanBehavior(mode);
                if (!operation.Accepted)
                {
                    SafeInvoke(() =>
                    {
                        if (seq != Volatile.Read(ref _fanRequestSeq)) return;
                        if (prevBtn != null) HighlightBtn(prevBtn, ref _activeFanBtn);
                        UpdateCustomFanLayout(prevCustom);
                        _lblFanStatus.Text = operation.Message;
                        _jelli?.PublishState();
                    });
                    return;
                }
                SafeInvoke(() => _jelli?.PublishState());
            });
        }

        private void OpenFanCurveEditor()
        {
            if (!_wmi.Capabilities.CanWrite(HardwareFeature.FanCurve)) return;
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

                args.Success = _wmi.SetFanSpeed((byte)cpuSpeed, (byte)gpuSpeed);

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

        #endregion

        #region Battery Limit & Diagnostics

        private void ApplyBatteryLimit(bool limit)
        {
            if (_isUpdatingBattery || _loadingPreferences || !_wmi.Capabilities.SupportsBatteryControl) return;
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
                    _switchBatteryLimit.Checked = _wmi.Capabilities.BatteryLimitEnabled ?? false;
                    _lblBatteryStatus.Text = _wmi.LastOperation?.Message ?? "Charge-limit state N/A";
                }
            }
            finally
            {
                _isUpdatingBattery = false;
            }
        }

        private void UpdateBatteryHealthUI(bool forceRefresh = false)
        {
            if (_lblBatteryHealth == null || _lblBatteryHealth.IsDisposed) return;

            var cached = BatteryHealthMonitor.CachedInfo;
            if (cached != null && !forceRefresh)
            {
                ApplyBatteryHealthToLabel(cached);
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    var info = await BatteryHealthMonitor.GetBatteryHealthAsync(forceRefresh);
                    SafeInvoke(() =>
                    {
                        if (info != null)
                        {
                            ApplyBatteryHealthToLabel(info);
                        }
                        else if (_lblBatteryHealth != null && !_lblBatteryHealth.IsDisposed)
                        {
                            _lblBatteryHealth.Text = "Battery diagnostics: AC power only / report unavailable";
                            _lblBatteryHealth.ForeColor = Color.FromArgb(120, 120, 135);
                        }
                    });
                }
                catch (Exception ex)
                {
                    Program.Report(ex, false);
                }
            });
        }

        private void ApplyBatteryHealthToLabel(BatteryHealthInfo info)
        {
            if (_lblBatteryHealth == null || _lblBatteryHealth.IsDisposed) return;
            string cyclesText = info.CycleCount > 0 ? $" • {info.CycleCount} Cycles" : "";
            double healthPct = Math.Max(0.0, Math.Round(100.0 - info.WearLevelPercent, 1));
            _lblBatteryHealth.Text = $"Health: {healthPct:0.#}% (Wear: {info.WearLevelPercent:0.#}%) • {info.FullChargeCapacityMWh:N0} / {info.DesignCapacityMWh:N0} mWh{cyclesText}";
            _lblBatteryHealth.ForeColor = healthPct >= 80 ? Color.FromArgb(0, 204, 150) : (healthPct >= 60 ? Color.FromArgb(255, 189, 46) : Color.FromArgb(255, 85, 85));
        }

        #endregion

        #region Keyboard RGB & Saved Profiles

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
            if (!_wmi.Keyboard.SupportsRgb || _restoringLighting) return;
            _colorPicker.Color = c;

            // Only switch to Static if currently in a mode that doesn't support custom colors (Neon, Wave, Off)
            bool wasUnsupported = (SelectedRgbMode == 2 || SelectedRgbMode == 3 || SelectedRgbMode == 8);
            int mode = wasUnsupported ? 0 : SelectedRgbMode;

            if (mode == 0) // Static: individual physical zones supported
            {
                if (_selectedZone == -1)
                {
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
                    _currentZoneColors[_selectedZone] = c;
                    if (_btnZones[_selectedZone] != null)
                        _btnZones[_selectedZone].ColorIndicator = c;

                    SaveState($"ZoneColor_{_selectedZone}", c.ToArgb());
                    _wmi.SetSingleZoneColor(_selectedZone, c, 0);
                }
            }
            else // Animated modes (Breathing, Shifting, Zoom, Meteor, Twinkling)
            {
                byte bright = (byte)_brightnessSlider.Value;
                byte speed = GetMappedSpeed();
                _wmi.SetRgbMode(mode, c.R, c.G, c.B, bright, speed, (byte)_wmi.Direction);
            }

            if (wasUnsupported && SelectedRgbMode != 0)
            {
                SelectRgbMode(0);
            }

            if (_btnCustomColor != null)
                _btnCustomColor.ColorIndicator = c;

            SaveState("RGB_R", c.R);
            SaveState("RGB_G", c.G);
            SaveState("RGB_B", c.B);
        }

        private void UpdateRgbControlsState(int mode)
        {
            bool colorsEnabled = (mode != 2 && mode != 3 && mode != 8);
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
                _selectedZone = -1;
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

        private void ApplyRgbModeFromDropdown(int mode)
        {
            if (!_wmi.Keyboard.SupportsRgb || _restoringLighting || !_rgbModeValues.Contains(mode)) return;
            CheckUntestedChassisWarning();
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

            if (SelectedRgbMode != mode)
                SelectRgbMode(mode);

            SaveState("RGB_Mode", mode);
            CheckRgbTrayFromMode(mode);
        }

        internal void LoadRgbProfilesIntoUi()
        {
            if (_cboRgbProfiles == null) return;
            var profiles = RgbProfileManager.LoadProfiles();
            string active = RgbProfileManager.GetActiveProfile();
            bool prevApplying = _isApplyingRgbProfile;
            _isApplyingRgbProfile = true;
            try
            {
                _cboRgbProfiles.Items.Clear();
                int selIdx = -1;
                for (int i = 0; i < profiles.Count; i++)
                {
                    _cboRgbProfiles.Items.Add(profiles[i].Name);
                    if (string.Equals(profiles[i].Name, active, StringComparison.OrdinalIgnoreCase))
                        selIdx = i;
                }
                if (selIdx < 0 && profiles.Count > 0) selIdx = 0;
                _cboRgbProfiles.SelectedIndex = selIdx;
                if (_btnDeleteRgbProfile != null)
                {
                    _btnDeleteRgbProfile.Enabled = profiles.Count > 1;
                }
            }
            finally
            {
                _isApplyingRgbProfile = prevApplying;
            }
        }

        internal void ApplyRgbProfile(RgbProfile profile)
        {
            if (!_wmi.Keyboard.SupportsRgb || _restoringLighting) return;
            if (profile == null) return;
            _isApplyingRgbProfile = true;
            try
            {
                var colors = RgbProfileManager.ParseZoneColors(profile.Zones);
                for (int i = 0; i < 4; i++)
                {
                    _currentZoneColors[i] = colors[i];
                    if (_btnZones[i] != null)
                        _btnZones[i].ColorIndicator = colors[i];
                    SaveState($"ZoneColor_{i}", colors[i].ToArgb());
                }
                if (_btnAllZones != null)
                    _btnAllZones.ColorIndicator = colors[0];

                if (_selectedZone >= 0 && _selectedZone < 4)
                {
                    if (_colorPicker != null) _colorPicker.Color = _currentZoneColors[_selectedZone];
                    if (_btnCustomColor != null) _btnCustomColor.ColorIndicator = _currentZoneColors[_selectedZone];
                }
                else
                {
                    if (_colorPicker != null) _colorPicker.Color = _currentZoneColors[0];
                    if (_btnCustomColor != null) _btnCustomColor.ColorIndicator = _currentZoneColors[0];
                }

                if (_rgbDropDown != null && SelectedRgbMode != 0)
                {
                    SelectRgbMode(0);
                }

                byte bright = (byte)(_brightnessSlider?.Value ?? 100);
                byte speed = _speedSlider != null ? GetMappedSpeed() : (byte)5;
                _wmi?.Set4ZoneColors(_currentZoneColors, 0, bright, speed);
                RgbProfileManager.SetActiveProfile(profile.Name);
            }
            finally
            {
                _isApplyingRgbProfile = false;
            }
        }

        internal void SaveCurrentRgbProfile()
        {
            string currentName = "";
            if (_cboRgbProfiles != null && _cboRgbProfiles.SelectedIndex >= 0 && _cboRgbProfiles.SelectedIndex < _cboRgbProfiles.Items.Count)
            {
                currentName = _cboRgbProfiles.Items[_cboRgbProfiles.SelectedIndex]?.ToString() ?? "";
            }
            if (string.IsNullOrWhiteSpace(currentName)) currentName = RgbProfileManager.GetActiveProfile();
            if (string.IsNullOrWhiteSpace(currentName)) currentName = "Custom Profile";
            RgbProfileManager.AddOrUpdateProfile(currentName, _currentZoneColors, _brightnessSlider?.Value ?? 100);
            RgbProfileManager.SetActiveProfile(currentName);
            LoadRgbProfilesIntoUi();

            if (_btnSaveRgbProfile != null)
            {
                _btnSaveRgbProfile.Text = "Saved \u2713";
                _profileSaveFeedbackTimer?.Stop();
                _profileSaveFeedbackTimer?.Dispose();
                _profileSaveFeedbackTimer = new System.Windows.Forms.Timer { Interval = 1500 };
                _profileSaveFeedbackTimer.Tick += (s, e) =>
                {
                    _profileSaveFeedbackTimer.Stop();
                    if (_btnSaveRgbProfile != null && !IsDisposed)
                        _btnSaveRgbProfile.Text = "Save";
                };
                _profileSaveFeedbackTimer.Start();
            }
        }

        private void DeleteCurrentRgbProfile()
        {
            var profiles = RgbProfileManager.LoadProfiles();
            if (profiles.Count <= 1)
            {
                MessageBox.Show(this, "At least one profile must be kept.", "Delete Profile", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int idx = _cboRgbProfiles.SelectedIndex;
            if (idx >= 0 && idx < profiles.Count)
            {
                string targetName = profiles[idx].Name;
                var res = MessageBox.Show(this, $"Are you sure you want to delete the RGB profile '{targetName}'?", "Delete RGB Profile", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (res == DialogResult.Yes)
                {
                    RgbProfileManager.DeleteProfile(targetName);
                    var remaining = RgbProfileManager.LoadProfiles();
                    LoadRgbProfilesIntoUi();
                    if (remaining.Count > 0)
                    {
                        ApplyRgbProfile(remaining[0]);
                    }
                    _jelli?.PublishState();
                }
            }
        }

        private void CreateNewRgbProfile()
        {
            var profiles = RgbProfileManager.LoadProfiles();
            string newName = $"Custom {profiles.Count + 1}";
            string? promptResult = ShowInputBox("New RGB Profile", "Enter profile name:", newName);
            if (!string.IsNullOrWhiteSpace(promptResult))
            {
                string cleanName = promptResult.Trim();
                RgbProfileManager.AddOrUpdateProfile(cleanName, _currentZoneColors, _brightnessSlider?.Value ?? 100);
                RgbProfileManager.SetActiveProfile(cleanName);
                LoadRgbProfilesIntoUi();
                int idx = _cboRgbProfiles.Items.IndexOf(cleanName);
                if (idx >= 0) _cboRgbProfiles.SelectedIndex = idx;
            }
        }

        private string? ShowInputBox(string title, string prompt, string defaultValue)
        {
            using var promptForm = new Form
            {
                Width = S(320),
                Height = S(170),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                Text = title,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = ThemeManager.Current.FormBg,
                ForeColor = ThemeManager.Current.TextPrimary,
                Font = FontBody
            };

            var lblPrompt = new Label
            {
                Left = S(20),
                Top = S(15),
                Width = S(280),
                Height = S(20),
                Text = prompt,
                ForeColor = ThemeManager.Current.TextSecondary
            };
            var txtInput = new TextBox
            {
                Left = S(20),
                Top = S(40),
                Width = S(265),
                Height = S(28),
                Text = defaultValue,
                BackColor = ThemeManager.Current.CardBg,
                ForeColor = ThemeManager.Current.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle
            };
            var btnOk = MakeButtonIn(promptForm, "Create", S(95), S(80), S(90), S(30));
            var btnCancel = MakeButtonIn(promptForm, "Cancel", S(195), S(80), S(90), S(30));

            btnOk.Click += (s, e) => { promptForm.DialogResult = DialogResult.OK; promptForm.Close(); };
            btnCancel.Click += (s, e) => { promptForm.DialogResult = DialogResult.Cancel; promptForm.Close(); };
            txtInput.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { promptForm.DialogResult = DialogResult.OK; promptForm.Close(); }
                else if (e.KeyCode == Keys.Escape) { promptForm.DialogResult = DialogResult.Cancel; promptForm.Close(); }
            };

            promptForm.Controls.Add(lblPrompt);
            promptForm.Controls.Add(txtInput);
            promptForm.Controls.Add(btnOk);
            promptForm.Controls.Add(btnCancel);

            return promptForm.ShowDialog(this) == DialogResult.OK ? txtInput.Text : null;
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
                $"Version {info.Version.ToString(3)} is available \u2014 you have v{Updater.CurrentText}",
                info.Notes, confirm: true);

            if (!accepted) return;

            try
            {
                _btnCheckUpdates.Enabled = false;
                _btnCheckUpdates.Text = "Downloading update\u2026";
                await Updater.ApplyAsync(info);

                _isClosing = true;
                Application.Exit();
            }
            catch (Exception ex)
            {
                _btnCheckUpdates.Enabled = true;
                _btnCheckUpdates.Text = $"\u2B07  Check for Updates  (v{Updater.CurrentText})";
                MessageBox.Show(this, $"Update failed:\n{ex.Message}",
                    "Predator Control", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        #endregion
    }
}
