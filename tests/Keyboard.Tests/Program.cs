using PredatorControlApp;

int passed = 0;
void Check(bool condition, string scenario)
{
    if (!condition) throw new Exception(scenario);
    Console.WriteLine($"PASS {scenario}");
    passed++;
}

var binary = KeyboardCapabilities.Detect("Acer", "Nitro AN515-58", true, null);
Check(binary.Kind == KeyboardKind.Binary && !binary.SupportsBrightness && !binary.SupportsRgb && binary.FixedColor == "#ff394b",
    "Nitro uses a fixed red On/Off profile even when an RGB method exists");
foreach (var model in new[] { "Nitro AN515-54", "AN515-54", "Nitro AN515-58", "AN515-58", "Nitro AN16-41", "ANV15-51", "Nitro V 15" })
foreach (var profile in new string?[] { null, "rgb", "binary", "none" })
{
    var capabilities = KeyboardCapabilities.Detect("Acer", model, true, profile);
    Check(capabilities.Kind == KeyboardKind.Binary && capabilities.FixedColor == "#ff394b" &&
        !capabilities.SupportsBrightness && !capabilities.SupportsRgb && capabilities.SupportsPower,
        $"{model} ignores misleading RGB metadata and saved profile '{profile ?? "auto"}'");
}
Check(KeyboardCapabilities.Detect("Acer", "Predator PH16-71", true, null).SupportsRgb,
    "Predator RGB capabilities are preserved");
Check(KeyboardCapabilities.Detect("Acer", "PH16-71", false, "rgb").SupportsRgb,
    "explicit RGB profile remains available outside Nitro");
Check(!KeyboardCapabilities.Detect("Other OEM", "Nitro AN515-58", true, "rgb").SupportsPower,
    "manufacturer is checked before applying Nitro policy");
Check(KeyboardCapabilities.Detect("Acer", "Unknown", false, null).Kind == KeyboardKind.Unknown,
    "unknown hardware is not silently classified as a red backlight");
Check(KeyboardCapabilities.Detect("Acer", "ANN515-58", false, null).Kind == KeyboardKind.Unknown &&
    KeyboardCapabilities.Detect("Acer", "AV15-51", false, null).Kind == KeyboardKind.Unknown,
    "unrelated model prefixes are not classified as Nitro");

foreach (bool on in new[] { false, true })
foreach (bool timeout in new[] { false, true })
{
    uint current = 0xABCD0000U | (on ? 100U : 0U) | (timeout ? 0x1E00U : 0U);
    var firmware = new FakeFirmware { Configuration = current };
    var controller = new KeyboardController(binary, firmware);
    var result = controller.Apply(null, !timeout);
    Check(result.Confirmed && (firmware.Configuration & 0xFFU) == (on ? 100U : 0U) &&
        (firmware.Configuration & 0xFFFF0000U) == 0xABCD0000U,
        $"changing timeout preserves power={on} and unrelated firmware bits (timeout={timeout})");
    result = controller.Apply(!on, null);
    Check(result.Confirmed && ((firmware.Configuration >> 8) & 0xFFU) == (!timeout ? 30U : 0U),
        $"changing power preserves timeout (power={on}, timeout={timeout})");
}

var io = new FakeFirmware { Configuration = 0x1E64 };
var device = new KeyboardController(binary, io);
Check(device.Restore(null, null) == null && io.Reads == 0 && io.Writes == 0,
    "first launch without explicit preferences performs no reads or writes");
var legacy = new Dictionary<string, object?> { ["Brightness"] = 0, ["RGB_Mode"] = 8, ["Backlight30s"] = 1 };
var migrated = KeyboardPreferences.Read(name => legacy.GetValueOrDefault(name));
Check(device.Restore(migrated.Enabled, migrated.IdleTimeout) == null && io.Writes == 0,
    "legacy RGB Off, brightness zero and timeout keys cannot turn the keyboard off on startup");
legacy["KeyboardPowerEnabled"] = "0"; legacy["KeyboardIdleTimeout"] = 2;
migrated = KeyboardPreferences.Read(name => legacy.GetValueOrDefault(name));
Check(migrated.Enabled == null && migrated.IdleTimeout == null,
    "invalid explicit settings preserve firmware instead of guessing a boolean");
legacy["KeyboardPowerEnabled"] = 0; legacy["KeyboardIdleTimeout"] = 1;
migrated = KeyboardPreferences.Read(name => legacy.GetValueOrDefault(name));
Check(migrated.Enabled == false && migrated.IdleTimeout == true, "explicit binary preferences retain Off independently of timeout");
Check(device.State.Enabled == null, "unverified firmware state is not displayed as confirmed Off");
io.Readable = false;
Check(device.Apply(false, null).Status == "failed" && io.Writes == 0,
    "missing readable OEM transport prevents a write");
io.Readable = true; io.Writeable = false;
Check(device.Apply(false, null).Status == "failed" && device.State.Enabled == null,
    "failed write does not change confirmed state");
io.Writeable = true; io.IgnoreWrite = true;
Check(device.Apply(false, null).Status == "unverified" && device.State.Enabled == null,
    "acknowledgement without matching readback is not success");
io.IgnoreWrite = false;
Check(device.Apply(false, null).Confirmed && device.State.Enabled == false,
    "explicit Off is supported and tracked after readback");
Check(device.Apply(null, false).Confirmed && (io.Configuration & 0xFF) == 0 && device.State.Enabled == false,
    "timeout change never substitutes brightness 100 for an Off backlight");
Check(device.Restore(true, true)?.Confirmed == true && io.Configuration == 0x1E64,
    "explicit startup preferences restore together with one firmware write");
var unknownIo = new FakeFirmware();
var unknown = new KeyboardController(new(KeyboardKind.Unknown, "Unverified"), unknownIo);
Check(unknown.Apply(true, true).Status == "failed" && unknownIo.Reads == 0 && unknownIo.Writes == 0,
    "unknown keyboards reject commands without touching firmware");
Console.WriteLine($"{passed} keyboard regression checks passed. No hardware accessed.");

sealed class FakeFirmware : IKeyboardFirmware
{
    public uint Configuration;
    public bool Readable = true, Writeable = true, IgnoreWrite;
    public int Reads, Writes;
    public bool TryRead(out uint configuration) { Reads++; configuration = Configuration; return Readable; }
    public bool TryWrite(uint configuration)
    {
        Writes++;
        if (!Writeable) return false;
        if (!IgnoreWrite) Configuration = configuration;
        return true;
    }
}
