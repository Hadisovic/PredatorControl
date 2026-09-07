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

        [DllImport("powrprof.dll")]
        private static extern uint PowerSetActiveOverlayScheme(Guid scheme);

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

                try
                {
                    using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM AcerGamingFunction");
                    using var results = searcher.Get();
                    using var enumerator = results.GetEnumerator();
                    if (enumerator.MoveNext() && enumerator.Current is ManagementObject obj)
                    {
                        _cachedObj = obj;
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
                try { _cachedObj?.Dispose(); } catch { }
                _cachedObj = null;
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
                    return ((result & 0xFF) == 0, result);
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
                    return (result & 0xFF) == 0;
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

        public (int? cpuTemp, int? gpuTemp, int? cpuFanRpm, int? gpuFanRpm) GetAllSensors(bool isPluggedIn)
        {
            int? cpuTemp = CpuTemp;
            int? cpuRpm = CpuFanRpm;
            int? gpuTemp = isPluggedIn ? GpuTemp : null;
            int? gpuRpm = isPluggedIn ? GpuFanRpm : null;

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
                _lastR = _zoneColors[0].R;
                _lastG = _zoneColors[0].G;
                _lastB = _zoneColors[0].B;
                _lastMode = mode;
                QueueLightingTask(() => ApplyMultiZoneLightingCore(1ul << zoneIndex, color));
            }
        }

        public void Set3ZoneCustomColor(int zoneIndex, Color color, int mode = 0)
        {
            _lastMode = mode;
            if (zoneIndex == 0)
            {
                _zoneColors[0] = color;
                _lastR = color.R; _lastG = color.G; _lastB = color.B;
                QueueLightingTask(() => ApplyMultiZoneLightingCore(1ul << 0, color));
            }
            else if (zoneIndex == 1)
            {
                // Middle zone covers physical zones 2 and 3 simultaneously
                _zoneColors[1] = color;
                _zoneColors[2] = color;
                QueueLightingTask(() => ApplyMultiZoneLightingCore((1ul << 1) | (1ul << 2), color));
            }
            else if (zoneIndex == 2)
            {
                // Right zone is physical zone 4
                _zoneColors[3] = color;
                QueueLightingTask(() => ApplyMultiZoneLightingCore(1ul << 3, color));
            }
        }

        private void ApplyMultiZoneLightingCore(ulong mask, Color color)
        {
            ulong zonePayload = 0x06ul | (mask << 8)
                | ((ulong)color.R << 16)
                | ((ulong)color.G << 24)
                | ((ulong)color.B << 32);
            SendCommand("SetGamingLEDBehavior", zonePayload);
        }

        public void Set4ZoneColors(Color[] zones, int mode = 0)
        {
            if (zones.Length >= 4)
            {
                for (int i = 0; i < 4; i++) _zoneColors[i] = zones[i];
                _lastR = zones[0].R; _lastG = zones[0].G; _lastB = zones[0].B;
                _lastMode = mode;
                QueueLightingTask(() => Apply4ZoneLightingCore(mode));
            }
        }

        public void Set3ZoneColors(Color zone1, Color zone2, Color zone3, int mode = 0)
        {
            // For 3-zone keyboards, map Zone 1 (Left), Zone 2 (Center -> zones 2 & 3), Zone 3 (Right -> zone 4)
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
                SendCommand("SetGamingLEDBehavior", 0x07ul);

                ulong zonePayload = 0x06ul | (0x0Ful << 8)
                    | ((ulong)c.R << 16) | ((ulong)c.G << 24) | ((ulong)c.B << 32);
                SendCommand("SetGamingLEDBehavior", zonePayload);

                byte[] payload = new byte[16];
                payload[0] = (byte)mode;
                payload[1] = _speed;
                payload[2] = _brightness;
                payload[3] = _direction;
                payload[5] = c.R;
                payload[6] = c.G;
                payload[7] = c.B;
                payload[9] = 1;
                SendLedCommand(payload);
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
            QueueLightingTask(() => ApplyLightingModeCore(0));
        }

        private void ApplyLightingModeCore(int mode)
        {
            SendCommand("SetGamingLEDBehavior", 0x07ul);

            if (mode != 2 && mode != 3)
            {
                ulong zonePayload = 0x06ul | (0x0Ful << 8)
                    | ((ulong)_lastR << 16) | ((ulong)_lastG << 24) | ((ulong)_lastB << 32);
                SendCommand("SetGamingLEDBehavior", zonePayload);
            }

            byte[] payload = new byte[16];
            payload[0] = (byte)mode;
            payload[1] = _speed;
            payload[2] = _brightness;
            payload[3] = _direction;
            payload[5] = _lastR;
            payload[6] = _lastG;
            payload[7] = _lastB;
            payload[9] = 1;
            SendLedCommand(payload);
        }

        private void Apply4ZoneLightingCore(int mode)
        {
            // Group zones with matching colors to send the absolute minimum number of WMI commands
            var colorGroups = new Dictionary<Color, ulong>();
            for (int z = 0; z < 4; z++)
            {
                var c = _zoneColors[z];
                ulong mask = 1ul << z;
                if (colorGroups.TryGetValue(c, out ulong existingMask))
                    colorGroups[c] = existingMask | mask;
                else
                    colorGroups[c] = mask;
            }

            bool first = true;
            foreach (var kvp in colorGroups)
            {
                if (!first)
                {
                    Thread.Sleep(35); // Pacing for EC firmware buffer
                }
                first = false;

                ApplyMultiZoneLightingCore(kvp.Value, kvp.Key);
            }
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