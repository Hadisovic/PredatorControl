using System.Buffers.Binary;
using System.IO.Pipes;
using System.Management;
using Microsoft.Win32;

namespace PredatorControlApp;

// Original interoperability implementation. No Acer code or assemblies are distributed.
internal static class NitroSense3Protocol
{
    internal const string Backend = "nitrosense3-pipe";
    internal const string ApgeBackend = "nitrosense3-apge";
    internal const string OperationModePipeBackend = "nitrosense3-opmode";
    internal const string OperationModeWmiBackend = "nitrosense3-wmi-opmode";
    internal const ushort CmdSetOperationMode = 30;
    internal const ushort CmdWmiSetGamingMiscSetting = 63;
    internal const ushort CmdWmiGetGamingMiscSetting = 64;
    internal const uint MiscSubfunctionOperationMode = 0x0B;
    internal const byte ModeQuiet = 0;
    internal const byte ModeDefault = 1;
    internal const byte ModePerformance = 4;

    // Recovered from NitroSense 3.01.3056 Nitro_MonitoringPage_2022.xaml (Tag=0 Quiet, Tag=1 Default, Tag=4 Performance).
    internal static readonly IReadOnlyList<PowerMode> OperationModes = Array.AsReadOnly(new PowerMode[]
    {
        new(ModeQuiet, "Quiet", OnAc: true, OnBattery: false),
        new(ModeDefault, "Default", OnAc: true, OnBattery: true),
        new(ModePerformance, "Performance", OnAc: true, OnBattery: false)
    });

    internal static string? LastReadFailure { get; private set; }
    internal static bool IsSupportedOperationMode(byte mode) => mode is ModeQuiet or ModeDefault or ModePerformance;
    internal static ulong? EncodeOperationModeMiscInput(byte mode) =>
        IsSupportedOperationMode(mode) ? ((ulong)mode << 8) | MiscSubfunctionOperationMode : null;
    internal static byte[] EncodeOperationModeCommand(byte mode) =>
        IsSupportedOperationMode(mode) ? Encode(CmdSetOperationMode, mode, wide: false) : throw new ArgumentOutOfRangeException(nameof(mode));
    internal static int? MapGpuOverclockLevel(byte mode) => mode switch
    {
        ModeQuiet => 0,
        ModeDefault => 1,
        ModePerformance => 2,
        _ => null
    };
    internal static byte? OperationModeState(ulong? reply) =>
        reply.HasValue && (reply.Value & 255) == 0 && IsSupportedOperationMode((byte)((reply.Value >> 8) & 255))
            ? (byte)((reply.Value >> 8) & 255) : null;
    internal static bool? BatteryBoostReady(ulong? sysInfoReply) =>
        sysInfoReply.HasValue && (sysInfoReply.Value & 255) == 0 && ((sysInfoReply.Value >> 40) & 255) <= 1
            ? ((sysInfoReply.Value >> 40) & 255) == 1 : null;
    internal static byte? ParseRegistryOperationMode(object? raw) => raw switch
    {
        int i when i is >= 0 and <= 255 && IsSupportedOperationMode((byte)i) => (byte)i,
        uint u when u <= 255 && IsSupportedOperationMode((byte)u) => (byte)u,
        string s when byte.TryParse(s, out byte b) && IsSupportedOperationMode(b) => b,
        _ => null
    };
    internal static byte? ReadRegistryOperationMode()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\OEM\NitroSense\Overclock");
            return ParseRegistryOperationMode(key?.GetValue("CurrentOperationMode"));
        }
        catch { return null; }
    }
    internal static ulong? ExchangeDirect(ushort command, ulong input, bool wide)
    {
        if (!(command == 20 && !wide && input == 519) &&
            !(command == 17 && wide && input is 7 or 0x10007 && HardwareWriteGate.InScope(HardwareFeature.CoolBoost, ApgeBackend))) return null;
        try
        {
            using var searcher = new ManagementObjectSearcher(new ManagementScope(@"root\WMI"), new ObjectQuery("SELECT * FROM APGeAction"),
                new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(2) });
            using var rows = searcher.Get(); using var iterator = rows.GetEnumerator();
            if (!iterator.MoveNext() || iterator.Current is not ManagementObject obj) return null;
            using (obj)
            using (var args = obj.GetMethodParameters(command == 20 ? "GetFunction" : "SetFunction"))
            {
                args["uiInput"] = input;
                using var reply = obj.InvokeMethod(command == 20 ? "GetFunction" : "SetFunction", args, new InvokeMethodOptions { Timeout = TimeSpan.FromSeconds(2) });
                return reply?["uiOutput"] is object raw ? Convert.ToUInt64(raw) : null;
            }
        }
        catch { return null; }
    }
    internal static byte[] Encode(ushort command, ulong argument, bool wide)
    {
        if (!wide && argument > uint.MaxValue) throw new ArgumentOutOfRangeException(nameof(argument));
        byte[] packet = new byte[wide ? 15 : 11];
        BinaryPrimitives.WriteUInt16LittleEndian(packet, command);
        packet[2] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(3), wide ? 8 : 4);
        if (wide) BinaryPrimitives.WriteUInt64LittleEndian(packet.AsSpan(7), argument);
        else BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7), (uint)argument);
        return packet;
    }

    internal static async Task<ulong?> ReadReply(Stream stream, bool wide, CancellationToken token)
    {
        byte[] header = new byte[5];
        try
        {
            await stream.ReadExactlyAsync(header, token);
            int size = wide ? 8 : 4;
            if (header[0] != 1 || BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(1)) != size) return null;
            byte[] payload = new byte[size];
            await stream.ReadExactlyAsync(payload, token);
            return wide ? BinaryPrimitives.ReadUInt64LittleEndian(payload) : BinaryPrimitives.ReadUInt32LittleEndian(payload);
        }
        catch (EndOfStreamException) { return null; }
        catch (OperationCanceledException) { return null; }
    }

    internal static bool? CoolBoostState(ulong? reply) => reply.HasValue && (reply.Value & 255) == 0 && ((reply.Value >> 8) & 255) <= 1
        ? ((reply.Value >> 8) & 255) == 1 : null;

    internal static async Task<ulong?> Exchange(ushort command, ulong input, bool wide)
    {
        // Only recovered CoolBoost and Nitro 2022 OperationMode operations are implemented; no general-purpose firmware command entry point.
        bool allowed = (command == 20 && !wide && input == 519) ||
            (command == 17 && wide && input is 7 or 0x10007 && HardwareWriteGate.InScope(HardwareFeature.CoolBoost, Backend)) ||
            (command == CmdWmiGetGamingMiscSetting && !wide && input == MiscSubfunctionOperationMode) ||
            ((command == CmdSetOperationMode && !wide && input is ModeQuiet or ModeDefault or ModePerformance ||
              command == CmdWmiSetGamingMiscSetting && wide && input is 0x000B or 0x010B or 0x040B) &&
             HardwareWriteGate.InScope(HardwareFeature.Power, OperationModePipeBackend));
        if (!allowed) return null;
        bool isRead = command is 20 or CmdWmiGetGamingMiscSetting;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var pipe = new NamedPipeClientStream(".", "PredatorSense_service_namedpipe", PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            if (isRead) LastReadFailure = "Connecting";
            await pipe.ConnectAsync(deadline.Token);
            if (!AcerAgentEndpoint.PipeTrusted(pipe.SafePipeHandle, "PSSvc")) { if (isRead) LastReadFailure = "Pipe server is not the trusted PSSvc process"; return null; }
            if (isRead) LastReadFailure = "Awaiting reply";
            await pipe.WriteAsync(Encode(command, input, wide), deadline.Token);
            ulong? reply = await ReadReply(pipe, isRead, deadline.Token);
            if (isRead) LastReadFailure = reply.HasValue ? $"Firmware status={reply.Value & 255}, state={(reply.Value >> 8) & 255}" : "Malformed, truncated or timed-out reply";
            return reply;
        }
        catch (IOException) { if (isRead) LastReadFailure = "Pipe I/O failed"; return null; }
        catch (UnauthorizedAccessException) { if (isRead) LastReadFailure = "Pipe access denied"; return null; }
        catch (OperationCanceledException) { if (isRead) LastReadFailure = "Pipe exchange timed out"; return null; }
    }
}
