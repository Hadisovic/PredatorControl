using Microsoft.Win32;

namespace PredatorControlApp;

public partial class Form1
{
    private PredatorToggle? _switchKeyboardPower;
    private Label? _lblKeyboardPower;
    private bool _restoringLighting = true;
    private bool _updatingKeyboard;
    private bool _keyboardBusy;
    private string? _keyboardNotice;
    private string? _keyboardUiSignature;
    private Label? _lblKeyboardEvidence;

    private static string KeyboardUiSignature(KeyboardControlProjection projection) =>
        $"{projection.Rgb}|{projection.ZoneCount}|{string.Join(',', projection.Effects)}|{projection.Power}|{projection.Brightness}|{projection.EffectSpeed}|{projection.IdleTimeout}|{projection.FixedColor}";

    private void RefreshKeyboardCapabilityUi(HardwareCapabilities capabilities)
    {
        var projection = KeyboardControlProjection.From(capabilities);
        string signature = KeyboardUiSignature(projection);
        if (_keyboardUiSignature == null) _keyboardUiSignature = signature;
        if (_keyboardUiSignature != signature && _tabPanels[2] != null)
        {
            int mode = SelectedRgbMode, brightness = _brightnessSlider.Value, speed = _speedSlider.Value;
            bool restoring = _restoringLighting;
            var previousTarget = _buildTarget;
            _restoringLighting = true;
            try
            {
                var page = _tabPanels[2];
                foreach (Control control in page.Controls.Cast<Control>().ToArray())
                {
                    if (control is Panel separator) _separators.Remove(separator);
                    control.Dispose();
                }
                page.Controls.Clear();
                Array.Clear(_btnZones);
                Array.Clear(_btnPresetColors);
                _switchKeyboardPower = null; _switchBacklight30s = null!;
                _lblKeyboardPower = _lblKeyboardEvidence = null; _lblBacklight30s = null!;
                _lblBrightHdr = null!; _lblSpeedHdr = null!;
                _btnAllZones = _btnCustomColor = null!;
                _btnSaveRgbProfile = _btnNewRgbProfile = _btnDeleteRgbProfile = null!;
                BuildKeyboardPage(capabilities);
                _brightnessSlider.Value = Math.Clamp(brightness, _brightnessSlider.Minimum, _brightnessSlider.Maximum);
                _speedSlider.Value = Math.Clamp(speed, _speedSlider.Minimum, _speedSlider.Maximum);
                SelectRgbMode(projection.AllowsEffect(mode) ? mode : projection.Effects.FirstOrDefault(-1));
                if (!projection.AllowsZone(_selectedZone)) _selectedZone = -1;
                LoadRgbProfilesIntoUi();
                UpdateRgbControlsState(SelectedRgbMode);
                _keyboardUiSignature = signature;
                ApplyCurrentTheme();
            }
            finally { _restoringLighting = restoring; _buildTarget = previousTarget; }
        }
        if (_lblKeyboardEvidence != null) _lblKeyboardEvidence.Text = projection.Explanation;
        UpdateKeyboardSwitches();
    }

    private static void PersistKeyboardSetting(string name, int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\PredatorControl");
        CompatibilitySettings.Write(key, name, value);
    }

    private static bool? ExplicitKeyboardPreference(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PredatorControl");
        return CompatibilitySettings.Read(key, name) is int value && value is 0 or 1 ? value == 1 : null;
    }

    private void RestoreKeyboardPreferences()
    {
        // Legacy Brightness/RGB_Mode/Backlight30s values are intentionally not migrated.
        // A missing preference means preserve firmware state, including on first launch.
        if (_wmi.Keyboard.Kind == KeyboardKind.Binary)
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PredatorControl");
            var preferences = KeyboardPreferences.Read(name => CompatibilitySettings.Read(key, name));
            var projection = KeyboardControlProjection.From(_wmi.Capabilities);
            var result = _wmi.KeyboardControl.Restore(projection.Power ? preferences.Enabled : null,
                projection.IdleTimeout ? preferences.IdleTimeout : null);
            _keyboardNotice = result?.Message;
        }
        else if (_wmi.Keyboard.SupportsRgb)
        {
            if (ExplicitKeyboardPreference("KeyboardLightingConfigured") == true)
            {
                _restoringLighting = false;
                try { ApplyRgbModeFromDropdown(SelectedRgbMode); }
                finally { _restoringLighting = true; }
            }
            var timeout = ExplicitKeyboardPreference("KeyboardIdleTimeout");
            if (timeout.HasValue && KeyboardControlProjection.From(_wmi.Capabilities).IdleTimeout)
                _keyboardNotice = _wmi.KeyboardControl.Apply(null, timeout).Message;
        }
        UpdateKeyboardSwitches();
    }

    private void UpdateKeyboardSwitches()
    {
        _updatingKeyboard = true;
        try
        {
            var projection = KeyboardControlProjection.From(_wmi.Capabilities);
            var state = _wmi.KeyboardControl.State;
            if (_lblKeyboardPower != null)
                _lblKeyboardPower.Text = state.Enabled.HasValue ? "Keyboard backlight" : "Keyboard backlight (unverified)";
            if (_lblBacklight30s != null)
                _lblBacklight30s.Text = state.IdleTimeout.HasValue ? "Turn off after 30 seconds idle" : "Idle sleep · 30 sec (unverified)";
            if (_switchKeyboardPower != null)
            {
                _switchKeyboardPower.Checked = state.Enabled ?? false;
                _switchKeyboardPower.Visible = projection.Power;
                _switchKeyboardPower.Enabled = !_keyboardBusy && projection.Power;
            }
            if (_switchBacklight30s != null)
            {
                _switchBacklight30s.Checked = state.IdleTimeout ?? false;
                _switchBacklight30s.Visible = projection.IdleTimeout;
                _switchBacklight30s.Enabled = !_keyboardBusy && projection.IdleTimeout;
            }
        }
        finally { _updatingKeyboard = false; }
    }

    private async Task<KeyboardOperationResult> ChangeKeyboardAsync(bool power, bool value)
    {
        var projection = KeyboardControlProjection.From(_wmi.Capabilities);
        if (power ? !projection.Power : !projection.IdleTimeout)
            return new("failed", "This keyboard control has no verified interface. No command was sent.");
        if (_keyboardBusy) return new("failed", "Wait for the current keyboard operation to finish.");
        _keyboardBusy = true;
        UpdateKeyboardSwitches();
        try
        {
            var result = await Task.Run(() => _wmi.KeyboardControl.Apply(power ? value : null, power ? null : value));
            _keyboardNotice = result.Message;
            if (result.Confirmed)
                PersistKeyboardSetting(power ? "KeyboardPowerEnabled" : "KeyboardIdleTimeout", value ? 1 : 0);
            return result;
        }
        finally
        {
            _keyboardBusy = false;
            if (!IsDisposed) UpdateKeyboardSwitches();
        }
    }

    private async Task ChangeKeyboardFromClassicAsync(bool power, bool value)
    {
        try
        {
            var result = await ChangeKeyboardAsync(power, value);
            if (!IsDisposed) MessageBox.Show(this, result.Message, "Keyboard backlight", MessageBoxButtons.OK,
                result.Confirmed ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception ex) { if (!IsDisposed) Program.Report(ex, false); }
    }
}
