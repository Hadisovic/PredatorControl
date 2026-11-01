// ====================================================================================================
// PREDATOR CONTROL · STATE PERSISTENCE, THEME SWITCHING & STARTUP TASK REGISTRATION
// File: src/Form1.Preferences.cs
// ====================================================================================================

using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace PredatorControlApp
{
    [SupportedOSPlatform("windows")]
    public partial class Form1
    {
        #region Theme Switching

        private void ApplyCurrentTheme()
        {
            var theme = ThemeManager.Current;
            BackColor = theme.FormBg;
            _pnlTitle.BackColor = theme.TitleBarBg;
            _contentPanel.BackColor = theme.FormBg;
            if (_pnlCustomFan != null) _pnlCustomFan.BackColor = theme.FormBg;

            var activeThemeBtn = theme.Mode switch
            {
                ThemeMode.NitroCrimson => _btnThemeCrimson,
                ThemeMode.OledBlack => _btnThemeOled,
                _ => _btnThemeTeal
            };
            HighlightBtn(activeThemeBtn, ref _activeThemeBtn);

            foreach (var sep in _separators)
                sep.BackColor = theme.Separator;

            Invalidate(true);
        }

        private void OnThemeChanged()
        {
            if (InvokeRequired) { BeginInvoke(new Action(OnThemeChanged)); return; }
            ApplyCurrentTheme();
        }

        #endregion

        #region State Persistence

        private void SaveState(string name, int value)
        {
            if (_isGameSyncOverriding || _loadingPreferences) return;
            if (!_restoringLighting && _wmi.Keyboard.SupportsRgb &&
                (name is "RGB_Mode" or "Brightness" or "RGB_Speed" || name.StartsWith("ZoneColor_", StringComparison.Ordinal)))
                PersistKeyboardSetting("KeyboardLightingConfigured", 1);
            DebounceHelper.Debounce($"State_{name}", () =>
            {
                try
                {
                    using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\PredatorControl");
                    CompatibilitySettings.Write(key, name, value);
                }
                catch { }

                // Mirror hardware states to HKLM so boot-time task can enforce them before user logon
                if (name is "BatteryLimit" or "Power" or "Fan" or "CoolBoostEnabled" or "RGB_Mode" or "RGB_R" or "RGB_G" or "RGB_B" or "Brightness" or "RGB_Speed"
                    or "BootSound" or "UsbCharging" or "AutoCoolBoost" or "TurboKeyAction" or "PredatorKeyAction" or "FanSpeedAux"
                    || name.StartsWith("ZoneColor_"))
                {
                    try
                    {
                        using var hklmKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\PredatorControl");
                        if (hklmKey != null) CompatibilitySettings.Write(hklmKey, name, value);
                    }
                    catch { }
                }
            }, 300);
        }

        private static int GetInt(RegistryKey key, string name, int fallback, int min, int max)
        {
            int v = fallback;
            try
            {
                object? raw = CompatibilitySettings.Read(key, name);
                if (raw is int i) v = i;
                else if (raw != null && int.TryParse(raw.ToString(), out int p)) v = p;
            }
            catch { }
            return Math.Clamp(v, min, max);
        }

        private static int LoadState(string name, int fallback = 0)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PredatorControl");
                if (key != null)
                {
                    object? raw = CompatibilitySettings.Read(key, name);
                    if (raw is int i) return i;
                    if (raw != null && int.TryParse(raw.ToString(), out int p)) return p;
                }
            }
            catch { }
            return fallback;
        }

        private void SaveCurveToRegistry(string name, List<Point> points)
        {
            DebounceHelper.Debounce($"Curve_{name}", () =>
            {
                try
                {
                    using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\PredatorControl");
                    string data = string.Join(";", points.Select(p => $"{p.X},{p.Y}"));
                    key.SetValue(name, data);
                }
                catch { }
            }, 300);
        }

        private List<Point>? LoadCurveFromRegistry(string name)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\PredatorControl");
                string? data = CompatibilitySettings.Read(key, name) as string;
                if (string.IsNullOrEmpty(data)) return null;

                var pts = new List<Point>();
                foreach (var pair in data.Split(';'))
                {
                    var parts = pair.Split(',');
                    if (parts.Length == 2 && int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y))
                        pts.Add(new Point(x, y));
                }
                return pts.Count >= 2 ? FanCurveGraph.Normalize(pts) : null;
            }
            catch { return null; }
        }

        private void LoadMemory()
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\PredatorControl");
                int savedPower = GetInt(key, "Power", 0x01, 0x00, 0xFF);
                int savedFan = GetInt(key, "Fan", 0x01, 0x00, 0xFF);
                int savedRgbMode = GetInt(key, "RGB_Mode", 0, 0, 8);
                int savedBrightness = GetInt(key, "Brightness", 100, 0, 100);
                int savedSpeed = GetInt(key, "RGB_Speed", 50, 1, 100);

                int savedR = GetInt(key, "RGB_R", 0, 0, 255);
                int savedG = GetInt(key, "RGB_G", 230, 0, 255);
                int savedB = GetInt(key, "RGB_B", 180, 0, 255);
                _colorPicker.Color = Color.FromArgb(savedR, savedG, savedB);

                _brightnessSlider.Value = Math.Clamp(savedBrightness, 0, 100);
                if (_lblBrightHdr != null) _lblBrightHdr.Text = $"BRIGHTNESS: {_brightnessSlider.Value}%";
                _speedSlider.Value = Math.Clamp(savedSpeed, 1, 100);
                if (_lblSpeedHdr != null) _lblSpeedHdr.Text = $"EFFECT SPEED: {_speedSlider.Value}%";

                object? rawSavedPower = CompatibilitySettings.Read(key, "Power");
                int? requestedPower = rawSavedPower is int pi ? pi : rawSavedPower != null && int.TryParse(rawSavedPower.ToString(), out int pp) ? pp : null;
                var powerRestore = _wmi.RestoreSavedPowerMode(requestedPower);
                if (_wmi.Capabilities.ActivePowerMode is byte restoredMode &&
                    _wmi.Capabilities.PowerModes.FirstOrDefault(m => m.Value == restoredMode) is PowerMode activePower)
                {
                    HighlightBtn(PowerByteToBtn(restoredMode), ref _activePowerBtn);
                    _lblPowerStatus.Text = activePower.Label;
                    var powerControls = PowerControls().ToArray();
                    if (powerControls.FirstOrDefault(c => c.Mode == restoredMode).Tray is ToolStripMenuItem activeTray)
                        CheckTrayItem(activeTray, powerControls.Select(c => c.Tray).ToArray());
                }
                else if (requestedPower is >= 0 and <= 255 && powerRestore.Accepted &&
                         _wmi.Capabilities.PowerModes.FirstOrDefault(m => m.Value == (byte)requestedPower.Value) is PowerMode fallbackPower)
                {
                    HighlightBtn(PowerByteToBtn(fallbackPower.Value), ref _activePowerBtn);
                    _lblPowerStatus.Text = fallbackPower.Label;
                    var powerControls = PowerControls().ToArray();
                    if (powerControls.FirstOrDefault(c => c.Mode == fallbackPower.Value).Tray is ToolStripMenuItem activeTray)
                        CheckTrayItem(activeTray, powerControls.Select(c => c.Tray).ToArray());
                }

                byte legacyAc = AcProfileValues[Math.Clamp(GetInt(key, "AutoPowerAC", 0, 0, 4), 0, 4)];
                byte legacyBattery = BatteryProfileValues[Math.Clamp(GetInt(key, "AutoPowerBattery", 0, 0, 3), 0, 3)];
                _cboAcProfile.SelectedIndex = Math.Max(0, Array.IndexOf(_acModeValues, (byte)GetInt(key, "AutoPowerACValue", legacyAc, 0, 255)));
                _cboBatteryProfile.SelectedIndex = Math.Max(0, Array.IndexOf(_batteryModeValues, (byte)GetInt(key, "AutoPowerBatteryValue", legacyBattery, 0, 255)));

                // H3: If a previous session crashed or was force-killed while manual fans were active, return to firmware Auto first.
                _wmi.RecoverCrashLeftoverManualFans();

                int savedFanSpeedCpu = GetInt(key, "FanSpeedCpu", 50, 10, 100);
                int savedFanSpeedGpu = GetInt(key, "FanSpeedGpu", 50, 10, 100);
                int savedFanSpeedAux = GetInt(key, "FanSpeedAux", 50, 10, 100);

                var (fanMode, fanBtn) = savedFan switch
                {
                    0x02 => ((byte)0x02, _btnMaxFan),
                    0x03 => ((byte)0x03, _btnCustomFan),
                    _ => ((byte)0x01, _btnAutoFan)
                };

                var loadedCpu = LoadCurveFromRegistry("CpuCurve");
                var loadedGpu = LoadCurveFromRegistry("GpuCurve");
                if (loadedCpu != null) _cpuCurvePoints = loadedCpu;
                if (loadedGpu != null) _gpuCurvePoints = loadedGpu;

                int savedCurveEnabled = GetInt(key, "FanCurveEnabled", 0, 0, 1);
                if (savedCurveEnabled == 1 && fanMode == 0x03 && _wmi.Capabilities.CanWrite(HardwareFeature.FanCurve))
                { _fanCurveEnabled = true; _curveAwaitingSensors = true; }

                if (CompatibilitySettings.Read(key, "Fan") != null && !(fanMode == 3 && _fanCurveEnabled)) ApplyFanMode(fanMode, fanBtn);
                if (_wmi.Capabilities.SupportsCoolBoost && GetInt(key, "CoolBoostEnabled", 0, 0, 1) == 1)
                    Task.Run(() => { try { _wmi.SetCoolBoost(true); } catch { } });

                if (fanMode == 0x03)
                {
                    if (_fanCurveEnabled)
                    {
                        _wmi.RecoverAutomaticFans(); // Real sensor samples are required before a curve can write.
                    }
                    else
                    {
                        _cpuFanSlider.Value = savedFanSpeedCpu;
                        _gpuFanSlider.Value = savedFanSpeedGpu;
                        _lblCpuFanSpeedHdr.Text = $"CPU FAN: {savedFanSpeedCpu}%";
                        _lblGpuFanSpeedHdr.Text = $"GPU FAN: {savedFanSpeedGpu}%";

                        bool hasAux = _wmi.HasAuxFan && _auxFanSlider != null;
                        if (hasAux && _auxFanSlider != null)
                        {
                            _auxFanSlider.Value = savedFanSpeedAux;
                            if (_lblAuxFanSpeedHdr != null) _lblAuxFanSpeedHdr.Text = $"AUX FAN: {savedFanSpeedAux}%";
                        }

                        int cpuSpd = savedFanSpeedCpu, gpuSpd = savedFanSpeedGpu, auxSpd = savedFanSpeedAux;
                        _ = Task.Run(() =>
                        {
                            try
                            {
                                _wmi.SetFanSpeed((byte)cpuSpd, (byte)gpuSpd);
                                if (hasAux) _wmi.SetAuxFanSpeed((byte)auxSpd);
                            }
                            catch { }
                        });
                    }
                }

                if (_switchBootSound != null && _wmi.Supports(AcerFeature.BootSound))
                {
                    int savedBoot = LoadState("BootSound", -1);
                    if (savedBoot >= 0)
                    {
                        _switchBootSound.Checked = savedBoot == 1;
                        Task.Run(() => { try { _wmi.SetBootSound(savedBoot == 1); } catch { } });
                    }
                }
                if (_switchUsbCharging != null && _wmi.Supports(AcerFeature.UsbCharging))
                {
                    int savedUsb = LoadState("UsbCharging", -1);
                    if (savedUsb >= 0)
                    {
                        _switchUsbCharging.Checked = savedUsb == 1;
                        Task.Run(() => { try { _wmi.SetUsbCharging(savedUsb == 1); } catch { } });
                    }
                }

                // Load 4-Zone Colors
                for (int z = 0; z < 4; z++)
                {
                    int savedArgb = GetInt(key, $"ZoneColor_{z}", -1, int.MinValue, int.MaxValue);
                    if (savedArgb != -1)
                    {
                        _currentZoneColors[z] = Color.FromArgb(savedArgb);
                    }
                    else
                    {
                        _currentZoneColors[z] = Color.FromArgb(savedR, savedG, savedB);
                    }

                    if (_btnZones[z] != null)
                        _btnZones[z].ColorIndicator = _currentZoneColors[z];
                }
                if (_btnAllZones != null)
                    _btnAllZones.ColorIndicator = _currentZoneColors[0];

                int clampedMode = Math.Clamp(savedRgbMode, 0, 8);
                if (_wmi.Keyboard.SupportsRgb) SelectRgbMode(clampedMode);
                // UI values may load, but only explicit 1.4+ lighting preferences are restored.
                RestoreKeyboardPreferences();

                // Restore active RGB profile if in Static mode
                if (_wmi.Keyboard.SupportsRgb && LoadState("KeyboardLightingConfigured", 0) == 1 && clampedMode == 0)
                {
                    string activeProfName = RgbProfileManager.GetActiveProfile();
                    var allProfiles = RgbProfileManager.LoadProfiles();
                    var activeProf = allProfiles.FirstOrDefault(p => string.Equals(p.Name, activeProfName, StringComparison.OrdinalIgnoreCase));
                    if (activeProf != null)
                    {
                        _restoringLighting = false;
                        try { ApplyRgbProfile(activeProf); }
                        finally { _restoringLighting = true; }
                    }
                }

                var activeColor = Color.FromArgb(savedR, savedG, savedB);
                if (_btnCustomColor != null)
                    _btnCustomColor.ColorIndicator = activeColor;

                _isUpdatingBattery = true;
                _switchBatteryLimit.Checked = _wmi.Capabilities.BatteryLimitEnabled ?? false;
                _switchBatteryLimit.Enabled = _wmi.Capabilities.SupportsBatteryControl;
                _lblBatteryStatus.Text = _wmi.Capabilities.BatteryLimitEnabled is bool limited
                    ? limited ? "80% limit confirmed" : "Full charging confirmed" : "Charge-limit state N/A";
                _isUpdatingBattery = false;

                if (IsStartupEnabled())
                {
                    Task.Run(() => { try { SetStartupEnabled(true); } catch { } });
                }

                // LCD Overdrive
                int savedLcdOverdrive = GetInt(key, "LcdOverdrive", 0, 0, 1);
                if (_switchLcdOverdrive != null) _switchLcdOverdrive.Checked = (savedLcdOverdrive == 1);
                if (CompatibilitySettings.Read(key, "LcdOverdrive") != null) Task.Run(() => { try { _wmi.SetLcdOverdrive(savedLcdOverdrive == 1); } catch { } });

                UpdateKeyboardSwitches();

                // Gaming Overlay HUD
                int savedOverlay = GetInt(key, "OverlayEnabled", 0, 0, 1);
                if (savedOverlay == 1)
                {
                    BeginInvoke(() => SetOverlayVisible(true));
                }

                // Jelli Desktop Companion
                int savedJelli = GetInt(key, "JelliEnabled", 1, 0, 1);
                bool jelliActive = (savedJelli == 1) && !Environment.GetCommandLineArgs().Contains("--legacy-ui");
                _jelliFallback = !jelliActive;
                if (_switchJelliEnabled != null) _switchJelliEnabled.Checked = jelliActive;
                if (_trayJelliToggle != null) _trayJelliToggle.Checked = jelliActive;

                // Windows Key Lock
                int savedWinKeyLock = GetInt(key, "LockWinKey", 0, 0, 1);
                if (_switchWinKeyLock != null) _switchWinKeyLock.Checked = (savedWinKeyLock == 1);
                if (_trayWinKey != null) _trayWinKey.Checked = (savedWinKeyLock == 1);
                PredatorKeyHook.SetWinKeyLocked(savedWinKeyLock == 1);
                if (_wmi.Capabilities.SupportsWinKey && (CompatibilitySettings.Read(key, "LockWinKey") != null || savedWinKeyLock == 1))
                    Task.Run(() => { try { _wmi.SetWinKeyLock(savedWinKeyLock == 1); } catch { } });

                // GPU Working Mode (MUX Switch)
                bool hasExplicitGpuMode = key.GetValue("GpuMode") != null;
                int savedGpuMode = GetInt(key, "GpuMode", AcerAgentClient.GPU_MODE_OPTIMUS, 0, 2);
                _currentGpuMode = hasExplicitGpuMode ? savedGpuMode : null;
                PredatorButton? targetBtn = savedGpuMode switch
                {
                    AcerAgentClient.GPU_MODE_OPTIMUS => _btnGpuOptimus,
                    AcerAgentClient.GPU_MODE_DISCRETE => _btnGpuDiscrete,
                    AcerAgentClient.GPU_MODE_AUTO => _btnGpuAuto,
                    _ => _btnGpuOptimus
                };
                if (targetBtn != null)
                    HighlightBtn(targetBtn, ref _activeGpuModeBtn);

                ToolStripMenuItem? targetTray = savedGpuMode switch
                {
                    AcerAgentClient.GPU_MODE_OPTIMUS => _trayGpuOptimus,
                    AcerAgentClient.GPU_MODE_DISCRETE => _trayGpuDiscrete,
                    AcerAgentClient.GPU_MODE_AUTO => _trayGpuAuto,
                    _ => _trayGpuOptimus
                };
                if (targetTray != null)
                    CheckTrayItem(targetTray, _trayGpuOptimus, _trayGpuDiscrete, _trayGpuAuto);

                // Query live hardware capability & mode from OEM service/firmware
                Task.Run(async () =>
                {
                    try
                    {
                        int cap = await _wmi.GetGpuModeCapabilityAsync();
                        if (cap > 0)
                        {
                            _gpuCapability = cap;
                        }
                        else if (_wmi.Capabilities.SupportsGpuMux)
                        {
                            _gpuCapability = _wmi.Capabilities.GpuModeMask;
                        }
                        else
                        {
                            _gpuCapability = 0;
                        }

                        int? liveMode = await _wmi.GetGpuModeAsync();

                        void UpdateGpuUi()
                        {
                            if (_isClosing || IsDisposed) return;

                            if ((_gpuCapability & 4) == 0 && _btnGpuAuto != null)
                            {
                                _btnGpuAuto.Visible = false;
                                if (_trayGpuAuto != null) _trayGpuAuto.Visible = false;
                            }
                            else if (_btnGpuAuto != null)
                            {
                                _btnGpuAuto.Visible = true;
                                if (_trayGpuAuto != null) _trayGpuAuto.Visible = true;
                            }

                            if ((_gpuCapability & 2) == 0)
                            {
                                if (_btnGpuDiscrete != null)
                                {
                                    _btnGpuDiscrete.Enabled = false;
                                    _btnGpuDiscrete.Text = "Discrete (N/A)";
                                }
                                if (_trayGpuDiscrete != null)
                                {
                                    _trayGpuDiscrete.Enabled = false;
                                }
                                if (_lblGpuRestartNotice != null)
                                {
                                    _lblGpuRestartNotice.Text = "Physical MUX switch not present on this model (Optimus Only)";
                                }
                            }
                            else
                            {
                                if (_btnGpuDiscrete != null)
                                {
                                    _btnGpuDiscrete.Enabled = true;
                                    _btnGpuDiscrete.Text = "Discrete GPU";
                                }
                                if (_trayGpuDiscrete != null)
                                {
                                    _trayGpuDiscrete.Enabled = true;
                                }
                                if (_lblGpuRestartNotice != null && _lblGpuRestartNotice.Text.Contains("Physical MUX"))
                                {
                                    _lblGpuRestartNotice.Text = "Requires system restart to take effect in firmware";
                                }
                            }

                            if (liveMode.HasValue)
                            {
                                PredatorButton? liveBtn = liveMode.Value switch
                                {
                                    AcerAgentClient.GPU_MODE_OPTIMUS => _btnGpuOptimus,
                                    AcerAgentClient.GPU_MODE_DISCRETE => _btnGpuDiscrete,
                                    AcerAgentClient.GPU_MODE_AUTO => _btnGpuAuto,
                                    _ => null
                                };

                                ToolStripMenuItem? liveTray = liveMode.Value switch
                                {
                                    AcerAgentClient.GPU_MODE_OPTIMUS => _trayGpuOptimus,
                                    AcerAgentClient.GPU_MODE_DISCRETE => _trayGpuDiscrete,
                                    AcerAgentClient.GPU_MODE_AUTO => _trayGpuAuto,
                                    _ => null
                                };

                                if (_currentGpuMode == null || _currentGpuMode == liveMode.Value)
                                {
                                    _currentGpuMode = liveMode.Value;
                                    if (liveBtn != null)
                                        HighlightBtn(liveBtn, ref _activeGpuModeBtn);
                                    if (liveTray != null)
                                        CheckTrayItem(liveTray, _trayGpuOptimus, _trayGpuDiscrete, _trayGpuAuto);
                                }
                                else if (_currentGpuMode != liveMode.Value)
                                {
                                    string pendingName = _currentGpuMode.Value switch
                                    {
                                        AcerAgentClient.GPU_MODE_OPTIMUS => "Optimus (Hybrid)",
                                        AcerAgentClient.GPU_MODE_DISCRETE => "Discrete GPU Only",
                                        AcerAgentClient.GPU_MODE_AUTO => "Auto (Advanced Optimus)",
                                        _ => "Selected mode"
                                    };
                                    if (_lblGpuRestartNotice != null)
                                    {
                                        _lblGpuRestartNotice.Text = $"Pending restart: {pendingName} will be active on next boot";
                                        _lblGpuRestartNotice.ForeColor = Color.FromArgb(255, 180, 50);
                                    }
                                }
                            }

                            _jelli?.PublishState();
                        }

                        if (IsHandleCreated)
                        {
                            BeginInvoke(new Action(UpdateGpuUi));
                        }
                        else
                        {
                            for (int i = 0; i < 25 && !IsHandleCreated && !IsDisposed; i++)
                                await Task.Delay(100);

                            if (IsHandleCreated && !IsDisposed)
                                BeginInvoke(new Action(UpdateGpuUi));
                        }
                    }
                    catch { }
                });
            }
            catch { }
            finally { _restoringLighting = false; _loadingPreferences = false; }
        }

        #endregion

        #region Startup Task Registration

        private const string StartupTaskName = "PredatorControl";
        private const string BootTaskName = "PredatorControlBoot";

        private static bool IsStartupEnabled()
        {
            return RunSchtasks($"/Query /TN \"{StartupTaskName}\"") == 0;
        }

        private static bool SetStartupEnabled(bool enable)
        {
            RemoveLegacyRunKey();

            if (!enable)
            {
                RunSchtasks($"/Delete /TN \"{BootTaskName}\" /F");
                return RunSchtasks($"/Delete /TN \"{StartupTaskName}\" /F") == 0 || !IsStartupEnabled();
            }

            // Tasks run with elevated rights, so they may only point at a binary in an admin-only location.
            string? taskExe = SecureLaunch.PrepareTaskBinary(out string taskExeReason);
            if (taskExe == null)
            {
                Debug.WriteLine("Startup task refused: " + taskExeReason);
                return false;
            }

            // 1. Register main interactive task: triggers immediately at user logon with 0 delay and correct working directory
            string xmlPath = Path.Combine(Path.GetTempPath(), "PredatorControlStartup.xml");
            bool logonOk = false;
            try
            {
                File.WriteAllText(xmlPath, BuildStartupTaskXml(taskExe), Encoding.Unicode);
                logonOk = RunSchtasks($"/Create /TN \"{StartupTaskName}\" /XML \"{xmlPath}\" /F") == 0;
            }
            catch { }
            finally
            {
                try { File.Delete(xmlPath); } catch { }
            }

            // 2. Register machine early boot task: triggers at system boot (before logon) as SYSTEM to lock 80% battery limit and apply saved RGB profile
            string bootXmlPath = Path.Combine(Path.GetTempPath(), "PredatorControlBoot.xml");
            try
            {
                File.WriteAllText(bootXmlPath, BuildBootTaskXml(taskExe), Encoding.Unicode);
                RunSchtasks($"/Create /TN \"{BootTaskName}\" /XML \"{bootXmlPath}\" /RU \"NT AUTHORITY\\SYSTEM\" /F");
            }
            catch { }
            finally
            {
                try { File.Delete(bootXmlPath); } catch { }
            }

            return logonOk;
        }

        private static string BuildStartupTaskXml(string exePath)
        {
            string workingDir = Path.GetDirectoryName(exePath) ?? "";
            string exe = System.Security.SecurityElement.Escape(exePath);
            string dir = System.Security.SecurityElement.Escape(workingDir);
            string userSid = WindowsIdentity.GetCurrent().User?.Value ?? "";
            string user = System.Security.SecurityElement.Escape(string.IsNullOrEmpty(userSid) ? WindowsIdentity.GetCurrent().Name : userSid);

            return $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>Starts Predator Control at logon with administrator rights.</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <UserId>{user}</UserId>
      <Delay>PT0S</Delay>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>{user}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>Parallel</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>5</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{exe}</Command>
      <Arguments>-hidden</Arguments>
      <WorkingDirectory>{dir}</WorkingDirectory>
    </Exec>
  </Actions>
</Task>";
        }

        private static string BuildBootTaskXml(string exePath)
        {
            string workingDir = Path.GetDirectoryName(exePath) ?? "";
            string exe = System.Security.SecurityElement.Escape(exePath);
            string dir = System.Security.SecurityElement.Escape(workingDir);

            return $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>Enforces Predator Control battery charge limiter at machine boot.</Description>
  </RegistrationInfo>
  <Triggers>
    <BootTrigger>
      <Enabled>true</Enabled>
    </BootTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>S-1-5-18</UserId>
      <LogonType>ServiceAccount</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>true</Hidden>
    <ExecutionTimeLimit>PT30S</ExecutionTimeLimit>
    <Priority>6</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{exe}</Command>
      <Arguments>--boot-limit</Arguments>
      <WorkingDirectory>{dir}</WorkingDirectory>
    </Exec>
  </Actions>
</Task>";
        }

        private static int RunSchtasks(string args)
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = SecureLaunch.System32("schtasks.exe"),
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
                if (p == null) return -1;
                p.WaitForExit(15000);
                return p.HasExited ? p.ExitCode : -1;
            }
            catch { return -1; }
        }

        private static void RemoveLegacyRunKey()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                key?.DeleteValue(StartupTaskName, false);
            }
            catch { }
        }

        private static void MigrateLegacyStartup()
        {
            bool hadLegacy;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
                hadLegacy = key?.GetValue(StartupTaskName) != null;
            }
            catch { return; }

            if (hadLegacy && !IsStartupEnabled())
                SetStartupEnabled(true);
            else if (hadLegacy)
                RemoveLegacyRunKey();
        }

        #endregion
    }
}
