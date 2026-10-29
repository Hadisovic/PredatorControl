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

    private static void PersistKeyboardSetting(string name, int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\PredatorControl");
        key.SetValue(name, value);
    }

    private static bool? ExplicitKeyboardPreference(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PredatorControl");
        return key?.GetValue(name) is int value && value is 0 or 1 ? value == 1 : null;
    }

    private void RestoreKeyboardPreferences()
    {
        // Legacy Brightness/RGB_Mode/Backlight30s values are intentionally not migrated.
        // A missing preference means preserve firmware state, including on first launch.
        if (_wmi.Keyboard.Kind == KeyboardKind.Binary)
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PredatorControl");
            var preferences = KeyboardPreferences.Read(name => key?.GetValue(name));
            var result = _wmi.KeyboardControl.Restore(preferences.Enabled, preferences.IdleTimeout);
            _keyboardNotice = result?.Message;
        }
        else if (_wmi.Keyboard.SupportsRgb)
        {
            if (ExplicitKeyboardPreference("KeyboardLightingConfigured") == true)
            {
                _restoringLighting = false;
                try { ApplyRgbModeFromDropdown(_rgbDropDown.SelectedIndex); }
                finally { _restoringLighting = true; }
            }
            var timeout = ExplicitKeyboardPreference("KeyboardIdleTimeout");
            if (timeout.HasValue) _keyboardNotice = _wmi.KeyboardControl.Apply(null, timeout).Message;
        }
        UpdateKeyboardSwitches();
    }

    private void UpdateKeyboardSwitches()
    {
        _updatingKeyboard = true;
        try
        {
            var state = _wmi.KeyboardControl.State;
            if (_lblKeyboardPower != null)
                _lblKeyboardPower.Text = state.Enabled.HasValue ? "Keyboard backlight" : "Keyboard backlight (unverified)";
            if (_lblBacklight30s != null)
                _lblBacklight30s.Text = state.IdleTimeout.HasValue ? "Turn off after 30 seconds idle" : "Idle sleep · 30 sec (unverified)";
            if (_switchKeyboardPower != null)
            {
                _switchKeyboardPower.Checked = state.Enabled ?? false;
                _switchKeyboardPower.Enabled = !_keyboardBusy && _wmi.Keyboard.SupportsPower;
            }
            if (_switchBacklight30s != null)
            {
                _switchBacklight30s.Checked = state.IdleTimeout ?? false;
                _switchBacklight30s.Enabled = !_keyboardBusy && _wmi.Keyboard.SupportsIdleTimeout;
            }
        }
        finally { _updatingKeyboard = false; }
    }

    private async Task<KeyboardOperationResult> ChangeKeyboardAsync(bool power, bool value)
    {
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
