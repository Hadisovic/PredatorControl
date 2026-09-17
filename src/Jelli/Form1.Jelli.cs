using System.Text.Json;

namespace PredatorControlApp;

public partial class Form1 : IJelliBackend
{
    private JelliHostForm? _jelli;
    private TelemetrySnapshot? _jelliTelemetry;
    private readonly Dictionary<string, Action<JsonElement>> _jelliActions = new();
    private readonly Dictionary<string, ToolStripMenuItem> _jelliMenuActions = new();
    private bool _jelliFallback;

    private async Task StartJelliAsync()
    {
        if (Environment.GetCommandLineArgs().Contains("--legacy-ui")) return;
        try
        {
            _jelli = new JelliHostForm(this);
            await _jelli.InitializeAsync(_overlayForm?.Visible == true);
            Hide();
            _telemetryService?.SetPollingState(true);
            _telemetryService?.SetSensorMask(true);
            _jelli.SetGaming(_overlayForm?.Visible == true);
        }
        catch (Exception ex)
        {
            _jelli?.Dispose();
            _jelli = null;
            if (_isClosing || IsDisposed) return;
            Opacity = 1;
            Show();
            Program.Report(new InvalidOperationException("Jelli could not start. The native controls remain available. " + ex.Message, ex), false);
        }
    }

    internal object JelliState(JelliSettings settings)
    {
        _jelliActions.Clear();
        var sections = new List<JelliSection>();
        JelliControl Button(string id, string label, PredatorButton b) {
            _jelliActions[id] = _ => { if (!b.Enabled) throw new InvalidOperationException("This action is currently unavailable."); b.InvokeAction(); };
            return new(id, label, "button", b.IsActive, b.Enabled, Accent: b.ColorIndicator is Color color ? ColorTranslator.ToHtml(color) : null);
        }
        JelliControl Toggle(string id, string label, PredatorToggle b) {
            _jelliActions[id] = d => { if (!b.Enabled) throw new InvalidOperationException("This feature is unavailable."); b.Checked = d.GetBoolean(); };
            return new(id, label, "toggle", b.Checked, b.Enabled);
        }
        JelliControl Select(string id, string label, PredatorDropDown b) {
            _jelliActions[id] = d => {
                int i = d.GetInt32();
                if (!b.Enabled || i < 0 || i >= b.Items.Count) throw new ArgumentException("Unavailable selection.");
                b.SelectedIndex = i;
            };
            return new(id, label, "select", b.SelectedIndex, b.Enabled,
                b.Items.Select((v, i) => new JelliChoice(v.ToString() ?? "", i)).ToArray());
        }
        JelliControl Range(string id, string label, PredatorSlider b) {
            _jelliActions[id] = d => {
                int v = d.GetInt32();
                if (!b.Enabled || v < b.Minimum || v > b.Maximum) throw new ArgumentException("Value outside the available range.");
                b.CommitValue(v);
            };
            return new(id, label, "range", b.Value, b.Enabled, Min: b.Minimum, Max: b.Maximum);
        }
        sections.Add(new("power", "Performance", [
            Button("power.quiet", "Quiet", _btnQuiet), Button("power.balanced", "Balanced", _btnBalanced),
            Button("power.performance", "Performance", _btnPerform), Button("power.turbo", "Turbo", _btnTurbo),
            Button("power.eco", "Eco", _btnEco), Select("power.ac", "When plugged in", _cboAcProfile),
            Select("power.battery", "On battery", _cboBatteryProfile)]));
        var fans = new List<JelliControl> { Button("fans.auto", "Auto", _btnAutoFan), Button("fans.max", "Maximum", _btnMaxFan), Button("fans.custom", "Custom", _btnCustomFan) };
        if (GetCurrentFanByte() == 3) fans.AddRange([
            Button("fans.fixed", "Fixed speed", _btnFixedSpeed), Button("fans.curve", "Edit fan curve ↗", _btnFanCurve),
            Range("fans.cpu", "CPU fan", _cpuFanSlider), Range("fans.gpu", "GPU fan", _gpuFanSlider)]);
        sections.Add(new("fans", "Cooling", fans.ToArray()));
        var gpu = new List<JelliControl>();
        if ((_gpuCapability & 1) != 0) gpu.Add(Button("gpu.hybrid", "Hybrid", _btnGpuOptimus));
        if ((_gpuCapability & 2) != 0) gpu.Add(Button("gpu.discrete", "Discrete", _btnGpuDiscrete));
        if ((_gpuCapability & 4) != 0) gpu.Add(Button("gpu.auto", "Automatic", _btnGpuAuto));
        sections.Add(new("gpu", "GPU · restart required", gpu.ToArray()));
        var display = new List<JelliControl>();
        if (_internalDisplayGdiName != null) display.AddRange([Button("display.60", "60 Hz", _btn60Hz), Button("display.max", $"{_maxHz} Hz", _btnMaxHz)]);
        foreach (var monitor in _externalMonitors) {
            string id = "display.external." + monitor.GdiName;
            _jelliActions[id] = d => {
                int hz = d.GetInt32();
                if (!monitor.SupportedHz.Contains(hz)) throw new ArgumentException("Unsupported display rate.");
                ApplyExternalDisplayMode(monitor.GdiName, hz);
            };
            display.Add(new(id, monitor.FriendlyName, "select", monitor.CurrentHz, Choices: monitor.SupportedHz.Select(hz => new JelliChoice($"{hz} Hz", hz)).ToArray()));
        }
        display.Add(Toggle("display.overdrive", "LCD overdrive", _switchLcdOverdrive));
        sections.Add(new("display", "Displays", display.ToArray()));
        _jelliActions["battery.limit"] = d => {
            bool wanted = d.GetBoolean(); ApplyBatteryLimit(wanted);
            if (_switchBatteryLimit.Checked != wanted) throw new InvalidOperationException("The backend could not apply the battery charge limit.");
        };
        sections.Add(new("battery", "Battery care", [new("battery.limit", "Stop charging at 80%", "toggle", _switchBatteryLimit.Checked)]));
        var rgb = new List<JelliControl> { Select("rgb.mode", "Lighting effect", _rgbDropDown) };
        rgb.Add(Button("rgb.all", "All zones", _btnAllZones));
        for (int i = 0; i < 4; i++) rgb.Add(Button($"rgb.zone{i}", $"Zone {i + 1}", _btnZones[i]));
        _jelliActions["rgb.color"] = d => {
            if (!_btnCustomColor.Enabled) throw new InvalidOperationException("This lighting effect uses firmware colors.");
            var text = d.GetString() ?? "";
            if (!System.Text.RegularExpressions.Regex.IsMatch(text, "^#[0-9a-fA-F]{6}$")) throw new ArgumentException("Invalid RGB color.");
            ApplyPresetColor(ColorTranslator.FromHtml(text));
        };
        for (int i = 0; i < _btnPresetColors.Length; i++) rgb.Add(Button($"rgb.preset{i}", PresetColorNames[i], _btnPresetColors[i]));
        rgb.Add(new("rgb.color", "Zone color", "color", ColorTranslator.ToHtml(_currentZoneColors[Math.Max(0, _selectedZone)]), _btnCustomColor.Enabled));
        rgb.AddRange([Range("rgb.brightness", "Brightness", _brightnessSlider), Range("rgb.speed", "Effect speed", _speedSlider), Toggle("rgb.sleep", "Backlight sleeps after 30 seconds", _switchBacklight30s)]);
        sections.Add(new("rgb", "Keyboard light", rgb.ToArray()));
                _jelliActions["overlay.mode"] = d => {
            string m = d.GetString() ?? "default";
            OverlayMode mode = m.ToLowerInvariant() switch { "light" => OverlayMode.Light, "full" => OverlayMode.Full, _ => OverlayMode.Default };
            _overlayForm?.SetMode(mode);
        };
        _jelliActions["overlay.scale"] = d => {
            int s = d.GetInt32();
            _overlayForm?.SetScale(s);
        };
        sections.Add(new("hardware", "Hardware", [Toggle("hardware.keyLock", "Lock Windows & Menu keys", _switchWinKeyLock)]));
        sections.Add(new("games", "Game Sync", [Toggle("games.enabled", "Apply profiles when games launch", _switchGameSync), Button("games.configure", "Configure game profiles ↗", _btnConfigureGames)]));
        sections.Add(new("app", "Application", [Toggle("app.startup", "Start with Windows", _switchStartWithWindows), Button("app.updates", "Check for updates", _btnCheckUpdates),
            Button("app.teal", "Teal", _btnThemeTeal), Button("app.crimson", "Crimson", _btnThemeCrimson), Button("app.oled", "OLED", _btnThemeOled)]));
        _jelliMenuActions.Clear();
        JelliMenu[] Menu(ToolStripItemCollection items, string prefix) => items.OfType<ToolStripMenuItem>().Where(i => i.Available).Select((item, index) => {
            string id = prefix + index;
            _jelliMenuActions[id] = item;
            return new JelliMenu(id, item.Text?.Trim() ?? "", item.Enabled, item.Checked, Menu(item.DropDownItems, id + "."));
        }).ToArray();
        return new {
            telemetry = _jelliTelemetry, powerMode = _lblPowerStatus.Text, fanMode = _lblFanStatus.Text,
            gpuMode = _activeGpuModeBtn?.Text, gpuNotice = _lblGpuRestartNotice.Text,
            fps = _overlayForm?.Visible == true ? _overlayForm.CurrentFps : null,
            overlay = _overlayForm?.Visible == true, sections, menu = Menu(_trayMenu.Items, "menu."), settings = settings with { JelliEnabled = !_jelliFallback },
            monitors = Screen.AllScreens.Select(s => new { id = s.DeviceName, label = s.DeviceName + (s.Primary ? " (primary)" : "") }),
            version = Updater.CurrentText, doubleClickMs = SystemInformation.DoubleClickTime,
            gameSyncStatus = _lblGameSyncStatus.Text,
        };
    }

    internal void JelliAction(string id, JsonElement data)
    {
        if (id.StartsWith("rgb.", StringComparison.Ordinal) ||
            (_jelliMenuActions.TryGetValue(id, out var selected) && new[] { _trayRgbStatic, _trayRgbBreathe, _trayRgbNeon, _trayRgbWave, _trayRgbShift, _trayRgbZoom, _trayRgbMeteor, _trayRgbTwinkle, _trayRgbOff }.Contains(selected)))
            _jelli?.DisableSync();
        if (_jelliActions.TryGetValue(id, out var action)) action(data);
        else if (_jelliMenuActions.TryGetValue(id, out var menu) && menu.Enabled && menu.Available && menu.DropDownItems.Count == 0) menu.PerformClick();
        else throw new ArgumentException("Unknown or unavailable control.");
    }

    private bool _isSyncingJelli;
    public void SetJelliEnabled(bool enabled)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => SetJelliEnabled(enabled))); return; }
        if (_isSyncingJelli) return;
        _isSyncingJelli = true;
        try
        {
            _jelliFallback = !enabled;
            SaveState("JelliEnabled", enabled ? 1 : 0);

            if (_switchJelliEnabled != null && _switchJelliEnabled.Checked != enabled)
                _switchJelliEnabled.Checked = enabled;

            if (_trayJelliToggle != null && _trayJelliToggle.Checked != enabled)
                _trayJelliToggle.Checked = enabled;

            if (enabled)
            {
                Hide();
                if (_jelli != null)
                {
                    _jelli.Show();
                    _jelli.SetSurface("compact");
                }
                else
                {
                    _ = StartJelliAsync();
                }
            }
            else
            {
                try { _jelli?.Dispose(); } catch { }
                _jelli = null;
                TerminateWebView2Processes();
                Opacity = 1;
                ShowApp();
            }
        }
        finally
        {
            _isSyncingJelli = false;
        }
    }

    internal void JelliFallback()
    {
        SetJelliEnabled(false);
    }

    internal void ApplyJelliOverlaySettings(JelliSettings settings) => _overlayForm?.Configure(settings);
    internal bool WriteJelliColor(Color color)
    {
        // A transient override: preserve all manual zone colors and persisted settings.
        if (_gameSync.ActiveGameExe != null || _isGameSyncOverriding) return false;
        _wmi.Set4ZoneColors([color, color, color, color], 0, (byte)_brightnessSlider.Value, GetMappedSpeed());
        return true;
    }
    object IJelliBackend.State(JelliSettings settings) => JelliState(settings);
    void IJelliBackend.Action(string id, JsonElement data) => JelliAction(id, data);
    void IJelliBackend.Fallback() => JelliFallback();
    void IJelliBackend.SetJelliEnabled(bool enabled) => SetJelliEnabled(enabled);
    void IJelliBackend.ConfigureOverlay(JelliSettings settings) => ApplyJelliOverlaySettings(settings);
    bool IJelliBackend.TryWriteColor(Color color) => WriteJelliColor(color);
    void IJelliBackend.RestoreColor() => RestoreJelliColor();
    void IJelliBackend.Exit() => HandleIpcCommand("EXIT");
    internal void RestoreJelliColor() => ApplyRgbModeFromDropdown(_rgbDropDown.SelectedIndex);

    private static void TerminateWebView2Processes()
    {
        try
        {
            int myPid = Environment.ProcessId;
            using var searcher = new System.Management.ManagementObjectSearcher(
                $"SELECT ProcessId FROM Win32_Process WHERE ParentProcessId = {myPid} AND Name = 'msedgewebview2.exe'");
            foreach (var obj in searcher.Get())
            {
                if (uint.TryParse(obj["ProcessId"]?.ToString(), out uint childPid))
                {
                    try
                    {
                        using var childProc = System.Diagnostics.Process.GetProcessById((int)childPid);
                        if (!childProc.HasExited) childProc.Kill();
                    }
                    catch { }
                }
            }
        }
        catch { }
    }
}
