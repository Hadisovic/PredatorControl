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

    private void StartFanSafetyWatchdog()
    {
        _fanSafetyTimer.Tick += (_, _) =>
        {
            if (!_fanCurveEnabled || GetCurrentFanByte() != 3 || Environment.TickCount64 - _lastTelemetryTick <= 5000) return;
            _curveAwaitingSensors = true;
            _cpuTempHistory.Clear(); _gpuTempHistory.Clear();
            _ = Task.Run(_wmi.RecoverAutomaticFans);
            _lblFanStatus.Text = "Telemetry stale · requested firmware Auto";
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
            c.Button.Left = pad + (w + gap) * i; c.Button.Width = w;
            c.Button.Text = _wmi.Capabilities.PowerModes.First(m => m.Value == c.Mode).Label;
            if (c.Tray != null) c.Tray.Text = c.Button.Text;
        }
    }

    private void ApplyCapabilityUi()
    {
        var caps = _wmi.Capabilities;
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
                : _activePowerBtn == null && caps.PowerModes.Count > 0
                    ? caps.PowerModes.FirstOrDefault(m => m.Value == 1) ?? caps.PowerModes[0]
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
        bool accepted = result.Accepted && (!_wmi.Capabilities.Results[feature].CanVerify || result.Confirmed);
        if (IsDisposed) return result;
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
        var ac = _wmi.Capabilities.PowerModes.Where(m => m.OnAc).ToArray();
        var battery = _wmi.Capabilities.PowerModes.Where(m => m.OnBattery).ToArray();
        _acModeValues = new byte[] {255}.Concat(ac.Select(m => m.Value)).ToArray();
        _batteryModeValues = new byte[] {255}.Concat(battery.Select(m => m.Value)).ToArray();
        _cboAcProfile.Items.AddRange(new[] {"Don't Change"}.Concat(ac.Select(m => m.Label)).ToArray());
        _cboBatteryProfile.Items.AddRange(new[] {"Don't Change"}.Concat(battery.Select(m => m.Label)).ToArray());
        _cboAcProfile.SelectedIndex = _cboBatteryProfile.SelectedIndex = 0;
    }

    private void ExportCompatibility()
    {
        using var save = new SaveFileDialog { Filter = "JSON report|*.json", FileName = "PredatorControl-compatibility.json" };
        if (save.ShowDialog(this) == DialogResult.OK) File.WriteAllText(save.FileName, _wmi.ExportDiagnosticReport());
    }
}
