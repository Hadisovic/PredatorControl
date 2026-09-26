using System;
using System.Drawing;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PredatorControlApp
{
    /// <summary>
    /// Lightweight, asynchronous IPC client for Acer OEM hardware services:
    /// - Primary: AcerAgentService TCP socket at 127.0.0.1:46933
    /// - Fallback: Named pipe \\.\pipe\predatorsense_agent_service_
    /// 
    /// Dispatches hardware commands:
    /// - OPERATING_MODE: Quiet (0), Balanced (1), Performance (4), Turbo (5), Eco (6)
    /// - FAN_CONTROL: Auto (0), Max (1), Custom (2)
    /// - GPU_MODE: Optimus (0), Discrete (1), Auto (2)
    /// - LCD_OVERDRIVE: Enable (1), Disable (0)
    /// - BACKLIGHT_30S: Enable (1), Disable (0)
    /// - BOOT_SOUND: Enable (1), Disable (0)
    /// - WIN_KEY: Lock (1), Unlock (0)
    /// - COOL_BOOST: Enable (1), Disable (0)
    /// </summary>
    public static class AcerAgentClient
    {
        private const string Host = "127.0.0.1";
        private const int TcpPort = 46933;
        private static readonly string[] PipeCandidates =
        {
            "nitrosense_hardware_service_",
            "nitrosense_agent_service_",
            "predatorsense_agent_service_",
            "predatorsense_hardware_service_",
            "systemmonitoring_hardware_service_"
        };
        private static readonly byte[] MagicBytes = Encoding.ASCII.GetBytes("ACER");

        public const uint CMD_INITIALIZATION = 0;
        public const uint CMD_GET_MONITOR_DATA = 10;
        public const uint CMD_GET_UPDATED_DATA = 20;
        public const uint CMD_GET_CONNECTED_DEVICE = 30;
        public const uint CMD_SET_DEVICE_DATA = 100;

        public const int GPU_MODE_OPTIMUS = 0;
        public const int GPU_MODE_DISCRETE = 1;
        public const int GPU_MODE_AUTO = 2;

        private static readonly SemaphoreSlim _sendLock = new(1, 1);

        /// <summary>
        /// Sends a command to Acer OEM Services (AcerAgentService / NitroSenseService) and returns response.
        /// Resilient: gracefully handles disconnected service without exceptions.
        /// </summary>
        public static async Task<string?> SendCommandAsync(uint packetId, string jsonPayload, int timeoutMs = 1500)
        {
            if (!await _sendLock.WaitAsync(timeoutMs))
                return null;

            try
            {
                // Attempt 1: TCP Socket to AcerAgentService (127.0.0.1:46933)
                try
                {
                    using var cts = new CancellationTokenSource(timeoutMs);
                    using var tcp = new TcpClient();
                    
                    var connectTask = tcp.ConnectAsync(Host, TcpPort);
                    var delayTask = Task.Delay(timeoutMs, cts.Token);
                    if (await Task.WhenAny(connectTask, delayTask) == connectTask && tcp.Connected)
                    {
                        using var stream = tcp.GetStream();
                        stream.ReadTimeout = timeoutMs;
                        stream.WriteTimeout = timeoutMs;

                        byte[] idBytes = BitConverter.GetBytes(packetId);
                        byte[] payloadBytes = Encoding.UTF8.GetBytes(jsonPayload);

                        byte[] fullPacket = new byte[MagicBytes.Length + idBytes.Length + payloadBytes.Length];
                        Buffer.BlockCopy(MagicBytes, 0, fullPacket, 0, MagicBytes.Length);
                        Buffer.BlockCopy(idBytes, 0, fullPacket, MagicBytes.Length, idBytes.Length);
                        Buffer.BlockCopy(payloadBytes, 0, fullPacket, MagicBytes.Length + idBytes.Length, payloadBytes.Length);

                        await stream.WriteAsync(fullPacket, cts.Token);
                        await stream.FlushAsync(cts.Token);

                        byte[] buffer = new byte[8192];
                        int bytesRead = await stream.ReadAsync(buffer, cts.Token);
                        if (bytesRead > 0)
                        {
                            if (bytesRead > 8 && buffer[0] == 'A' && buffer[1] == 'C' && buffer[2] == 'E' && buffer[3] == 'R')
                            {
                                return Encoding.UTF8.GetString(buffer, 8, bytesRead - 8);
                            }
                            return Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        }
                        return null;
                    }
                }
                catch
                {
                    // Fall back to Named Pipes if TCP is unavailable or busy
                }

                // Attempt 2: Named Pipes (NitroSense and PredatorSense candidates)
                foreach (var pipeName in PipeCandidates)
                {
                    try
                    {
                        using var ctsPipe = new CancellationTokenSource(timeoutMs);
                        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                        
                        var connectTask = pipe.ConnectAsync(ctsPipe.Token);
                        var delayTask = Task.Delay(timeoutMs, ctsPipe.Token);
                        if (await Task.WhenAny(connectTask, delayTask) == connectTask && pipe.IsConnected)
                        {
                            byte[] idBytes = BitConverter.GetBytes(packetId);
                            byte[] payloadBytes = Encoding.UTF8.GetBytes(jsonPayload);

                            byte[] fullPacket = new byte[MagicBytes.Length + idBytes.Length + payloadBytes.Length];
                            Buffer.BlockCopy(MagicBytes, 0, fullPacket, 0, MagicBytes.Length);
                            Buffer.BlockCopy(idBytes, 0, fullPacket, MagicBytes.Length, idBytes.Length);
                            Buffer.BlockCopy(payloadBytes, 0, fullPacket, MagicBytes.Length + idBytes.Length, payloadBytes.Length);

                            await pipe.WriteAsync(fullPacket, ctsPipe.Token);
                            await pipe.FlushAsync(ctsPipe.Token);

                            byte[] buffer = new byte[8192];
                            int bytesRead = await pipe.ReadAsync(buffer, ctsPipe.Token);
                            if (bytesRead > 0)
                            {
                                if (bytesRead > 8 && buffer[0] == 'A' && buffer[1] == 'C' && buffer[2] == 'E' && buffer[3] == 'R')
                                {
                                    return Encoding.UTF8.GetString(buffer, 8, bytesRead - 8);
                                }
                                return Encoding.UTF8.GetString(buffer, 0, bytesRead);
                            }
                        }
                    }
                    catch
                    {
                        // Try next pipe candidate
                    }
                }
            }
            finally
            {
                _sendLock.Release();
            }

            return null;
        }

        #region High-Level Hardware Commands

        /// <summary>
        /// Sets device data via SET_DEVICE_DATA (Packet 100).
        /// Validates return code "result": "0" from OEM service.
        /// </summary>
        public static async Task<bool> SetDeviceDataAsync(string function, object parameter)
        {
            var payloadObj = new
            {
                Function = function,
                Parameter = parameter
            };
            string json = JsonSerializer.Serialize(payloadObj);
            string? resp = await SendCommandAsync(CMD_SET_DEVICE_DATA, json);
            if (string.IsNullOrEmpty(resp)) return false;

            try
            {
                using var doc = JsonDocument.Parse(resp);
                if (doc.RootElement.TryGetProperty("result", out var resProp))
                {
                    if (resProp.ValueKind == JsonValueKind.String && resProp.GetString() == "0")
                        return true;
                    if (resProp.ValueKind == JsonValueKind.Number && resProp.GetInt32() == 0)
                        return true;
                }
            }
            catch { }

            return resp.Contains("\"result\" : \"0\"") || resp.Contains("\"result\":\"0\"");
        }

        /// <summary>
        /// Queries supported GPU MUX capabilities bitmask from AcerAgentService:
        /// Bit 0 (1): Optimus
        /// Bit 1 (2): Discrete GPU
        /// Bit 2 (4): Auto / Advanced Optimus
        /// Returns e.g. 7 (all 3 supported) or 3 (Optimus + Discrete).
        /// </summary>
        public static async Task<int> GetGpuModeCapabilityAsync()
        {
            try
            {
                string? resp = await SendCommandAsync(CMD_INITIALIZATION, "{}");
                if (!string.IsNullOrEmpty(resp))
                {
                    using var doc = JsonDocument.Parse(resp);
                    if (doc.RootElement.TryGetProperty("SUPPORT_GPU_MODE_CAPABILITY", out var capProp) &&
                        capProp.ValueKind == JsonValueKind.Array &&
                        capProp.GetArrayLength() > 0)
                    {
                        return capProp[0].GetInt32();
                    }
                }
            }
            catch { }
            return 0; // Default to 0 (No hardware MUX capability) when service is unavailable or unsupported
        }

        /// <summary>
        /// Queries device data via GET_UPDATED_DATA (Packet 20).
        /// </summary>
        public static async Task<string?> GetDeviceDataAsync(string function)
        {
            var payloadObj = new { Function = function };
            string json = JsonSerializer.Serialize(payloadObj);
            return await SendCommandAsync(CMD_GET_UPDATED_DATA, json);
        }

        /// <summary>
        /// Sets Operating Power Mode in Acer OEM Service:
        /// 0 = Quiet
        /// 1 = Balanced (Default)
        /// 4 = Performance (Extreme)
        /// 5 = Turbo
        /// 6 = Eco
        /// </summary>
        public static async Task<bool> SetOperatingModeAsync(int mode)
        {
            return await SetDeviceDataAsync("OPERATING_MODE", new { mode });
        }

        /// <summary>
        /// Queries current Operating Power Mode from Acer OEM Service.
        /// </summary>
        public static async Task<int?> GetOperatingModeAsync()
        {
            string? resp = await GetDeviceDataAsync("OPERATING_MODE");
            if (string.IsNullOrEmpty(resp)) return null;

            try
            {
                using var doc = JsonDocument.Parse(resp);
                if (doc.RootElement.TryGetProperty("data", out var dataProp) &&
                    dataProp.TryGetProperty("mode", out var modeProp))
                {
                    return modeProp.GetInt32();
                }
                if (doc.RootElement.TryGetProperty("mode", out var directMode))
                {
                    return directMode.GetInt32();
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Sets Fan Mode in Acer OEM Service:
        /// 0 = Auto
        /// 1 = Max (100% full speed)
        /// 2 = Custom
        /// </summary>
        public static async Task<bool> SetFanModeAsync(int mode)
        {
            return await SetDeviceDataAsync("FAN_CONTROL", new { mode });
        }

        /// <summary>
        /// Sets Custom Fan Speeds in Acer OEM Service.
        /// </summary>
        public static async Task<bool> SetCustomFanSpeedAsync(int cpuSpeed, int gpuSpeed)
        {
            var param = new
            {
                mode = 2,
                custom_fan_data = new object[]
                {
                    new { fan_name = "CPU", fan_index = 0, fan_custom_auto = 0, fan_custom_speed = cpuSpeed },
                    new { fan_name = "GPU", fan_index = 0, fan_custom_auto = 0, fan_custom_speed = gpuSpeed }
                }
            };
            return await SetDeviceDataAsync("FAN_CONTROL", param);
        }

        /// <summary>
        /// Queries current Fan Control state from Acer OEM Service.
        /// </summary>
        public static async Task<string?> GetFanControlAsync()
        {
            return await GetDeviceDataAsync("FAN_CONTROL");
        }

        /// <summary>
        /// Sets GPU MUX working mode:
        /// 0 = Optimus (Dynamic switching / Hybrid)
        /// 1 = Discrete (NVIDIA GPU Only / Direct display connection)
        /// 2 = Automatic (Advanced Optimus)
        /// </summary>
        public static async Task<bool> SetGpuModeAsync(int mode)
        {
            return await SetDeviceDataAsync("GPU_MODE", new { mode });
        }

        /// <summary>
        /// Queries the current GPU MUX working mode.
        /// </summary>
        public static async Task<int?> GetGpuModeAsync()
        {
            string? resp = await GetDeviceDataAsync("GPU_MODE");
            if (string.IsNullOrEmpty(resp)) return null;

            try
            {
                using var doc = JsonDocument.Parse(resp);
                if (doc.RootElement.TryGetProperty("data", out var dataProp) &&
                    dataProp.TryGetProperty("mode", out var modeProp))
                {
                    return modeProp.GetInt32();
                }
                if (doc.RootElement.TryGetProperty("mode", out var directMode))
                {
                    return directMode.GetInt32();
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Sets LCD panel overdrive (response time acceleration).
        /// </summary>
        public static async Task<bool> SetLcdOverdriveAsync(bool enable)
        {
            return await SetDeviceDataAsync("LCD_OVERDRIVE", new { status = enable ? 1 : 0 });
        }

        /// <summary>
        /// Queries LCD overdrive status.
        /// </summary>
        public static async Task<bool?> GetLcdOverdriveAsync()
        {
            string? resp = await GetDeviceDataAsync("LCD_OVERDRIVE");
            if (string.IsNullOrEmpty(resp)) return null;

            try
            {
                using var doc = JsonDocument.Parse(resp);
                if (doc.RootElement.TryGetProperty("data", out var dataProp) &&
                    dataProp.TryGetProperty("status", out var statusProp))
                {
                    return statusProp.GetInt32() == 1;
                }
                if (doc.RootElement.TryGetProperty("status", out var directStatus))
                {
                    return directStatus.GetInt32() == 1;
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Sets Keyboard Backlight 30-Second Idle Sleep timeout.
        /// </summary>
        public static async Task<bool> SetBacklight30sAsync(bool enable)
        {
            return await SetDeviceDataAsync("BACKLIGHT_30S", new { status = enable ? 1 : 0 });
        }

        /// <summary>
        /// Queries Keyboard Backlight 30-Second Idle Sleep status.
        /// </summary>
        public static async Task<bool?> GetBacklight30sAsync()
        {
            string? resp = await GetDeviceDataAsync("BACKLIGHT_30S");
            if (string.IsNullOrEmpty(resp)) return null;

            try
            {
                using var doc = JsonDocument.Parse(resp);
                if (doc.RootElement.TryGetProperty("data", out var dataProp) &&
                    dataProp.TryGetProperty("status", out var statusProp))
                {
                    return statusProp.GetInt32() == 1;
                }
                if (doc.RootElement.TryGetProperty("status", out var directStatus))
                {
                    return directStatus.GetInt32() == 1;
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Sets BIOS Boot Sound (Startup Chime).
        /// </summary>
        public static async Task<bool> SetBootSoundAsync(bool enable)
        {
            return await SetDeviceDataAsync("BOOT_SOUND", new { status = enable ? 1 : 0 });
        }

        /// <summary>
        /// Queries BIOS Boot Sound status.
        /// </summary>
        public static async Task<bool?> GetBootSoundAsync()
        {
            string? resp = await GetDeviceDataAsync("BOOT_SOUND");
            if (string.IsNullOrEmpty(resp)) return null;

            try
            {
                using var doc = JsonDocument.Parse(resp);
                if (doc.RootElement.TryGetProperty("data", out var dataProp) &&
                    dataProp.TryGetProperty("status", out var statusProp))
                {
                    return statusProp.GetInt32() == 1;
                }
                if (doc.RootElement.TryGetProperty("status", out var directStatus))
                {
                    return directStatus.GetInt32() == 1;
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Sets BIOS boot startup animation / logo.
        /// </summary>
        public static async Task<bool> SetBootAnimationAsync(bool enable)
        {
            string img = enable ? @"C:\ProgramData\oem\AcerAgentService\BIOSLogo\AcerLogo.gif" : "";
            return await SetDeviceDataAsync("CUSTOM_BOOT_LOGO", new { imageSource = img });
        }

        /// <summary>
        /// Queries BIOS boot startup animation / logo status.
        /// </summary>
        public static async Task<bool?> GetBootAnimationAsync()
        {
            string? resp = await GetDeviceDataAsync("CUSTOM_BOOT_LOGO");
            if (string.IsNullOrEmpty(resp)) return null;

            try
            {
                using var doc = JsonDocument.Parse(resp);
                if (doc.RootElement.TryGetProperty("data", out var dataProp) &&
                    dataProp.TryGetProperty("imageSource", out var imgProp))
                {
                    string? src = imgProp.GetString();
                    return !string.IsNullOrEmpty(src);
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Sets Windows and Menu Key Lock status in OEM service.
        /// </summary>
        public static async Task<bool> SetWinKeyLockAsync(bool lockKeys)
        {
            // Note: OEM status 1 = lock enabled, 0 = lock disabled
            return await SetDeviceDataAsync("WIN_KEY", new { status = lockKeys ? 1 : 0 });
        }

        /// <summary>
        /// Sets CoolBoost™ Fan Ceiling Boost.
        /// </summary>
        public static async Task<bool> SetCoolBoostAsync(bool enable)
        {
            return await SetDeviceDataAsync("COOL_BOOST", new { status = enable ? 1 : 0 });
        }

        /// <summary>
        /// Queries CoolBoost™ status from OEM service.
        /// </summary>
        public static async Task<bool?> GetCoolBoostAsync()
        {
            try
            {
                string? resp = await GetDeviceDataAsync("COOL_BOOST");
                if (!string.IsNullOrEmpty(resp))
                {
                    using var doc = JsonDocument.Parse(resp);
                    if (doc.RootElement.TryGetProperty("data", out var dataProp) &&
                        dataProp.TryGetProperty("status", out var statProp))
                    {
                        return statProp.GetInt32() == 1;
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Sets 4-zone static keyboard RGB lighting via OEM Agent Service LIGHTING command.
        /// </summary>
        public static async Task<bool> Set4ZoneLightingAsync(Color[] zones, byte brightness = 100)
        {
            if (zones == null || zones.Length == 0) return false;
            var leds = new object[Math.Min(4, zones.Length)];
            for (int i = 0; i < leds.Length; i++)
            {
                string hex = $"#{zones[i].R:X2}{zones[i].G:X2}{zones[i].B:X2}";
                leds[i] = new { LED_id = i, color = hex, status = 1 };
            }

            int bVal = Math.Clamp((int)Math.Round(brightness * 5.0 / 100.0), 0, 5);
            string hexPrimary = $"#{zones[0].R:X2}{zones[0].G:X2}{zones[0].B:X2}";
            var param = new
            {
                device = 0,
                effect = "STATIC",
                color = hexPrimary,
                brightness = bVal,
                speed = 5,
                direction = 0,
                LEDs = leds
            };

            return await SetDeviceDataAsync("LIGHTING", param);
        }

        /// <summary>
        /// Sets animated or mode-based keyboard RGB lighting via OEM Agent Service LIGHTING command.
        /// </summary>
        public static async Task<bool> SetRgbEffectAsync(int mode, Color c, byte brightness, byte speed, byte direction)
        {
            string effectName = mode switch
            {
                0 => "STATIC",
                1 => "BREATHING",
                2 => "NEON",
                3 => "WAVE",
                4 => "SHIFTING",
                5 => "ZOOM",
                6 => "METEOR",
                7 => "TWINKLING",
                _ => "STATIC"
            };

            int bVal = Math.Clamp((int)Math.Round(brightness * 5.0 / 100.0), 0, 5);
            int sVal = speed > 9 ? Math.Clamp((int)Math.Round(speed * 9.0 / 100.0), 1, 9) : Math.Clamp((int)speed, 1, 9);
            string hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";

            var param = new
            {
                device = 0,
                effect = effectName,
                color = hex,
                brightness = bVal,
                speed = sVal,
                direction = direction
            };

            return await SetDeviceDataAsync("LIGHTING", param);
        }

        #endregion
    }
}
