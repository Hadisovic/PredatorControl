using Microsoft.Win32;

namespace PredatorControlApp;

public partial class Form1
{
    private int[] _rgbModeValues = [0];
    private int SelectedRgbMode => _rgbDropDown.SelectedIndex >= 0 && _rgbDropDown.SelectedIndex < _rgbModeValues.Length ? _rgbModeValues[_rgbDropDown.SelectedIndex] : -1;
    private void SelectRgbMode(int mode) => _rgbDropDown.SelectedIndex = Array.IndexOf(_rgbModeValues, mode);
    private bool _loadingPreferences = true;
    private bool _changingHardwareToggle;
    private bool _curveAwaitingSensors;
    private long _lastTelemetryTick = Environment.TickCount64;
    private readonly System.Windows.Forms.Timer _fanSafetyTimer = new() { Interval = 3000 };
    private readonly ToolTip _compatibilityTip = new();
    private byte[] _acModeValues = [255];
    private byte[] _batteryModeValues = [255];
    private bool UsesWindowsPowerPlans => _wmi.Capabilities.Results.GetValueOrDefault(HardwareFeature.Power)?.Backend == "windows-power-plan";
    private Label? _lblPowerPlanNotice;
    private PredatorButton? _btnRefreshCompatibility;
    private bool _refreshingCompatibilityUi;
    private Control[] _coolBoostRows = [];
    private ToolStripMenuItem? _trayRgbMenu, _trayGpuMenu;
    private long _lastDisplayCompatibilityRefresh;

    private void RefreshCompatibilityAfterDisplayChange()
    {
        long now = Environment.TickCount64;
        if (now - _lastDisplayCompatibilityRefresh < 10000) return;
        _lastDisplayCompatibilityRefresh = now;
        _wmi.RequestCompatibilityRefresh();
    }

    private void RefreshCoolingCapabilityUi(HardwareCapabilities capabilities)
    {
        if (_pnlCustomFan == null || capabilities.SupportsCoolBoost == (_coolBoostRows.Length > 0)) return;
        var page = _tabPanels[1];
        int start = _pnlCustomFan.Top + (_isCustomFanExpanded ? _customFanShiftHeight : 0);
        int height = S(84);
        page.SuspendLayout();
        try
        {
            if (capabilities.SupportsCoolBoost)
            {
                foreach (Control control in page.Controls)
                    if (control != _pnlCustomFan && control.Top >= start) control.Top += height;
                var previous = _buildTarget;
                _buildTarget = page;
                try { BuildCoolBoostControls(ref start, capabilities); }
                finally { _buildTarget = previous; }
            }
            else
            {
                foreach (var control in _coolBoostRows) control.Dispose();
                _coolBoostRows = [];
                _switchCoolBoost = _switchAutoCoolBoost = null;
                _lblCoolBoost = _lblAutoCoolBoost = null;
                foreach (Control control in page.Controls)
                    if (control != _pnlCustomFan && control.Top >= start + height) control.Top -= height;
            }
        }
        finally { page.ResumeLayout(true); }
    }

    private void ApplyFanSliderRanges(HardwareCapabilities capabilities)
    {
        int cpuMinimum = 10, cpuMaximum = 100, gpuMinimum = 10, gpuMaximum = 100;
        bool reviewedService = capabilities.Results.GetValueOrDefault(HardwareFeature.FanManual)?.Backend == VerifiedAcerProtocol.FanBackend;
        if (reviewedService && VerifiedAcerProtocol.TryGetCpuGpuFans(capabilities.FanChannels, out var cpu, out var gpu) &&
            VerifiedAcerProtocol.TryGetCustomSliderRange(cpu, out cpuMinimum, out cpuMaximum) &&
            VerifiedAcerProtocol.TryGetCustomSliderRange(gpu, out gpuMinimum, out gpuMaximum))
        {
            // Use only bounds from the selected service protocol's live capability reply.
        }
        else
        {
            cpuMinimum = gpuMinimum = 10;
            cpuMaximum = gpuMaximum = 100;
        }

        void Apply(PredatorSlider? slider, Label? label, int minimum, int maximum, string fan)
        {
            if (slider == null || label == null) return;
            slider.Maximum = maximum;
            slider.Minimum = minimum;
            slider.Value = Math.Clamp(slider.Value, minimum, maximum);
            label.Text = $"{fan} FAN: {slider.Value}%";
        }

        Apply(_cpuFanSlider, _lblCpuFanSpeedHdr, cpuMinimum, cpuMaximum, "CPU");
        Apply(_gpuFanSlider, _lblGpuFanSpeedHdr, gpuMinimum, gpuMaximum, "GPU");
        if (_fanCurveForm != null && !_fanCurveForm.IsDisposed)
        {
            _fanCurveForm.SetMinSpeed(cpuMinimum, gpuMinimum);
        }
    }

    private async Task RefreshCompatibilityUiAsync()
    {
        if (_refreshingCompatibilityUi || _isClosing) return;
        _refreshingCompatibilityUi = true;
        if (_btnRefreshCompatibility != null) _btnRefreshCompatibility.Enabled = false;
        try
        {
            await _wmi.RefreshCompatibilityAsync();
            if (!IsDisposed) OnCompatibilityChanged(this, EventArgs.Empty);
        }
        catch (Exception ex) { if (!IsDisposed) Program.Report(ex, false); }
        finally
        {
            _refreshingCompatibilityUi = false;
            if (!IsDisposed && _btnRefreshCompatibility != null) _btnRefreshCompatibility.Enabled = true;
        }
    }

    private void OnCompatibilityChanged(object? sender, EventArgs e)
    {
        if (_isClosing || IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired) { SafeInvoke(() => OnCompatibilityChanged(sender, e)); return; }
        ApplyCapabilityUi();
        PerformCustomLayout();
        _jelli?.PublishState();
    }

    private void StartFanSafetyWatchdog()
    {
        _fanSafetyTimer.Tick += (_, _) =>
        {
            if (GetCurrentFanByte() != 3 || Environment.TickCount64 - _lastTelemetryTick <= 5000) return;
            if (_fanCurveEnabled)
            {
                _curveAwaitingSensors = true;
                _cpuTempHistory.Clear(); _gpuTempHistory.Clear();
            }
            _lblFanStatus.Text = "Telemetry stale · requesting firmware Auto";
            if (Interlocked.CompareExchange(ref _fanCurveWriteInFlight, 1, 0) != 0) return;
            _ = Task.Run(() =>
            {
                try
                {
                    _wmi.RecoverAutomaticFans();
                    if (_wmi.LastOperation?.Accepted == true)
                        SafeInvoke(() => HighlightBtn(_btnAutoFan, ref _activeFanBtn));
                }
                finally { Interlocked.Exchange(ref _fanCurveWriteInFlight, 0); }
            });
        };
        _fanSafetyTimer.Start();
        FormClosed += (_, _) => { _fanSafetyTimer.Dispose(); _compatibilityTip.Dispose(); };
    }

    private IEnumerable<(byte Mode, PredatorButton Button, ToolStripMenuItem? Tray)> PowerControls() =>
        UsesWindowsPowerPlans
        ? [((byte)0, _btnQuiet, _trayPowerQuiet), ((byte)1, _btnBalanced, _trayPowerBal), ((byte)2, _btnEco, _trayPowerEco),
            ((byte)4, _btnPerform, _trayPowerPerf), ((byte)5, _btnTurbo, _trayPowerTurbo)]
        : [((byte)0, _btnQuiet, _trayPowerQuiet), ((byte)1, _btnBalanced, _trayPowerBal), ((byte)4, _btnPerform, _trayPowerPerf),
            ((byte)5, _btnTurbo, _trayPowerTurbo), ((byte)6, _btnEco, _trayPowerEco)];

    private void LayoutPowerControls(int pad, int width, int gap)
    {
        if (_lblPowerPlanNotice != null) _lblPowerPlanNotice.Width = width;
        if (_btnQuiet == null || _btnBalanced == null || _btnPerform == null || _btnTurbo == null || _btnEco == null) return;
        var available = PowerControls().Where(c => _wmi.Capabilities.PowerModes.Any(m => m.Value == c.Mode)).ToArray();
        foreach (var c in PowerControls())
        {
            bool supported = available.Any(a => a.Mode == c.Mode);
            c.Button.Visible = supported;
            if (c.Tray != null) c.Tray.Visible = supported;
        }
        int count = Math.Max(1, available.Length), w = (width - gap * (count - 1)) / count;
        for (int i = 0; i < available.Length; i++)
        {
            var c = available[i];
            c.Button.Left = pad + (w + gap) * i;
            c.Button.Width = (i == available.Length - 1) ? (width - (c.Button.Left - pad)) : w;
            c.Button.Text = _wmi.Capabilities.PowerModes.First(m => m.Value == c.Mode).Label;
            if (c.Tray != null) c.Tray.Text = c.Button.Text;
        }
    }

    private void ApplyCapabilityUi()
    {
        var caps = _wmi.Capabilities;
        RefreshKeyboardCapabilityUi(caps);
        RefreshCoolingCapabilityUi(caps);
        PopulateAutoModes();
        var keyboardUi = KeyboardControlProjection.From(caps);
        if (_trayRgbMenu != null) _trayRgbMenu.Visible = keyboardUi.Rgb;
        if (_trayGpuMenu != null) _trayGpuMenu.Visible = caps.SupportsGpuMux;
        var rgbTray = new[] { _trayRgbStatic, _trayRgbBreathe, _trayRgbNeon, _trayRgbWave, _trayRgbShift,
            _trayRgbZoom, _trayRgbMeteor, _trayRgbTwinkle, _trayRgbOff };
        for (int effect = 0; effect < rgbTray.Length; effect++)
            if (rgbTray[effect] != null) rgbTray[effect].Visible = keyboardUi.AllowsEffect(effect);
        if (_trayCoolBoost != null) _trayCoolBoost.Visible = _trayCoolBoost.Enabled = caps.SupportsCoolBoost;
        void Set(Control? c, HardwareFeature f)
        {
            if (c == null) return;
            c.Enabled = caps.CanWrite(f);
            _compatibilityTip.SetToolTip(c, caps.Results.GetValueOrDefault(f)?.Evidence ?? caps.Explanation);
        }
        Set(_btnAutoFan, HardwareFeature.FanAuto); Set(_btnMaxFan, HardwareFeature.FanMax);
        Set(_btnCustomFan, HardwareFeature.FanManual); Set(_btnFixedSpeed, HardwareFeature.FanManual);
        Set(_btnFanCurve, HardwareFeature.FanCurve); Set(_cpuFanSlider, HardwareFeature.FanManual);
        Set(_gpuFanSlider, HardwareFeature.FanManual); Set(_auxFanSlider, HardwareFeature.AuxFan);
        ApplyFanSliderRanges(caps);
        if (_fanCurveEnabled)
        {
            _cpuFanSlider.Enabled = _gpuFanSlider.Enabled = false;
            if (_auxFanSlider != null) _auxFanSlider.Enabled = false;
        }
        Set(_switchBatteryLimit, HardwareFeature.BatteryLimit);
        Set(_switchCoolBoost, HardwareFeature.CoolBoost); Set(_switchLcdOverdrive, HardwareFeature.LcdOverdrive);
        Set(_switchBootSound, HardwareFeature.BootSound); Set(_switchUsbCharging, HardwareFeature.UsbCharging);
        _gpuCapability = caps.GpuModeMask;
        foreach (var (button, tray, bit) in new[] { (_btnGpuOptimus, _trayGpuOptimus, 1), (_btnGpuDiscrete, _trayGpuDiscrete, 2), (_btnGpuAuto, _trayGpuAuto, 4) })
        {
            bool enabled = caps.SupportsGpuMux && (caps.GpuModeMask & bit) != 0;
            if (button != null) button.Enabled = enabled;
            if (tray != null) tray.Enabled = enabled;
        }
        if (_trayFanAuto != null) _trayFanAuto.Enabled = caps.CanWrite(HardwareFeature.FanAuto);
        if (_trayFanMax != null) _trayFanMax.Enabled = caps.CanWrite(HardwareFeature.FanMax);
        if (_trayFanCustom != null) _trayFanCustom.Enabled = caps.CanWrite(HardwareFeature.FanManual);
        if (_trayBatteryMenu != null) _trayBatteryMenu.Enabled = caps.SupportsBatteryControl;
        if (_lblPowerStatus != null && caps.PowerModeCount == 0) _lblPowerStatus.Text = "N/A · see compatibility report";
        if (_lblPowerPlanNotice != null) _lblPowerPlanNotice.Text = caps.PowerPlanNotice ?? "";
        if (_lblFanStatus != null && !caps.CanWrite(HardwareFeature.FanAuto)) _lblFanStatus.Text = "N/A · firmware controls cooling";
        _cboAcProfile.Enabled = _cboBatteryProfile.Enabled = caps.PowerModeCount > 0;
        var current = caps.ActivePowerMode is byte activeMode
            ? caps.PowerModes.FirstOrDefault(m => m.Value == activeMode)
            : UsesWindowsPowerPlans && caps.ActiveWindowsPowerPlan.HasValue
                ? caps.PowerModes.FirstOrDefault(m => m.WindowsScheme == caps.ActiveWindowsPowerPlan)
                : null;
        if (current != null && _lblPowerStatus != null)
        {
            _lblPowerStatus.Text = current.Label;
            HighlightBtn(PowerByteToBtn(current.Value), ref _activePowerBtn);
            var controls = PowerControls().ToArray();
            if (controls.FirstOrDefault(c => c.Mode == current.Value).Tray is ToolStripMenuItem activeTray)
                CheckTrayItem(activeTray, controls.Select(c => c.Tray).ToArray());
        }
        else if (UsesWindowsPowerPlans && caps.ActiveWindowsPowerPlan.HasValue && _lblPowerStatus != null)
        {
            _lblPowerStatus.Text = "Other Windows power plan";
        }
        ApplyPowerRules(SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online, false);
    }

    private async Task<HardwareOperationResult> ChangeHardwareToggle(PredatorToggle toggle, string preference, HardwareFeature feature, Func<bool, bool> write)
    {
        if (_loadingPreferences || _changingHardwareToggle) return new("failed", "Control is busy restoring preferences.");
        bool wanted = toggle.Checked;
        toggle.Enabled = false;
        var result = await Task.Run(() =>
        {
            lock (HardwareWriteGate.CommandLock)
            {
                if (!_wmi.Capabilities.CanWrite(feature)) return new HardwareOperationResult("failed", "Feature is unavailable.");
                bool ok = write(wanted);
                return _wmi.LastOperation ?? new(ok ? "unverified" : "failed", "Hardware state could not be verified.");
            }
        });
        var current = _wmi.Capabilities;
        bool accepted = result.Accepted && current.CanWrite(feature) &&
            (current.Results.GetValueOrDefault(feature)?.CanVerify != true || result.Confirmed);
        if (IsDisposed || toggle.IsDisposed) return result;
        if (accepted)
        {
            SaveState(preference, wanted ? 1 : 0);
            if (feature == HardwareFeature.CoolBoost && _trayCoolBoost != null) _trayCoolBoost.Checked = wanted;
        }
        else
        {
            _changingHardwareToggle = true;
            try { toggle.Checked = !wanted; } finally { _changingHardwareToggle = false; }
        }
        toggle.Enabled = _wmi.Capabilities.CanWrite(feature);
        _compatibilityTip.SetToolTip(toggle, result.Message);
        _jelli?.PublishState();
        return result;
    }

    private void PopulateAutoModes()
    {
        if (_cboAcProfile == null || _cboBatteryProfile == null) return;
        byte acSelection = _cboAcProfile.SelectedIndex >= 0 && _cboAcProfile.SelectedIndex < _acModeValues.Length
            ? _acModeValues[_cboAcProfile.SelectedIndex] : (byte)255;
        byte batterySelection = _cboBatteryProfile.SelectedIndex >= 0 && _cboBatteryProfile.SelectedIndex < _batteryModeValues.Length
            ? _batteryModeValues[_cboBatteryProfile.SelectedIndex] : (byte)255;
        var ac = _wmi.Capabilities.PowerModes.Where(m => m.OnAc).ToArray();
        var battery = _wmi.Capabilities.PowerModes.Where(m => m.OnBattery).ToArray();
        var acLabels = new[] { "Don't Change" }.Concat(ac.Select(m => m.Label)).ToArray();
        var batteryLabels = new[] { "Don't Change" }.Concat(battery.Select(m => m.Label)).ToArray();
        if (_cboAcProfile.Items.Count > 0 && _acModeValues.SequenceEqual(new byte[] {255}.Concat(ac.Select(m => m.Value))) &&
            _batteryModeValues.SequenceEqual(new byte[] {255}.Concat(battery.Select(m => m.Value))) &&
            _cboAcProfile.Items.Cast<object>().Select(item => item.ToString()).SequenceEqual(acLabels) &&
            _cboBatteryProfile.Items.Cast<object>().Select(item => item.ToString()).SequenceEqual(batteryLabels)) return;
        bool loading = _loadingPreferences;
        _loadingPreferences = true;
        try
        {
            _acModeValues = new byte[] {255}.Concat(ac.Select(m => m.Value)).ToArray();
            _batteryModeValues = new byte[] {255}.Concat(battery.Select(m => m.Value)).ToArray();
            _cboAcProfile.Items.Clear(); _cboBatteryProfile.Items.Clear();
            _cboAcProfile.Items.AddRange(acLabels);
            _cboBatteryProfile.Items.AddRange(batteryLabels);
            _cboAcProfile.SelectedIndex = Math.Max(0, Array.IndexOf(_acModeValues, acSelection));
            _cboBatteryProfile.SelectedIndex = Math.Max(0, Array.IndexOf(_batteryModeValues, batterySelection));
        }
        finally { _loadingPreferences = loading; }
    }

    private void ExportCompatibility()
    {
        using var save = new SaveFileDialog { Filter = "JSON report|*.json", FileName = "PredatorControl-compatibility.json" };
        IWin32Window owner = _jelli?.Visible == true ? _jelli : this;
        if (save.ShowDialog(owner) == DialogResult.OK) SafeFile.WriteText(save.FileName, _wmi.ExportDiagnosticReport());
    }
}
