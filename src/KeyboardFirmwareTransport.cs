using System.Management;
using Microsoft.Win32;

namespace PredatorControlApp;

// Use one readable APGe transport, preserving all unrelated configuration bits.
// Never guess Predator's default hotkey number on a Nitro keyboard.
internal sealed class KeyboardFirmwareTransport(bool nitroSense3 = false) : IKeyboardFirmware
{
    private string? _className;
    private uint? _hotkey;

    public bool TryRead(out uint configuration)
    {
        configuration = 0;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(nitroSense3 ? @"SOFTWARE\OEM\NitroSense" : @"SOFTWARE\OEM\AcerAgentService");
            object? raw = key?.GetValue("BK_Hotkey_Number");
            if (raw == null || !uint.TryParse(raw.ToString(), out uint hotkey) || hotkey > 255) return false;
            _hotkey = hotkey;
            foreach (string name in _className == null ? new[] { "APGeAction", "AcerGenericMethod" } : new[] { _className })
            {
                try
                {
                    if (Invoke(name, "GetFunction", 0x80001U | (hotkey << 8), out ulong output) && (output & 255) == 0)
                    {
                        uint config = (uint)(output >> 32);
                        // Only accept the configuration shape this adapter understands.
                        if ((config & 0xFF) > 100 || ((config >> 8) & 0xFF) is not (0 or 30)) continue;
                        configuration = config;
                        _className = name;
                        return true;
                    }
                }
                catch (ManagementException) { }
            }
        }
        catch { /* Read failure must never cause a speculative write. */ }
        return false;
    }

    public bool TryWrite(uint configuration)
    {
        if (!HardwareWriteGate.InScope(HardwareFeature.KeyboardPower, "apge-keyboard") &&
            !HardwareWriteGate.InScope(HardwareFeature.KeyboardTimeout, "apge-keyboard")) return false;
        if (_className == null || !_hotkey.HasValue) return false;
        try
        {
            ulong input = ((ulong)configuration << 32) | 0x80002U | (_hotkey.Value << 8);
            // SetFunction's output is not treated as an acknowledgement; caller reads back.
            return Invoke(_className, "SetFunction", input, out _);
        }
        catch { return false; }
    }

    private static bool Invoke(string className, string method, ulong input, out ulong output)
    {
        output = 0;
        var scope = new ManagementScope(@"\\.\root\WMI");
        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT * FROM {className}"),
            new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(2) });
        using var results = searcher.Get();
        using var enumerator = results.GetEnumerator();
        if (!enumerator.MoveNext() || enumerator.Current is not ManagementObject obj) return false;
        using (obj)
        using (var args = obj.GetMethodParameters(method))
        {
            args["uiInput"] = args.Properties["uiInput"].Type == CimType.UInt32 ? (object)checked((uint)input) : input;
            using var result = obj.InvokeMethod(method, args, new InvokeMethodOptions { Timeout = TimeSpan.FromSeconds(2) });
            if (result?["uiOutput"] == null) return false;
            output = Convert.ToUInt64(result["uiOutput"]);
            return true;
        }
    }
}
