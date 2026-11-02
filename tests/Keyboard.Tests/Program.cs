using PredatorControlApp;

int passed = 0;
void Check(bool condition, string scenario)
{
    if (!condition) throw new Exception(scenario);
    Console.WriteLine($"PASS {scenario}");
    passed++;
}

// Explicit fixture evidence replaces the former brand-wide and user-preference assumptions.
var binary = new KeyboardCapabilities(KeyboardKind.Binary, "Verified synthetic fixed-red keyboard", "#ff394b", IdleTimeoutSupported: true);
foreach (string model in new[] { "Nitro AN515-58", "ANV15-51", "PH16-71", "Unknown" })
    Check(!KeyboardCapabilities.Detect("Acer", model, true, "rgb").SupportsPower,
        $"Brand, method metadata and saved RGB preference cannot authorize {model}");

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
