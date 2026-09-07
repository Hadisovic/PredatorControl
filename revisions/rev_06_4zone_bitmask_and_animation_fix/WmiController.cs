using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PredatorControlApp
{
    [SupportedOSPlatform("windows")]
    public class WmiController : IDisposable
    {
        private ManagementObject? _cachedObj;
        private readonly object _lock = new();
        private bool _disposed;

        // Consecutive failure tracking — only evict cache after 2+ failures in a row
        private int _consecutiveFailures = 0;
        private const int MaxFailuresBeforeEviction = 2;
        // Pre-warm flag: avoids blocking hot path while cache is being rebuilt
        private volatile bool _cacheRebuilding = false;

        [DllImport("powrprof.dll")]
        private static extern uint PowerSetActiveOverlayScheme(Guid scheme);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool WritePrivateProfileString(string lpAppName, string? lpKeyName, string? lpString, string lpFileName);

        private static readonly Guid OVERLAY_EFFICIENCY = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
        private static readonly Guid OVERLAY_BALANCED = new("00000000-0000-0000-0000-000000000000");
        private static readonly Guid OVERLAY_PERFORMANCE = new("ded574b5-45a0-4f42-8737-46345c09c238");

        private byte _lastR = 0, _lastG = 200, _lastB = 150;
        private byte _brightness = 100;
        private byte _speed = 5;
        private byte _direction = 0;
        private int _lastMode = 3;

        // 4-zone (or 3-zone mapped) keyboard colors
        private Color[] _zoneColors = new Color[4]
        {
            Color.FromArgb(0, 200, 160),
            Color.FromArgb(0, 200, 160),
            Color.FromArgb(0, 200, 160),
            Color.FromArgb(0, 200, 160)
        };

        private byte _customCpuFanSpeed = 50;
        private byte _customGpuFanSpeed = 50;
        // Background worker queue for zero-latency instantaneous lighting updates
        private Action? _pendingLightingAction;
        private readonly object _lightingLock = new();
        private bool _isLightingWorkerRunning;


        public byte LastR => _lastR;
        public byte LastG => _lastG;
        public byte LastB => _lastB;
        public byte Brightness => _brightness;
        public byte Speed => _speed;
        public byte Direction => _direction;
        public int LastRgbMode => _lastMode;
        public Color[] ZoneColors => _zoneColors;
        public byte CustomCpuFanSpeed => _customCpuFanSpeed;
        public byte CustomGpuFanSpeed => _customGpuFanSpeed;

        private ManagementObject? GetWmiObject()
        {
            lock (_lock)
            {
                if (_disposed) return null;
                if (_cachedObj != null) return _cachedObj;
                if (_cacheRebuilding) return null; // Skip rather than block during pre-warm

                try
                {
                    using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM AcerGamingFunction");
                    using var results = searcher.Get();
                    using var enumerator = results.GetEnumerator();
                    if (enumerator.MoveNext() && enumerator.Current is ManagementObject obj)
                    {
                        _cachedObj = obj;
                        _consecutiveFailures = 0;
                    }
                }
                catch
                {
                    _cachedObj = null;
                }

                return _cachedObj;
            }
        }

        private void InvalidateCache()
        {
            lock (_lock)
            {
                _consecutiveFailures++;

                // Only evict the cache after MaxFailuresBeforeEviction consecutive failures.
                // This prevents a single transient WMI timeout from triggering an expensive
                // 200-400ms cache rebuild on the very next command.
                if (_consecutiveFailures < MaxFailuresBeforeEviction)
                    return;

                _consecutiveFailures = 0;
                try { _cachedObj?.Dispose(); } catch { }
                _cachedObj = null;
            }

            // Pre-warm the cache off the hot path so the next command is fast
            if (!_cacheRebuilding)
            {
                _cacheRebuilding = true;
                Task.Run(() =>
                {
                    try { GetWmiObject(); } catch { }
                    finally { _cacheRebuilding = false; }
                });
            }
        }

        private (bool success, ulong output) SendCommand(string method, ulong input)
        {
            lock (_lock)
            {
                try
                {
                    var obj = GetWmiObject();
                    if (obj == null) return (false, 0);

                    using var inParams = obj.GetMethodParameters(method);
                    inParams["gmInput"] = input;

                    using var outParams = obj.InvokeMethod(method, inParams, null);
                    if (outParams?["gmOutput"] == null) return (false, 0);

                    ulong result = Convert.ToUInt64(outParams["gmOutput"]);
                    bool ok = (result & 0xFF) == 0;

                    if (ok)
                    {
                        _consecutiveFailures = 0;
                        return (true, result);
                    }

                    // EC rejected the command (non-zero status byte) — retry once after 20ms
                    // This handles transient EC-busy states without meaningfully increasing
                    // latency on the success path.
                    Thread.Sleep(20);
                    using var retryIn = obj.GetMethodParameters(method);
                    retryIn["gmInput"] = input;
                    using var retryOut = obj.InvokeMethod(method, retryIn, null);
                    if (retryOut?["gmOutput"] == null) return (false, 0);
                    ulong retryResult = Convert.ToUInt64(retryOut["gmOutput"]);
                    _consecutiveFailures = 0;
                    return ((retryResult & 0xFF) == 0, retryResult);
                }
                catch
                {
                    InvalidateCache();
                    return (false, 0);
                }
            }
        }

        private bool SendLedCommand(byte[] payload)
        {
            lock (_lock)
            {
                try
                {
                    var obj = GetWmiObject();
                    if (obj == null) return false;

                    using var inParams = obj.GetMethodParameters("SetGamingKBBacklight");
                    inParams["gmInput"] = payload;

                    using var outParams = obj.InvokeMethod("SetGamingKBBacklight", inParams, null);
                    if (outParams?["gmOutput"] == null) return false;

                    ulong result = Convert.ToUInt64(outParams["gmOutput"]);
                    bool ok = (result & 0xFF) == 0;

                    if (ok) { _consecutiveFailures = 0; return true; }

                    // Single retry on EC rejection
                    Thread.Sleep(20);
                    using var retryIn = obj.GetMethodParameters("SetGamingKBBacklight");
                    retryIn["gmInput"] = payload;
                    using var retryOut = obj.InvokeMethod("SetGamingKBBacklight", retryIn, null);
                    if (retryOut?["gmOutput"] == null) return false;
                    ulong retryResult = Convert.ToUInt64(retryOut["gmOutput"]);
                    _consecutiveFailures = 0;
                    return (retryResult & 0xFF) == 0;
                }
                catch
                {
                    InvalidateCache();
                    return false;
                }
            }
        }

        private bool SendGamingRgbKbCommand(byte[] payload)
        {
            lock (_lock)
            {
                try
                {
                    var obj = GetWmiObject();
                    if (obj == null) return false;

                    using var inParams = obj.GetMethodParameters("SetGamingRgbKb");
                    if (inParams == null) return false;

                    if (inParams.Properties["gmInput"]?.IsArray == true)
                    {
                        inParams["gmInput"] = payload;
                    }
                    else
                    {
                        ulong val = 0;
                        for (int i = 0; i < Math.Min(payload.Length, 8); i++)
                            val |= ((ulong)payload[i]) << (i * 8);
                        inParams["gmInput"] = val;
                    }

                    using var outParams = obj.InvokeMethod("SetGamingRgbKb", inParams, null);
                    if (outParams?["gmOutput"] == null) return false;

                    ulong result = Convert.ToUInt64(outParams["gmOutput"]);
                    return (result & 0xFF) == 0;
                }
                catch
                {
                    InvalidateCache();
                    return false;
                }
            }
        }

        private int? GetSensorReading(ulong sensorId)
        {
            lock (_lock)
            {
                try
                {
                    var obj = GetWmiObject();
                    if (obj == null) return null;

                    using var inParams = obj.GetMethodParameters("GetGamingSysInfo");
                    inParams["gmInput"] = (ulong)(0x0001 | (sensorId << 8));

                    using var outParams = obj.InvokeMethod("GetGamingSysInfo", inParams, null);
                    if (outParams?["gmOutput"] == null) return null;

                    ulong raw = Convert.ToUInt64(outParams["gmOutput"]);
                    if ((raw & 0xFF) == 0)
                    {
                        int reading = (int)((raw >> 8) & 0xFFFF);
                        return reading > 0 ? reading : null;
                    }
                }
                catch (ManagementException) { InvalidateCache(); }
                catch (COMException) { InvalidateCache(); }
                catch { }

                return null;
            }
        }

        public (int? cpuTemp, int? gpuTemp, int? cpuFanRpm, int? gpuFanRpm) GetAllSensors(bool isPluggedIn, bool includeGpu = true)
        {
            int? cpuTemp = CpuTemp;
            int? cpuRpm = CpuFanRpm;
            int? gpuTemp = (isPluggedIn && includeGpu) ? GpuTemp : null;
            int? gpuRpm = (isPluggedIn && includeGpu) ? GpuFanRpm : null;

            return (cpuTemp, gpuTemp, cpuRpm, gpuRpm);
        }

        public int? CpuTemp => GetSensorReading(0x01);
        public int? GpuTemp => GetSensorReading(0x0A);
        public int? CpuFanRpm => GetSensorReading(0x02);
        public int? GpuFanRpm => GetSensorReading(0x06);

        public void SetPowerMode(byte mode)
        {
            SendCommand("SetGamingMiscSetting", (ulong)0x0B | ((ulong)mode << 8));
            SyncWindowsPowerMode(mode);
        }

        public void SetFanBehavior(byte mode)
        {
            SendCommand("SetGamingFanBehavior", (ulong)(0x09 | ((ulong)mode << 16) | ((ulong)mode << 22)));

            if (mode == 0x03)
                SetFanSpeed(_customCpuFanSpeed, _customGpuFanSpeed);
        }

        public bool SetFanSpeed(byte cpuSpeed, byte gpuSpeed)
        {
            _customCpuFanSpeed = cpuSpeed;
            _customGpuFanSpeed = gpuSpeed;

            var (cpuOk, _) = SendCommand("SetGamingFanSpeed", 0x01UL | ((ulong)cpuSpeed << 8));
            var (gpuOk, _) = SendCommand("SetGamingFanSpeed", 0x04UL | ((ulong)gpuSpeed << 8));
            return cpuOk && gpuOk;
        }

        public bool SetCpuFanSpeed(byte speed)
        {
            _customCpuFanSpeed = speed;
            var (ok, _) = SendCommand("SetGamingFanSpeed", 0x01UL | ((ulong)speed << 8));
            return ok;
        }

        public bool SetGpuFanSpeed(byte speed)
        {
            _customGpuFanSpeed = speed;
            var (ok, _) = SendCommand("SetGamingFanSpeed", 0x04UL | ((ulong)speed << 8));
            return ok;
        }

        #region Non-blocking Asynchronous RGB Lighting

        private void QueueLightingTask(Action action)
        {
            lock (_lightingLock)
            {
                _pendingLightingAction = action;
                if (_isLightingWorkerRunning) return;
                _isLightingWorkerRunning = true;
            }

            Task.Run(() =>
            {
                while (true)
                {
                    Action? next;
                    lock (_lightingLock)
                    {
                        next = _pendingLightingAction;
                        _pendingLightingAction = null;
                        if (next == null)
                        {
                            _isLightingWorkerRunning = false;
                            break;
                        }
                    }

                    try
                    {
                        next();
                    }
                    catch { }

                    // Inter-action pacing to prevent EC firmware buffer saturation
                    Thread.Sleep(30);
                }
            });
        }


        public void SetRgbMode(int mode, byte r, byte g, byte b, byte brightness, byte speed, byte direction)
        {
            _lastR = r; _lastG = g; _lastB = b;
            _brightness = brightness;
            _speed = speed;
            _direction = direction;
            _lastMode = mode;

            for (int i = 0; i < 4; i++)
                _zoneColors[i] = Color.FromArgb(r, g, b);

            QueueLightingTask(() => ApplyLightingModeCore(mode));
        }

        public void SetSingleZoneColor(int zoneIndex, Color color, int mode = 0)
        {
            if (zoneIndex >= 0 && zoneIndex < 4)
            {
                _zoneColors[zoneIndex] = color;
                _lastR = color.R;
                _lastG = color.G;
                _lastB = color.B;
                _lastMode = mode;
                QueueLightingTask(() =>
                {
                    if (mode == 0) // Static: update specific physical zone
                    {
                        ApplyZoneLightingCore(zoneIndex, color);

                        byte[] payload = new byte[16];
                        payload[0] = 0;
                        payload[1] = _speed;
                        payload[2] = _brightness;
                        payload[3] = 0;
                        payload[4] = (byte)(_direction + 1);
                        payload[5] = color.R;
                        payload[6] = color.G;
                        payload[7] = color.B;
                        payload[8] = 0x03;
                        payload[9] = 1;
                        SendLedCommand(payload);
                        SyncLightingProfileIni();
                    }
                    else // Animated: color applies to entire keyboard
                    {
                        for (int i = 0; i < 4; i++) _zoneColors[i] = color;
                        ApplyLightingModeCore(mode);
                    }
                });
            }
        }

        public void Set3ZoneCustomColor(int zoneIndex, Color color, int mode = 0)
        {
            _lastMode = mode;
            if (zoneIndex == 0)
            {
                _zoneColors[0] = color;
                _lastR = color.R; _lastG = color.G; _lastB = color.B;
                QueueLightingTask(() =>
                {
                    ApplyZoneLightingCore(0, color);
                    ApplyLightingModeCore(mode);
                });
            }
            else if (zoneIndex == 1)
            {
                // Middle zone covers physical zones 2 and 3 simultaneously
                _zoneColors[1] = color;
                _zoneColors[2] = color;
                QueueLightingTask(() =>
                {
                    ApplyZoneLightingCore(1, color);
                    Thread.Sleep(15);
                    ApplyZoneLightingCore(2, color);
                    ApplyLightingModeCore(mode);
                });
            }
            else if (zoneIndex == 2)
            {
                _zoneColors[3] = color;
                QueueLightingTask(() =>
                {
                    ApplyZoneLightingCore(3, color);
                    ApplyLightingModeCore(mode);
                });
            }
        }

        private void ApplyZoneLightingCore(int zoneIndex, Color color)
        {
            // SetGamingRgbKb zone bitmask: 1 << zoneIndex (1, 2, 4, 8 for physical zones 1, 2, 3, 4)
            ulong mask = 1ul << Math.Clamp(zoneIndex, 0, 3);
            ulong zonePayload = mask
                | ((ulong)color.R << 8)
                | ((ulong)color.G << 16)
                | ((ulong)color.B << 24);
            SendCommand("SetGamingRgbKb", zonePayload);
        }

        public void Set4ZoneColors(Color[] zones, int mode = 0, byte? brightness = null, byte? speed = null)
        {
            if (zones != null && zones.Length >= 4)
            {
                for (int i = 0; i < 4; i++) _zoneColors[i] = zones[i];
                _lastR = zones[0].R; _lastG = zones[0].G; _lastB = zones[0].B;
                _lastMode = mode;
                if (brightness.HasValue) _brightness = brightness.Value;
                if (speed.HasValue) _speed = speed.Value;
                QueueLightingTask(() => Apply4ZoneLightingCore(mode));
            }
        }

        public void Set3ZoneColors(Color zone1, Color zone2, Color zone3, int mode = 0)
        {
            var zones4 = new Color[] { zone1, zone2, zone2, zone3 };
            Set4ZoneColors(zones4, mode);
        }

        public void SetBrightness(byte brightness)
        {
            _brightness = brightness;
            QueueLightingTask(() => ApplyLightingModeCore(_lastMode));
        }

        public void SetSpeed(byte speed)
        {
            _speed = speed;
            QueueLightingTask(() => ApplyLightingModeCore(_lastMode));
        }

        public void SetDirection(byte direction)
        {
            _direction = direction;
            QueueLightingTask(() => ApplyLightingModeCore(_lastMode));
        }

        public void SetKeyboardColor(Color c, int mode = 0)
        {
            _lastR = c.R; _lastG = c.G; _lastB = c.B;
            _lastMode = mode;
            for (int i = 0; i < 4; i++) _zoneColors[i] = c;

            QueueLightingTask(() =>
            {
                byte[] payload = new byte[16];
                payload[0] = (byte)(mode == 8 ? 0 : mode);
                payload[1] = _speed;
                payload[2] = (byte)(mode == 8 ? 0 : _brightness);
                payload[3] = 0;
                payload[4] = (byte)(_direction + 1);
                payload[5] = c.R;
                payload[6] = c.G;
                payload[7] = c.B;
                payload[8] = 0x03;
                payload[9] = (byte)(mode == 8 ? 0 : 1);
                SendLedCommand(payload);
                SyncLightingProfileIni();
            });
        }

        public void SetStaticColor(byte r, byte g, byte b, byte brightness)
        {
            _brightness = brightness;
            SetKeyboardColor(Color.FromArgb(r, g, b), 0);
        }

        public void SetKeyboardOff()
        {
            _brightness = 0;
            _lastMode = 8;
            QueueLightingTask(() => ApplyLightingModeCore(8));
        }

        private void ApplyLightingModeCore(int mode)
        {
            byte[] payload = new byte[16];
            payload[0] = (byte)(mode == 8 ? 0 : mode);
            payload[1] = _speed;
            payload[2] = (byte)(mode == 8 ? 0 : _brightness);
            payload[3] = 0;
            payload[4] = (byte)(_direction + 1);
            payload[5] = _lastR;
            payload[6] = _lastG;
            payload[7] = _lastB;
            payload[8] = 0x03;
            payload[9] = (byte)(mode == 8 ? 0 : 1);
            SendLedCommand(payload);
            SyncLightingProfileIni();
        }

        private void Apply4ZoneLightingCore(int mode)
        {
            bool allSame = (_zoneColors[0] == _zoneColors[1] &&
                            _zoneColors[1] == _zoneColors[2] &&
                            _zoneColors[2] == _zoneColors[3]);

            if (allSame)
            {
                ulong zonePayload = 0x0Ful
                    | ((ulong)_zoneColors[0].R << 8)
                    | ((ulong)_zoneColors[0].G << 16)
                    | ((ulong)_zoneColors[0].B << 24);
                SendCommand("SetGamingRgbKb", zonePayload);
            }
            else
            {
                for (int z = 0; z < 4; z++)
                {
                    ApplyZoneLightingCore(z, _zoneColors[z]);
                    Thread.Sleep(15);
                }
            }

            byte[] payload = new byte[16];
            payload[0] = (byte)(mode == 8 ? 0 : mode);
            payload[1] = _speed;
            payload[2] = (byte)(mode == 8 ? 0 : _brightness);
            payload[3] = 0;
            payload[4] = (byte)(_direction + 1);
            payload[5] = _zoneColors[0].R;
            payload[6] = _zoneColors[0].G;
            payload[7] = _zoneColors[0].B;
            payload[8] = 0x03;
            payload[9] = (byte)(mode == 8 ? 0 : 1);
            SendLedCommand(payload);
            SyncLightingProfileIni();
        }

        private void SyncLightingProfileIni()
        {
            try
            {
                string iniPath = @"C:\ProgramData\OEM\AcerLightingService\LightingProfile\LightingProfile.ini";
                if (!File.Exists(iniPath)) return;

                string modeSection = _lastMode switch
                {
                    0 => "AcerECKeyboard Device_STATIC",
                    1 => "AcerECKeyboard Device_BREATHING",
                    2 => "AcerECKeyboard Device_NEON",
                    3 => "AcerECKeyboard Device_WAVE",
                    4 => "AcerECKeyboard Device_SHIFTING",
                    5 => "AcerECKeyboard Device_ZOOM",
                    6 => "AcerECKeyboard Device_METEOR",
                    7 => "AcerECKeyboard Device_TWINKLING",
                    _ => "AcerECKeyboard Device_STATIC"
                };

                int activeModeVal = _lastMode == 8 ? 0 : (_lastMode + 1);
                WritePrivateProfileString("AcerECKeyboard Device", "ActiveMode", activeModeVal.ToString(), iniPath);
                WritePrivateProfileString(modeSection, "brightness", _brightness.ToString(), iniPath);
                WritePrivateProfileString(modeSection, "speed", _speed.ToString(), iniPath);
                string colorHex = $"0X{_lastR:x2}{_lastG:x2}{_lastB:x2}";
                WritePrivateProfileString(modeSection, "color", colorHex, iniPath);

                for (int i = 0; i < 4; i++)
                {
                    Color c = _zoneColors[i];
                    WritePrivateProfileString("AcerECKeyboard Device_PerKeyColor", $"Key{i + 1}", $"0X{c.R:x2}{c.G:x2}{c.B:x2}", iniPath);
                }
            }
            catch { }
        }

        #endregion

        private void SyncWindowsPowerMode(byte acerMode)
        {
            try
            {
                Guid overlay = acerMode switch
                {
                    0x00 or 0x06 => OVERLAY_EFFICIENCY,
                    0x04 or 0x05 => OVERLAY_PERFORMANCE,
                    _ => OVERLAY_BALANCED
                };
                PowerSetActiveOverlayScheme(overlay);
            }
            catch { }
        }

        public bool SetBatteryChargeLimit(bool enable)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM BatteryControl");
                using var results = searcher.Get();
                using var obj = results.Cast<ManagementObject>().FirstOrDefault();
                if (obj == null) return false;

                using var inParams = obj.GetMethodParameters("SetBatteryHealthControl");
                inParams["uBatteryNo"] = (byte)1;
                inParams["uFunctionMask"] = (byte)1;
                inParams["uFunctionStatus"] = (byte)(enable ? 1 : 0);
                inParams["uReservedIn"] = new byte[] { 0, 0, 0, 0, 0 };

                using var outParams = obj.InvokeMethod("SetBatteryHealthControl", inParams, null);
                if (outParams?["uReturn"] == null) return false;

                ushort result = Convert.ToUInt16(outParams["uReturn"]);
                return result == 0;
            }
            catch
            {
                return false;
            }
        }

        public bool IsBatteryControlSupported()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM BatteryControl");
                using var results = searcher.Get();
                using var obj = results.Cast<ManagementObject>().FirstOrDefault();
                return obj != null;
            }
            catch
            {
                return false;
            }
        }

        #region Hardware Features (LCD Overdrive, Backlight Sleep, Windows Key Lock)

        public bool SetLcdOverdrive(bool enable)
        {
            var (ok, _) = SendCommand("SetGamingMiscSetting", 0x05UL | ((ulong)(enable ? 1 : 0) << 8));
            return ok;
        }

        public bool SetBacklight30s(bool enable)
        {
            byte status = (byte)(enable ? 1 : 0);
            byte[] kbPayload = new byte[8] { 0x30, 0x01, status, 0x00, 0x00, 0x00, 0x00, (byte)(0xCE - status) };
            return SendGamingRgbKbCommand(kbPayload);
        }

        public bool SetWinKeyLock(bool lockKeys)
        {
            byte val = (byte)(lockKeys ? 0 : 3);
            byte[] kbPayload = new byte[8] { 0x03, val, 0x00, 0x00, 0x00, 0x00, 0x00, (byte)(0xFC - val) };
            return SendGamingRgbKbCommand(kbPayload);
        }

        #endregion

        public void Dispose()
        {
            lock (_lock)
            {
                if (!_disposed)
                {
                    _disposed = true;
                    lock (_lightingLock)
                    {
                        _pendingLightingAction = null;
                    }
                    _cachedObj?.Dispose();
                    _cachedObj = null;
                }
            }
        }
    }
}
