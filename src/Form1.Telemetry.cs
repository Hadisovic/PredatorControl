// ====================================================================================================
// PREDATOR CONTROL · TELEMETRY LOOP, THERMAL FAN CURVES, POWER RULES & GAME SYNC
// File: src/Form1.Telemetry.cs
// ====================================================================================================

using System.Runtime.Versioning;
using Microsoft.Win32;

namespace PredatorControlApp
{
    [SupportedOSPlatform("windows")]
    public partial class Form1
    {
        #region Game Sync Handlers

        private DashboardSnapshot CaptureCurrentState()
        {
            return new DashboardSnapshot
            {
                PowerMode = GetCurrentPowerByte(),
                FanMode = GetCurrentFanByte(),
                CpuFanSpeed = _cpuFanSlider.Value,
                GpuFanSpeed = _gpuFanSlider.Value,
                FanCurveWasEnabled = _fanCurveEnabled,
                RefreshRate = DisplayCcdController.GetCurrentRefreshRate(_internalDisplayGdiName),
                BatteryLimit = _switchBatteryLimit.Checked ? 1 : 0,
                RgbMode = SelectedRgbMode,
                RgbBrightness = _brightnessSlider.Value,
                RgbSpeed = _speedSlider.Value,
                RgbR = _wmi.LastR,
                RgbG = _wmi.LastG,
                RgbB = _wmi.LastB,
                WasWinKeyLocked = _switchWinKeyLock?.Checked ?? false,
                WasLcdOverdriveEnabled = _switchLcdOverdrive?.Checked ?? true
            };
        }

        private byte GetCurrentPowerByte()
        {
            return PowerControls().Where(c => c.Button != null && c.Button == _activePowerBtn).Select(c => c.Mode).FirstOrDefault(UsesWindowsPowerPlans ? (byte)255 : (byte)1);
        }

        private byte GetCurrentFanByte()
        {
            if (_activeFanBtn == _btnMaxFan) return 0x02;
            if (_activeFanBtn == _btnCustomFan) return 0x03;
            return 0x01;
        }

        private PredatorButton PowerByteToBtn(byte mode) => mode switch
        {
            0x00 => _btnQuiet,
            0x04 => _btnPerform,
            0x05 => _btnTurbo,
            0x06 => _btnEco,
            0x02 => _btnEco,
            _ => _btnBalanced
        };

        private PredatorButton FanByteToBtn(byte mode) => mode switch
        {
            0x02 => _btnMaxFan,
            0x03 => _btnCustomFan,
            _ => _btnAutoFan
        };

        private async void OnGameDetected(GameProfile profile)
        {
            if (InvokeRequired) { Invoke(() => OnGameDetected(profile)); return; }

            _jelli?.PauseColorSync();
            _isGameSyncOverriding = true;
            try { await ApplyGameProfile(profile); }
            finally { _isGameSyncOverriding = false; ApplyCapabilityUi(); }
        }

        private async Task ApplyGameProfile(GameProfile profile)
        {
            _lblGameSyncStatus.Text = $"Active \u2014 {profile.DisplayName}";

            _gameSync.SetPreGameSnapshot(CaptureCurrentState());

            if (_wmi.Capabilities.PowerModes.Any(m => m.Value == profile.PowerMode))
                ApplyPowerMode(profile.PowerMode, PowerByteToBtn(profile.PowerMode));
            ApplyFanMode(profile.FanMode, FanByteToBtn(profile.FanMode));

            if (profile.FanMode == 0x03 && _wmi.Capabilities.CanWrite(HardwareFeature.FanManual))
            {
                _fanCurveEnabled = false;
                HighlightBtn(_btnFixedSpeed, ref _activeCustomSubBtn);
                _cpuFanSlider.Enabled = true;
                _gpuFanSlider.Enabled = true;

                int cpuSpeed = profile.CpuFanSpeed >= 10 ? Math.Clamp(profile.CpuFanSpeed, 10, 100) : _cpuFanSlider.Value;
                int gpuSpeed = profile.GpuFanSpeed >= 10 ? Math.Clamp(profile.GpuFanSpeed, 10, 100) : _gpuFanSlider.Value;
                if (profile.CpuFanSpeed >= 10)
                {
                    _cpuFanSlider.Value = cpuSpeed;
                    _lblCpuFanSpeedHdr.Text = $"CPU FAN: {cpuSpeed}%";
                }
                if (profile.GpuFanSpeed >= 10)
                {
                    _gpuFanSlider.Value = gpuSpeed;
                    _lblGpuFanSpeedHdr.Text = $"GPU FAN: {gpuSpeed}%";
                }
                _ = Task.Run(() => _wmi.SetFanSpeed((byte)cpuSpeed, (byte)gpuSpeed));
            }

            if (profile.RefreshRate > 0)
                ApplyDisplayMode(profile.RefreshRate, (profile.RefreshRate <= 60 || _btnMaxHz == null) ? _btn60Hz : _btnMaxHz);

            if (profile.BatteryLimit >= 0)
                ApplyBatteryLimit(profile.BatteryLimit == 1);

            if (profile.LockWinKey >= 0)
            {
                bool lockKeys = profile.LockWinKey == 1;
                if (_switchWinKeyLock != null) _switchWinKeyLock.Checked = lockKeys;
                PredatorKeyHook.SetWinKeyLocked(lockKeys);
                _ = Task.Run(() => { try { _wmi.SetWinKeyLock(lockKeys); } catch { } });
            }

            if (profile.LcdOverdrive >= 0)
            {
                bool enableLcd = profile.LcdOverdrive == 1;
                if (_switchLcdOverdrive != null) _switchLcdOverdrive.Checked = enableLcd;
                _ = Task.Run(() => { try { _wmi.SetLcdOverdrive(enableLcd); } catch { } });
            }

            await Task.Delay(500);
            if (IsDisposed) return;

            if (_wmi.Keyboard.SupportsRgb && _rgbModeValues.Contains(profile.RgbMode))
            {
                int mode = Math.Clamp(profile.RgbMode, 0, 8);
                int brightVal = profile.RgbBrightness >= 0 ? Math.Clamp(profile.RgbBrightness, 0, 100) : _brightnessSlider.Value;
                int speedVal = profile.RgbSpeed >= 0 ? Math.Clamp(profile.RgbSpeed, 1, 100) : _speedSlider.Value;
                byte bright = (byte)brightVal;
                byte speed = profile.RgbSpeed >= 0 ? (byte)Math.Clamp(Math.Round(speedVal * 9.0 / 100.0), 1, 9) : GetMappedSpeed();
                byte r = profile.RgbR >= 0 ? (byte)Math.Clamp(profile.RgbR, 0, 255) : _wmi.LastR;
                byte g = profile.RgbG >= 0 ? (byte)Math.Clamp(profile.RgbG, 0, 255) : _wmi.LastG;
                byte b = profile.RgbB >= 0 ? (byte)Math.Clamp(profile.RgbB, 0, 255) : _wmi.LastB;

                _wmi.SetRgbMode(mode, r, g, b, bright, speed, 0);
                if (SelectedRgbMode != mode)
                    SelectRgbMode(mode);
                _brightnessSlider.Value = brightVal;
                _speedSlider.Value = speedVal;
                CheckRgbTrayFromMode(mode);
                UpdateRgbControlsState(mode);
            }
        }

        private async void OnGameExited(DashboardSnapshot snap)
        {
            if (InvokeRequired) { Invoke(() => OnGameExited(snap)); return; }

            _lblGameSyncStatus.Text = "Active \u2014 Monitoring";

            _isGameSyncOverriding = true;
            try { await RestoreSnapshot(snap); }
            finally { _isGameSyncOverriding = false; ApplyCapabilityUi(); }
        }

        private async Task RestoreSnapshot(DashboardSnapshot snap)
        {
            if (_wmi.Capabilities.PowerModes.Any(m => m.Value == snap.PowerMode))
                ApplyPowerMode(snap.PowerMode, PowerByteToBtn(snap.PowerMode));
            ApplyFanMode(snap.FanMode, FanByteToBtn(snap.FanMode));

            if (snap.FanMode == 0x03 && _wmi.Capabilities.CanWrite(HardwareFeature.FanManual))
            {
                if (snap.FanCurveWasEnabled)
                {
                    _fanCurveEnabled = true;
                    HighlightBtn(_btnFanCurve, ref _activeCustomSubBtn);
                    _cpuFanSlider.Enabled = false;
                    _gpuFanSlider.Enabled = false;
                }
                else
                {
                    _fanCurveEnabled = false;
                    HighlightBtn(_btnFixedSpeed, ref _activeCustomSubBtn);
                    _cpuFanSlider.Enabled = true;
                    _gpuFanSlider.Enabled = true;
                    int cpuSpeed = Math.Clamp(snap.CpuFanSpeed, 10, 100);
                    int gpuSpeed = Math.Clamp(snap.GpuFanSpeed, 10, 100);
                    _cpuFanSlider.Value = cpuSpeed;
                    _gpuFanSlider.Value = gpuSpeed;
                    _lblCpuFanSpeedHdr.Text = $"CPU FAN: {cpuSpeed}%";
                    _lblGpuFanSpeedHdr.Text = $"GPU FAN: {gpuSpeed}%";
                    _ = Task.Run(() => _wmi.SetFanSpeed((byte)cpuSpeed, (byte)gpuSpeed));
                }
            }

            if (snap.RefreshRate > 0)
                ApplyDisplayMode(snap.RefreshRate, (snap.RefreshRate <= 60 || _btnMaxHz == null) ? _btn60Hz : _btnMaxHz);

            ApplyBatteryLimit(snap.BatteryLimit == 1);

            if (_switchWinKeyLock != null)
            {
                _switchWinKeyLock.Checked = snap.WasWinKeyLocked;
                PredatorKeyHook.SetWinKeyLocked(snap.WasWinKeyLocked);
                _ = Task.Run(() => { try { _wmi.SetWinKeyLock(snap.WasWinKeyLocked); } catch { } });
            }

            if (_switchLcdOverdrive != null)
            {
                _switchLcdOverdrive.Checked = snap.WasLcdOverdriveEnabled;
                _ = Task.Run(() => { try { _wmi.SetLcdOverdrive(snap.WasLcdOverdriveEnabled); } catch { } });
            }

            await Task.Delay(500);
            if (IsDisposed) return;

            if (!_wmi.Keyboard.SupportsRgb || !_rgbModeValues.Contains(snap.RgbMode)) return;
            int rgbMode = Math.Clamp(snap.RgbMode, 0, 8);
            int bright = Math.Clamp(snap.RgbBrightness, 0, 100);
            int speed = Math.Clamp(snap.RgbSpeed, 1, 100);

            _wmi.SetRgbMode(rgbMode,
                            (byte)Math.Clamp(snap.RgbR, 0, 255),
                            (byte)Math.Clamp(snap.RgbG, 0, 255),
                            (byte)Math.Clamp(snap.RgbB, 0, 255),
                            (byte)bright,
                            (byte)Math.Clamp(Math.Round(speed * 9.0 / 100.0), 1, 9), 0);
            if (SelectedRgbMode != rgbMode)
                SelectRgbMode(rgbMode);
            _brightnessSlider.Value = bright;
            _speedSlider.Value = speed;
            CheckRgbTrayFromMode(rgbMode);
            UpdateRgbControlsState(rgbMode);
        }

        #endregion

        #region Telemetry & Power Rules

        private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.Resume) return;
            if (_isClosing || IsDisposed || !IsHandleCreated) return;

            try { BeginInvoke(new Action(async () => await ResyncAfterResume())); }
            catch { }
        }

        private async Task ResyncAfterResume()
        {
            if (_isResyncing) return;
            _isResyncing = true;

            try
            {
                _isPluggedIn = null;
                _lastCurveCpuSpeed = -1;
                _lastCurveGpuSpeed = -1;

                var snap = CaptureCurrentState();
                snap.RefreshRate = _activeDisplayBtn == _btn60Hz ? 60 : _maxHz;

                // Immediately re-enforce battery limit register upon waking up
                if (_wmi.IsBatteryControlSupported())
                {
                    _ = Task.Run(() => _wmi.SetBatteryChargeLimit(snap.BatteryLimit == 1));
                }

                await Task.Delay(2000);
                if (_isClosing || IsDisposed) return;

                await RestoreSnapshot(snap);
            }
            catch (Exception ex) { Program.Report(ex, false); }
            finally
            {
                _pendingPluggedIn = null;
                _powerLineStableTicks = 0;
                _isResyncing = false;
            }
        }

        internal static bool? DebouncePowerLine(PowerLineStatus line, bool? current, ref bool? pending, ref int ticks)
        {
            if (line == PowerLineStatus.Unknown)
            {
                pending = null;
                ticks = 0;
                return current;
            }

            bool pluggedIn = line == PowerLineStatus.Online;

            if (pending != pluggedIn)
            {
                pending = pluggedIn;
                ticks = 1;
            }
            else if (ticks < 2)
            {
                ticks++;
            }

            return ticks >= 2 ? pluggedIn : current;
        }

        // ============================================================================================
        // SECTION 4: 1Hz ZERO-ALLOCATION TELEMETRY LOOP & SENSOR GAUGE UPDATES
        // ============================================================================================

        private void OnTelemetryReceived(TelemetrySnapshot snap)
        {
            _lastTelemetryTick = Environment.TickCount64;
            _jelliTelemetry = snap;
            bool? confirmed = DebouncePowerLine(snap.PowerLine, _isPluggedIn, ref _pendingPluggedIn, ref _powerLineStableTicks);

            if (confirmed != _isPluggedIn && !_isResyncing)
            {
                try { ApplyPowerRules(confirmed == true); }
                catch (Exception ex) { Program.Report(ex, false); }
                finally { _isPluggedIn = confirmed; }
            }

            _cpuTemp = snap.CpuTemp;
            _gpuTemp = snap.GpuTemp;
            _cpuFanRpm = snap.CpuFanRpm;
            _gpuFanRpm = snap.GpuFanRpm;
            _auxFanRpm = snap.AuxFanRpm;

            // Zero-allocation cached debounced telemetry updates (no GC allocations on idle)
            if (_prevCpuTemp != _cpuTemp)
            {
                _lblCpuTemp.Text = FormatTemp(_cpuTemp);
                _lblCpuTemp.ForeColor = TempColor(_cpuTemp ?? 0);
                _prevCpuTemp = _cpuTemp;
            }
            if (_prevGpuTemp != _gpuTemp)
            {
                _lblGpuTemp.Text = FormatTemp(_gpuTemp);
                _lblGpuTemp.ForeColor = TempColor(_gpuTemp ?? 0);
                _prevGpuTemp = _gpuTemp;
            }

            if (_prevCpuFanRpm != _cpuFanRpm)
            {
                _lblCpuRpm.Text = _cpuFanRpm.HasValue ? $"{_cpuFanRpm.Value} RPM" : "N/A";
                _prevCpuFanRpm = _cpuFanRpm;
            }
            if (_prevGpuFanRpm != _gpuFanRpm)
            {
                _lblGpuRpm.Text = _gpuFanRpm.HasValue ? $"{_gpuFanRpm.Value} RPM" : "N/A";
                _prevGpuFanRpm = _gpuFanRpm;
            }
            if (_lblAuxRpm != null && _prevAuxFanRpm != _auxFanRpm)
            {
                _lblAuxRpm.Text = _auxFanRpm.HasValue ? $"{_auxFanRpm.Value} RPM" : "N/A";
                _prevAuxFanRpm = _auxFanRpm;
            }

            _trayIcon.Text = $"Predator Control\nCPU: {FormatTemp(_cpuTemp)}  GPU: {FormatTemp(_gpuTemp)}";

            if (_fanCurveForm != null && !_fanCurveForm.IsDisposed)
                _fanCurveForm.UpdateTemps(_cpuTemp ?? 0, _gpuTemp ?? 0);

            if (_overlayForm != null && !_overlayForm.IsDisposed && _overlayForm.Visible)
                _overlayForm.UpdateSnapshot(snap);

            _telemetryTickCount++;
            if (_telemetryTickCount % 30 == 0)
            {
                UpdateBatteryHealthUI(forceRefresh: false);
                _ = Task.Run(() => _wmi.RefreshServiceAvailability()).ContinueWith(_ => SafeInvoke(ApplyCapabilityUi));
            }

            ApplyFanCurve();
            EnforceManualThermalFloor();
        }

        private (int Cpu, int Gpu)? _manualFloorApplied;
        private int _fanCurveWriteInFlight;

        // Manual fan speeds have no temperature feedback of their own: raise them while hot, restore the slider values after.
        private void EnforceManualThermalFloor()
        {
            if (_fanCurveEnabled || GetCurrentFanByte() != 0x03 || !_wmi.Capabilities.CanWrite(HardwareFeature.FanManual))
            {
                _manualFloorApplied = null;
                return;
            }
            if (FanCurveSafety.MustRecover(_cpuTemp, _gpuTemp, Environment.TickCount64 - _lastTelemetryTick)) return;

            int floor = FanCurveSafety.FloorSpeed(_cpuTemp, _gpuTemp);
            int cpu = _cpuFanSlider.Value, gpu = _gpuFanSlider.Value;
            var desired = floor > 0 ? (Cpu: Math.Max(cpu, floor), Gpu: Math.Max(gpu, floor)) : (Cpu: cpu, Gpu: gpu);
            bool engaged = floor > 0 && (desired.Cpu != cpu || desired.Gpu != gpu);
            if ((engaged || _manualFloorApplied.HasValue) && _manualFloorApplied != desired)
            {
                if (Interlocked.CompareExchange(ref _fanCurveWriteInFlight, 1, 0) != 0) return;
                _ = Task.Run(() =>
                {
                    try
                    {
                        if (_wmi.SetFanSpeed((byte)desired.Cpu, (byte)desired.Gpu))
                            _manualFloorApplied = engaged ? desired : null;
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _fanCurveWriteInFlight, 0);
                    }
                });
            }
        }

        private void ApplyFanCurve()
        {
            if (!_fanCurveEnabled || !_wmi.Capabilities.CanWrite(HardwareFeature.FanCurve)) return;
            if (GetCurrentFanByte() != 0x03 && !_curveAwaitingSensors) return;
            if (FanCurveSafety.MustRecover(_cpuTemp, _gpuTemp, Environment.TickCount64 - _lastTelemetryTick))
            {
                _curveAwaitingSensors = true;
                _cpuTempHistory.Clear(); _gpuTempHistory.Clear();
                _ = Task.Run(() => _wmi.RecoverAutomaticFans());
                _lastCurveCpuSpeed = _lastCurveGpuSpeed = _lastCurveAuxSpeed = -1;
                _lblFanStatus.Text = "Sensor unavailable · requested firmware Auto";
                return;
            }

            if (_curveAwaitingSensors)
            {
                if (Interlocked.CompareExchange(ref _fanCurveWriteInFlight, 1, 0) != 0) return;
                _ = Task.Run(() =>
                {
                    try
                    {
                        var recovered = _wmi.SetFanBehavior(3);
                        if (recovered.Accepted)
                        {
                            SafeInvoke(() =>
                            {
                                _curveAwaitingSensors = false;
                                HighlightBtn(_btnCustomFan, ref _activeFanBtn);
                            });
                        }
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _fanCurveWriteInFlight, 0);
                    }
                });
                return;
            }

            // Rolling 3-sample average for temperature inputs to avoid micro-spikes
            if (_cpuTemp.HasValue && _cpuTemp.Value > 0)
            {
                _cpuTempHistory.Enqueue(_cpuTemp.GetValueOrDefault());
                if (_cpuTempHistory.Count > 3) _cpuTempHistory.Dequeue();
            }
            if (_gpuTemp.HasValue && _gpuTemp.Value > 0)
            {
                _gpuTempHistory.Enqueue(_gpuTemp.GetValueOrDefault());
                if (_gpuTempHistory.Count > 3) _gpuTempHistory.Dequeue();
            }

            int avgCpuTemp = _cpuTempHistory.Count > 0 ? (int)Math.Round(_cpuTempHistory.Average()) : (_cpuTemp ?? 0);
            int avgGpuTemp = _gpuTempHistory.Count > 0 ? (int)Math.Round(_gpuTempHistory.Average()) : (_gpuTemp ?? 0);

            int thermalFloor = FanCurveSafety.FloorSpeed(_cpuTemp, _gpuTemp);
            int targetCpuSpeed = Math.Clamp(Math.Max(InterpolateCurve(_cpuCurvePoints, Math.Max(avgCpuTemp, _cpuTemp.GetValueOrDefault())), thermalFloor), 10, 100);
            int targetGpuSpeed = Math.Clamp(Math.Max(InterpolateCurve(_gpuCurvePoints, Math.Max(avgGpuTemp, _gpuTemp.GetValueOrDefault())), thermalFloor), 10, 100);

            long now = Environment.TickCount64;

            // CPU Fan Hysteresis: instant ramp-up when hot, 3s hold time before stepping down
            int cpuSpeed = targetCpuSpeed;
            if (targetCpuSpeed < _lastCurveCpuSpeed)
            {
                if (now - _lastCpuDownstepTick < 3000)
                {
                    cpuSpeed = _lastCurveCpuSpeed;
                }
                else
                {
                    _lastCpuDownstepTick = now;
                }
            }
            else
            {
                _lastCpuDownstepTick = now;
            }

            // GPU Fan Hysteresis: instant ramp-up when hot, 3s hold time before stepping down
            int gpuSpeed = targetGpuSpeed;
            if (targetGpuSpeed < _lastCurveGpuSpeed)
            {
                if (now - _lastGpuDownstepTick < 3000)
                {
                    gpuSpeed = _lastCurveGpuSpeed;
                }
                else
                {
                    _lastGpuDownstepTick = now;
                }
            }
            else
            {
                _lastGpuDownstepTick = now;
            }

            bool cpuOrGpuChanged = cpuSpeed != _lastCurveCpuSpeed || gpuSpeed != _lastCurveGpuSpeed;
            if (cpuOrGpuChanged)
            {
                _cpuFanSlider.Value = Math.Clamp(cpuSpeed, 10, 100);
                _lblCpuFanSpeedHdr.Text = $"CPU FAN: {cpuSpeed}%";
                _gpuFanSlider.Value = Math.Clamp(gpuSpeed, 10, 100);
                _lblGpuFanSpeedHdr.Text = $"GPU FAN: {gpuSpeed}%";
            }

            int? newAuxSpeed = null;
            if (_wmi.HasAuxFan)
            {
                int maxTemp = Math.Max(avgCpuTemp, avgGpuTemp);
                int targetAuxSpeed = Math.Clamp(InterpolateCurve(_gpuCurvePoints, maxTemp), 10, 100);
                int auxSpeed = targetAuxSpeed;

                if (targetAuxSpeed < _lastCurveAuxSpeed)
                {
                    if (now - _lastAuxDownstepTick < 3000)
                    {
                        auxSpeed = _lastCurveAuxSpeed;
                    }
                    else
                    {
                        _lastAuxDownstepTick = now;
                    }
                }
                else
                {
                    _lastAuxDownstepTick = now;
                }

                if (auxSpeed != _lastCurveAuxSpeed)
                {
                    newAuxSpeed = auxSpeed;
                    if (_auxFanSlider != null)
                    {
                        _auxFanSlider.Value = Math.Clamp(auxSpeed, 10, 100);
                        if (_lblAuxFanSpeedHdr != null)
                            _lblAuxFanSpeedHdr.Text = $"AUX FAN: {auxSpeed}%";
                    }
                }
            }

            if ((cpuOrGpuChanged || newAuxSpeed.HasValue) && Interlocked.CompareExchange(ref _fanCurveWriteInFlight, 1, 0) == 0)
            {
                int dispatchCpu = cpuSpeed, dispatchGpu = gpuSpeed;
                int? dispatchAux = newAuxSpeed;
                _ = Task.Run(() =>
                {
                    try
                    {
                        if (cpuOrGpuChanged && _wmi.SetFanSpeed((byte)dispatchCpu, (byte)dispatchGpu))
                        {
                            _lastCurveCpuSpeed = dispatchCpu;
                            _lastCurveGpuSpeed = dispatchGpu;
                        }
                        if (dispatchAux.HasValue && _wmi.SetAuxFanSpeed((byte)dispatchAux.Value))
                        {
                            _lastCurveAuxSpeed = dispatchAux.Value;
                        }
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _fanCurveWriteInFlight, 0);
                    }
                });
            }
        }

        private static int InterpolateCurve(List<Point> curve, int temp)
        {
            if (curve.Count == 0) return 50;
            if (temp <= curve[0].X) return curve[0].Y;
            if (temp >= curve[^1].X) return curve[^1].Y;

            for (int i = 0; i < curve.Count - 1; i++)
            {
                if (temp >= curve[i].X && temp <= curve[i + 1].X)
                {
                    float span = curve[i + 1].X - curve[i].X;
                    if (span == 0) return curve[i].Y;
                    float t = (temp - curve[i].X) / span;
                    return (int)Math.Round(curve[i].Y + t * (curve[i + 1].Y - curve[i].Y));
                }
            }
            return curve[^1].Y;
        }

        private void ApplyPowerRules(bool pluggedIn, bool applyPreference = true)
        {
            foreach (var c in PowerControls())
            {
                bool allowed = _wmi.Capabilities.AllowsPower(c.Mode, pluggedIn);
                c.Button.Enabled = allowed;
                if (c.Tray != null) c.Tray.Enabled = allowed;
            }
            if (_loadingPreferences || !applyPreference)
            {
                _jelli?.PublishState();
                return;
            }
            var values = pluggedIn ? _acModeValues : _batteryModeValues;
            int index = pluggedIn ? _cboAcProfile.SelectedIndex : _cboBatteryProfile.SelectedIndex;
            if (index > 0 && index < values.Length && _wmi.Capabilities.AllowsPower(values[index], pluggedIn))
            {
                ApplyPowerMode(values[index], PowerByteToBtn(values[index]));
                return;
            }
            byte currentMode = GetCurrentPowerByte();
            if (!_wmi.Capabilities.AllowsPower(currentMode, pluggedIn) && _wmi.Capabilities.AllowsPower(1, pluggedIn))
            {
                ApplyPowerMode(1, PowerByteToBtn(1), persistPreference: false);
                return;
            }
            _jelli?.PublishState();
        }

        private static Color TempColor(int temp) => temp switch
        {
            <= 0 => Color.FromArgb(107, 114, 128),
            < 55 => Color.FromArgb(0, 200, 160),
            < 72 => Color.FromArgb(255, 220, 50),
            < 87 => Color.FromArgb(255, 140, 0),
            _ => Color.FromArgb(255, 60, 60)
        };

        #endregion
    }
}
