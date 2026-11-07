using System.Collections.ObjectModel;
using System.Text.Json;

namespace PredatorControlApp;

// Protocol identity comes from the reviewed shipped service and explicit live capabilities.
// Neither branding, a registry preference nor WMI method-name similarity selects this backend.
internal static class VerifiedAcerProtocol
{
    internal const string Family = "acer-agent-5.2";
    internal const string PowerBackend = "acer-agent-profile";
    internal const string FanBackend = "acer-agent-fans";
    internal const string AgentSha256 = "FA8C50E604CA71F93314209414D35622A265D1CB65532798DF810C17FC042201";
    internal const string AgentSha256_2108 = "46533208B86B48EA901C8CE41368C44B512BA57B941D3E02048B08C039959484";
    internal const string CentralSha256 = "63855F9D6BD706145E82A8CBFEA5A26ECD273ED2E16674C4CA17A2F9025475D9";
    internal const string CentralSha256_2108 = "5BB16FA594878F7014BC61E2A7B2A6C6B640F0CE0E6A1BF1E18E440C3DD4C9F3";
    internal const string Nitro3ServiceSha256 = "E6638FECC41730075D25587BFDDA7B4DDA2D170B64AC9DF9B3C03543999F3B4E";
    internal static bool ReviewedAgent(string? hash) =>
        string.Equals(hash, AgentSha256, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(hash, AgentSha256_2108, StringComparison.OrdinalIgnoreCase);
    internal static bool ReviewedService(ServiceEvidence? service) => service is { Exists: true, Running: true, Trusted: true, ProcessId: > 0 } &&
        ((string.Equals(service.BinarySha256, CentralSha256, StringComparison.OrdinalIgnoreCase) && string.Equals(service.ProtocolBinarySha256, AgentSha256, StringComparison.OrdinalIgnoreCase)) ||
         (string.Equals(service.BinarySha256, CentralSha256_2108, StringComparison.OrdinalIgnoreCase) && string.Equals(service.ProtocolBinarySha256, AgentSha256_2108, StringComparison.OrdinalIgnoreCase)));
    internal static bool ReviewedAttestedBinary(string hash) => ReviewedAgent(hash) ||
        string.Equals(hash, CentralSha256, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(hash, CentralSha256_2108, StringComparison.OrdinalIgnoreCase);

    internal static IReadOnlyList<PowerMode> ParseModes(string? json)
    {
        using var document = Reply(json, "SUPPORT_OPERATING_MODE_CAPABILITY");
        if (document == null) return Array.Empty<PowerMode>();
        var data = document.RootElement.GetProperty("data");
        if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() is < 1 or > 5) return Array.Empty<PowerMode>();
        var modes = new List<PowerMode>();
        foreach (var item in data.EnumerateArray())
        {
            if (!item.TryGetByteSafe(out byte value) || modes.Any(m => m.Value == value)) return Array.Empty<PowerMode>();
            string? label = value switch { 0 => "Quiet", 1 => "Balanced", 4 => "Performance", 5 => "Turbo", 6 => "Eco", _ => null };
            if (label == null) return Array.Empty<PowerMode>();
            modes.Add(new(value, label, OnAc: value != 6, OnBattery: value is 1 or 6));
        }
        return Array.AsReadOnly(modes.ToArray());
    }

    internal static byte? ParseCurrentMode(string? json)
    {
        using var document = Reply(json, "OPERATING_MODE");
        if (document == null) return null;
        var data = document.RootElement.GetProperty("data");
        return data.ValueKind == JsonValueKind.Object && data.TryGetProperty("mode", out var mode) && mode.TryGetByteSafe(out byte value) &&
            value is 0 or 1 or 4 or 5 or 6 ? value : null;
    }

    internal static bool? ParseStatus(string? json, string function)
    {
        using var document = Reply(json, function);
        if (document == null) return null;
        var data = document.RootElement.GetProperty("data");
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("status", out var status) || !status.TryGetByteSafe(out byte value)) return null;
        // Full adaptor uses bits 0/1; USB-PD uses bits 2/3. A nonzero value
        // alone does not confirm that higher performance modes are available.
        return function == "ADAPTOR_STATUS" ? value <= 15 ? (value & 3) != 0 : null : value <= 1 ? value == 1 : null;
    }

    internal static IReadOnlyList<OemFanChannel> ParseFans(string? json)
    {
        using var document = Reply(json, "SUPPORT_FAN_CONTROL");
        if (document == null) return [];
        var data = document.RootElement.GetProperty("data");
        if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() is < 1 or > 8) return [];
        var fans = new List<OemFanChannel>();
        foreach (var fan in data.EnumerateArray())
        {
            if (fan.ValueKind != JsonValueKind.Object || !ReadInt(fan, "fan_index", out int index) || index is < 0 or > 7 ||
                !fan.TryGetProperty("fan_name", out var name) || name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString()) || name.GetString()!.Length > 32 ||
                !ReadInt(fan, "fan_custom_min_speed", out int min) || !ReadInt(fan, "fan_custom_max_speed", out int max) || min < 0 || min > max || max > 100 ||
                fans.Any(f => f.Index == index && f.Name == name.GetString())) return [];
            fans.Add(new(index, name.GetString()!, min, max));
        }
        return Array.AsReadOnly(fans.ToArray());
    }

    // The existing UI exposes CPU and GPU custom speed sliders. Bind those sliders only
    // when the live OEM channel names identify exactly those two channels.
    internal static bool TryGetCpuGpuFans(IReadOnlyList<OemFanChannel> fans, out OemFanChannel cpu, out OemFanChannel gpu)
    {
        cpu = null!;
        gpu = null!;
        if (fans.Count != 2) return false;
        var cpus = fans.Where(f => string.Equals(f.Name.Trim(), "CPU", StringComparison.OrdinalIgnoreCase)).ToArray();
        var gpus = fans.Where(f => string.Equals(f.Name.Trim(), "GPU", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (cpus.Length != 1 || gpus.Length != 1) return false;
        cpu = cpus[0];
        gpu = gpus[0];
        return TryGetCustomSliderRange(cpu, out _, out _) && TryGetCustomSliderRange(gpu, out _, out _);
    }

    internal static bool TryGetCustomSliderRange(OemFanChannel fan, out int minimum, out int maximum)
    {
        // The existing PredatorControl control is a 10–100 percentage slider. Preserve
        // that floor while honoring narrower limits reported by the reviewed OEM service.
        minimum = Math.Max(10, fan.Minimum);
        maximum = fan.Maximum;
        return minimum <= maximum;
    }

    internal static OemKeyboardInfo? ParseKeyboard(string? json)
    {
        using var document = Reply(json, "SUPPORT_KEYBOARD_INFO");
        if (document == null) return null;
        var data = document.RootElement.GetProperty("data");
        if (data.ValueKind != JsonValueKind.Object || !ReadInt(data, "keyboard_backlight_color_control", out int control) || control is < 0 or > 2 ||
            !ReadInt(data, "keyboard_led_type", out int type) || type is not (0 or 1 or 3 or 4)) return null;
        return new(control switch { 0 => "none", 1 => "fixed-color", _ => "rgb" }, type, type == 0);
    }

    internal static int ParseGpuMask(string? json)
    {
        using var document = Reply(json, "SUPPORT_GPU_MODE_CAPABILITY");
        if (document == null) return 0;
        var data = document.RootElement.GetProperty("data");
        return data.ValueKind == JsonValueKind.Array && data.GetArrayLength() == 1 && data[0].TryGetByteSafe(out byte mask) && mask is > 0 and <= 7 ? mask : 0;
    }

    internal static bool AuthorizedWritePayload(string json)
    {
        try
        {
            if (json.Length > 16384) return false;
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || HasDuplicateFields(root) || root.EnumerateObject().Count() != 2 ||
                !root.TryGetProperty("Function", out var function) || function.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("Parameter", out var parameter) || parameter.ValueKind != JsonValueKind.Object ||
                !parameter.TryGetProperty("mode", out var mode) || !mode.TryGetByteSafe(out byte value)) return false;
            if (HardwareWriteGate.InScope(HardwareFeature.Power, PowerBackend))
                return function.GetString() == "OPERATING_MODE" && parameter.EnumerateObject().Count() == 1 && value is 0 or 1 or 4 or 5 or 6;
            bool fanScope = value switch
            {
                0 => HardwareWriteGate.InScope(HardwareFeature.FanAuto, FanBackend),
                1 => HardwareWriteGate.InScope(HardwareFeature.FanMax, FanBackend),
                2 => HardwareWriteGate.InScope(HardwareFeature.FanManual, FanBackend),
                _ => false
            };
            if (function.GetString() != "FAN_CONTROL" || !fanScope || parameter.EnumerateObject().Count() != 2 ||
                !parameter.TryGetProperty("custom_fan_data", out var custom) || custom.ValueKind != JsonValueKind.Array || custom.GetArrayLength() is < 1 or > 8)
                return false;
            var seen = new HashSet<(int, string)>();
            foreach (var item in custom.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || item.EnumerateObject().Count() != 4 ||
                    !ReadInt(item, "fan_index", out int index) || index is < 0 or > 7 ||
                    !item.TryGetProperty("fan_name", out var name) || name.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(name.GetString()) || name.GetString()!.Length > 32 ||
                    !seen.Add((index, name.GetString()!)) || !ReadInt(item, "fan_custom_speed", out int speed) || speed is < 0 or > 100 ||
                    !ReadInt(item, "fan_custom_auto", out int automatic) || automatic is < 0 or > 1)
                    return false;
            }
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static bool ReadInt(JsonElement obj, string name, out int number)
    {
        number = 0;
        if (!obj.TryGetProperty(name, out var value)) return false;
        return value.ValueKind == JsonValueKind.Number ? value.TryGetInt32(out number) :
            value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out number);
    }

    internal static (byte Mode, JsonElement CustomData)? ParseFanState(string? json, IReadOnlyList<OemFanChannel> fans)
    {
        using var document = Reply(json, "FAN_CONTROL");
        if (document == null || fans.Count == 0) return null;
        var data = document.RootElement.GetProperty("data");
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("mode", out var mode) || !mode.TryGetByteSafe(out byte value) || value > 2 ||
            !data.TryGetProperty("custom_fan_data", out var custom) || custom.ValueKind != JsonValueKind.Array || custom.GetArrayLength() != fans.Count) return null;
        var seen = new HashSet<(string, int)>();
        foreach (var fan in custom.EnumerateArray())
        {
            if (fan.ValueKind != JsonValueKind.Object || !ReadInt(fan, "fan_index", out int index) ||
                !fan.TryGetProperty("fan_name", out var name) || name.ValueKind != JsonValueKind.String ||
                !seen.Add((name.GetString()!, index))) return null;
            var supported = fans.FirstOrDefault(f => f.Index == index && f.Name == name.GetString());
            if (supported == null || !ReadInt(fan, "fan_custom_speed", out int speed) ||
                !ReadInt(fan, "fan_custom_auto", out int automatic) || automatic is < 0 or > 1 ||
                speed < supported.Minimum || speed > supported.Maximum) return null;
        }
        return (value, custom.Clone());
    }

    internal static bool FanSpeedMatches(JsonElement customData, IReadOnlyList<OemFanChannel> fans, int cpuSpeed, int gpuSpeed)
    {
        if (!TryGetCpuGpuFans(fans, out var cpu, out var gpu) || customData.ValueKind != JsonValueKind.Array || customData.GetArrayLength() != fans.Count)
            return false;
        bool cpuFound = false, gpuFound = false;
        foreach (var item in customData.EnumerateArray())
        {
            if (!ReadInt(item, "fan_index", out int index) || !item.TryGetProperty("fan_name", out var name) || name.ValueKind != JsonValueKind.String ||
                !ReadInt(item, "fan_custom_speed", out int speed)) return false;
            if (index == cpu.Index && name.GetString() == cpu.Name && speed == cpuSpeed) cpuFound = true;
            else if (index == gpu.Index && name.GetString() == gpu.Name && speed == gpuSpeed) gpuFound = true;
            else return false;
        }
        return cpuFound && gpuFound;
    }

    internal static bool AllFansManual(JsonElement customData)
    {
        if (customData.ValueKind != JsonValueKind.Array || customData.GetArrayLength() is < 1 or > 8) return false;
        foreach (var item in customData.EnumerateArray())
            if (!ReadInt(item, "fan_custom_auto", out int automatic) || automatic != 0) return false;
        return true;
    }

    private static bool TryGetByteSafe(this JsonElement value, out byte number)
    {
        number = 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetByte(out number);
    }

    private static JsonDocument? Reply(string? json, string request)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 16384) return null;
        JsonDocument? document = null;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || HasDuplicateFields(root) ||
                !root.TryGetProperty("request", out var echo) || echo.ValueKind != JsonValueKind.String || echo.GetString() != request ||
                !root.TryGetProperty("data", out _) ||
                root.TryGetProperty("result", out var result) && !(result.ValueKind == JsonValueKind.String && result.GetString() == "0") &&
                !(result.ValueKind == JsonValueKind.Number && result.TryGetInt32(out int code) && code == 0))
            { document.Dispose(); return null; }
            return document;
        }
        catch (JsonException) { document?.Dispose(); return null; }
    }

    private static bool HasDuplicateFields(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => value.EnumerateObject().GroupBy(p => p.Name).Any(g => g.Count() > 1) || value.EnumerateObject().Any(p => HasDuplicateFields(p.Value)),
        JsonValueKind.Array => value.EnumerateArray().Any(HasDuplicateFields),
        _ => false
    };

    internal static ProtocolEvidence? Match(HardwareDiscovery discovery)
    {
        if (!discovery.Identity.IsAcer) return null;
        var matches = discovery.Protocols.Where(p => p.Family == Family && p.Service == "AASSvc" && ReviewedAgent(p.BinarySha256) &&
            discovery.Services.GetValueOrDefault(p.Service) is { Exists: true, Running: true, Trusted: true, InterfaceWorking: true } service &&
            ReviewedService(service) && p.PowerModes.Count <= 5 &&
            p.PowerModes.Select(m => m.Value).Distinct().Count() == p.PowerModes.Count &&
            p.PowerModes.All(m => m.Value is 0 or 1 or 4 or 5 or 6 && m.WindowsScheme == null) &&
            (p.PowerModes.Count == 0 || p.CurrentPowerMode.HasValue && p.PowerModes.Any(m => m.Value == p.CurrentPowerMode)) &&
            (p.PowerModes.Count > 0 || p.Fans is { Count: > 0 })).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    internal static DeviceProfile CreateProfile(ProtocolEvidence protocol, DeviceIdentity identity, DeviceProfile? modelException)
    {
        var bindings = (modelException?.Bindings ?? []).Where(b => b.Feature != HardwareFeature.Power).ToList();
        if (protocol.PowerModes.Count > 0) bindings.Add(new(HardwareFeature.Power, PowerBackend, Service: protocol.Service, CanVerify: true));
        if (protocol.Fans is { Count: > 0 })
        {
            bool preserveReviewedCurve = modelException?.Bindings.Any(b => b.Feature == HardwareFeature.FanCurve) == true;
            bindings.RemoveAll(b => b.Feature is HardwareFeature.FanAuto or HardwareFeature.FanMax or HardwareFeature.FanManual or HardwareFeature.FanCurve or HardwareFeature.AuxFan);
            bindings.Add(new(HardwareFeature.FanAuto, FanBackend, Service: protocol.Service, CanVerify: true));
            bindings.Add(new(HardwareFeature.FanMax, FanBackend, Service: protocol.Service, CanVerify: true));
            // The OEM payload reports channels and bounds, not a curve capability. The
            // existing software curve remains available only where an exact reviewed
            // model exception already supplied it and the live CPU/GPU speed backend works.
            if (TryGetCpuGpuFans(protocol.Fans, out _, out _))
            {
                bindings.Add(new(HardwareFeature.FanManual, FanBackend, Service: protocol.Service, CanVerify: true));
                if (preserveReviewedCurve)
                    bindings.Add(new(HardwareFeature.FanCurve, FanBackend, Service: protocol.Service, CanVerify: true));
            }
        }
        return new("protocol-" + Family, identity.NormalizedModel,
            "Reviewed PredatorSense 5.2 service fingerprint plus explicit supported-mode and current-mode replies; static interoperability evidence, physical validation pending.",
            bindings.AsReadOnly(), Array.AsReadOnly(protocol.PowerModes.ToArray()), modelException?.Keyboard ?? new(KeyboardKind.Unknown, "No reviewed lighting backend for this controller."),
            modelException?.Sensors ?? new ReadOnlyDictionary<string, ulong>(new Dictionary<string, ulong>()),
            UnsupportedFeatures: modelException?.UnsupportedFeatures);
    }
}
