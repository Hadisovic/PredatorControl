// ====================================================================================================
// PREDATOR CONTROL · SYSTEM TRAY ICON & CONTEXT MENU
// File: src/Form1.Tray.cs
// ====================================================================================================

using System.Runtime.Versioning;

namespace PredatorControlApp
{
    [SupportedOSPlatform("windows")]
    public partial class Form1
    {
        #region System Tray

        private void SetupSystemTray()
        {
            try { _trayIcon.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { _trayIcon.Icon = SystemIcons.Application; }

            _trayIcon.ContextMenuStrip = _trayMenu;
            _trayIcon.Text = "Predator Control";
            try { _trayIcon.Visible = true; } catch { }
            _trayIcon.DoubleClick += (s, e) => ShowApp();
            _trayIcon.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowApp(); };
        }

        private void BuildTrayMenu()
        {
            _trayMenu = new ContextMenuStrip();

            var powerMenu = new ToolStripMenuItem("  Power Mode");
            _trayPowerQuiet = new ToolStripMenuItem("Quiet", null, (s, e) => ApplyPowerMode(0x00, _btnQuiet, true));
            _trayPowerBal = new ToolStripMenuItem("Balanced", null, (s, e) => ApplyPowerMode(0x01, _btnBalanced, true));
            _trayPowerPerf = new ToolStripMenuItem("Performance", null, (s, e) => ApplyPowerMode(0x04, _btnPerform, true));
            _trayPowerTurbo = new ToolStripMenuItem("Turbo", null, (s, e) => ApplyPowerMode(0x05, _btnTurbo, true));
            _trayPowerEco = new ToolStripMenuItem("Eco", null, (s, e) => ApplyPowerMode(UsesWindowsPowerPlans ? (byte)2 : (byte)6, _btnEco, true));
            foreach (var c in PowerControls())
                if (c.Tray != null)
                {
                    var mode = _wmi.Capabilities.PowerModes.FirstOrDefault(m => m.Value == c.Mode);
                    c.Tray.Visible = mode != null;
                    if (mode != null) c.Tray.Text = mode.Label;
                }
            powerMenu.DropDownItems.AddRange(PowerControls().Select(c => c.Tray!).ToArray());

            var fanMenu = new ToolStripMenuItem("  Fan Mode");
            _trayFanAuto = new ToolStripMenuItem("Auto", null, (s, e) => ApplyFanMode(0x01, _btnAutoFan));
            _trayFanMax = new ToolStripMenuItem("Max", null, (s, e) => ApplyFanMode(0x02, _btnMaxFan));
            _trayFanCustom = new ToolStripMenuItem("Custom", null, (s, e) => ApplyFanMode(0x03, _btnCustomFan));
            fanMenu.DropDownItems.AddRange([_trayFanAuto, _trayFanMax, _trayFanCustom]);
            if (_wmi.Capabilities.SupportsCoolBoost)
            {
                _trayCoolBoost = new ToolStripMenuItem("CoolBoost", null, (s, e) => ToggleCoolBoost())
                {
                    CheckOnClick = true,
                    Checked = _wmi.CoolBoost
                };
                fanMenu.DropDownItems.Add(_trayCoolBoost);
            }

            var displayMenu = new ToolStripMenuItem("  Display (Internal)");
            _trayDisplay60 = new ToolStripMenuItem("60 Hz", null, (s, e) => ApplyDisplayMode(60, _btn60Hz));
            if (_maxHz > 60)
            {
                _trayDisplayMax = new ToolStripMenuItem($"{_maxHz} Hz", null, (s, e) => ApplyDisplayMode(_maxHz, _btnMaxHz));
                displayMenu.DropDownItems.AddRange([_trayDisplay60, _trayDisplayMax]);
            }
            else
            {
                _trayDisplayMax = null;
                displayMenu.DropDownItems.Add(_trayDisplay60);
            }

            var gpuMenu = new ToolStripMenuItem("  GPU Mode (MUX)");
            _trayGpuOptimus = new ToolStripMenuItem("Optimus (Hybrid)", null, (s, e) => ApplyGpuMode(AcerAgentClient.GPU_MODE_OPTIMUS, _btnGpuOptimus, "Optimus (Hybrid)"));
            _trayGpuDiscrete = new ToolStripMenuItem("Discrete GPU", null, (s, e) => ApplyGpuMode(AcerAgentClient.GPU_MODE_DISCRETE, _btnGpuDiscrete, "Discrete GPU Only"));
            _trayGpuAuto = new ToolStripMenuItem("Auto (Advanced Optimus)", null, (s, e) => ApplyGpuMode(AcerAgentClient.GPU_MODE_AUTO, _btnGpuAuto, "Auto (Advanced Optimus)"));
            gpuMenu.DropDownItems.AddRange([_trayGpuOptimus, _trayGpuDiscrete, _trayGpuAuto]);

            var rgbMenu = new ToolStripMenuItem("  Keyboard RGB");
            _trayRgbStatic = new ToolStripMenuItem("Static", null, (s, e) => ApplyRgbModeFromDropdown(0));
            _trayRgbBreathe = new ToolStripMenuItem("Breathing", null, (s, e) => ApplyRgbModeFromDropdown(1));
            _trayRgbNeon = new ToolStripMenuItem("Neon", null, (s, e) => ApplyRgbModeFromDropdown(2));
            _trayRgbWave = new ToolStripMenuItem("Wave", null, (s, e) => ApplyRgbModeFromDropdown(3));
            _trayRgbShift = new ToolStripMenuItem("Shifting", null, (s, e) => ApplyRgbModeFromDropdown(4));
            _trayRgbZoom = new ToolStripMenuItem("Zoom", null, (s, e) => ApplyRgbModeFromDropdown(5));
            _trayRgbMeteor = new ToolStripMenuItem("Meteor", null, (s, e) => ApplyRgbModeFromDropdown(6));
            _trayRgbTwinkle = new ToolStripMenuItem("Twinkling", null, (s, e) => ApplyRgbModeFromDropdown(7));
            _trayRgbOff = new ToolStripMenuItem("Off", null, (s, e) => ApplyRgbModeFromDropdown(8));
            rgbMenu.DropDownItems.AddRange([_trayRgbStatic, _trayRgbBreathe, _trayRgbNeon, _trayRgbWave,
                                            _trayRgbShift, _trayRgbZoom, _trayRgbMeteor, _trayRgbTwinkle, _trayRgbOff]);

            _trayBatteryMenu = new ToolStripMenuItem("  Battery Limit");
            _trayBatteryLimit80 = new ToolStripMenuItem("Limit to 80%", null, (s, e) => ApplyBatteryLimit(true));
            _trayBatteryLimit100 = new ToolStripMenuItem("Full Charge (100%)", null, (s, e) => ApplyBatteryLimit(false));
            _trayBatteryMenu.DropDownItems.AddRange([_trayBatteryLimit80, _trayBatteryLimit100]);

            _trayMenu.Items.Add(powerMenu);
            _trayMenu.Items.Add(fanMenu);
            _trayMenu.Items.Add(displayMenu);
            if (_wmi.Capabilities.SupportsGpuMux) _trayMenu.Items.Add(gpuMenu);
            _trayMenu.Items.Add(_trayBatteryMenu);
            if (_wmi.Capabilities.SupportsRgbLighting) _trayMenu.Items.Add(rgbMenu);

            var hardwareMenu = new ToolStripMenuItem("  Hardware Features");
            _trayWinKey = new ToolStripMenuItem("Lock Windows Key", null, (s, e) =>
            {
                if (_switchWinKeyLock != null) _switchWinKeyLock.Checked = !_switchWinKeyLock.Checked;
            });
            var trayLcdOverdrive = new ToolStripMenuItem("LCD Overdrive", null, (s, e) =>
            {
                if (_switchLcdOverdrive != null) _switchLcdOverdrive.Checked = !_switchLcdOverdrive.Checked;
            });
            hardwareMenu.DropDownItems.AddRange([_trayWinKey, trayLcdOverdrive]);
            _trayMenu.Items.Add(hardwareMenu);

            _trayJelliToggle = new ToolStripMenuItem("  Jelli Desktop Companion", null, (s, e) => SetJelliEnabled(!(_trayJelliToggle?.Checked ?? !_jelliFallback)))
            {
                Checked = !_jelliFallback
            };
            _trayMenu.Items.Add(_trayJelliToggle);

            _trayOverlay = new ToolStripMenuItem("  Gaming Overlay (Ctrl+Shift+O)", null, (s, e) => ToggleGameOverlay())
            {
                CheckOnClick = true
            };
            _trayMenu.Items.Add(_trayOverlay);
            _trayMenu.Items.Add("  Overlay Settings...", null, (s, e) => OpenOverlaySettings());

            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("Export Hardware Diagnostic Report", null, (s, e) =>
            {
                try
                {
                    string dump = _wmi.ExportDiagnosticReport();
                    string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "PredatorControl_Diagnostic.json");
                    File.WriteAllText(path, dump);
                    MessageBox.Show($"Hardware diagnostic report saved to your Desktop:\n{path}\n\nYou can attach this file to a GitHub issue or discussion to help support your laptop model!", "Diagnostic Report Exported", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not export report: {ex.Message}", "Export Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            });
            _trayMenu.Items.Add("Open Dashboard", null, (s, e) => ShowApp());
            _trayMenu.Items.Add("Exit", null, (s, e) => { _isClosing = true; Application.Exit(); });
        }

        private static void CheckTrayItem(ToolStripMenuItem active, params ToolStripMenuItem?[] group)
        {
            foreach (var item in group) if (item != null) item.Checked = false;
            active.Checked = true;
        }

        private void CheckRgbTrayFromMode(int mode)
        {
            var active = mode switch
            {
                0 => _trayRgbStatic,
                1 => _trayRgbBreathe,
                2 => _trayRgbNeon,
                3 => _trayRgbWave,
                4 => _trayRgbShift,
                5 => _trayRgbZoom,
                6 => _trayRgbMeteor,
                7 => _trayRgbTwinkle,
                _ => _trayRgbOff
            };
            CheckTrayItem(active, _trayRgbStatic, _trayRgbBreathe, _trayRgbNeon, _trayRgbWave,
                          _trayRgbShift, _trayRgbZoom, _trayRgbMeteor, _trayRgbTwinkle, _trayRgbOff);
        }

        #endregion
    }
}
