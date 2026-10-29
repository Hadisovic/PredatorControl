using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PredatorControlApp;

internal static class AcerAgentEndpoint
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);

    internal static bool PipeTrusted(SafePipeHandle handle) => GetNamedPipeServerProcessId(handle, out uint pid) && OemServiceManager.IsTrustedAgentProcess(pid);
    internal static bool PipeTrusted(SafePipeHandle handle, string service) => GetNamedPipeServerProcessId(handle, out uint pid) && OemServiceManager.IsTrustedAgentProcess(pid, service);
    internal static bool TcpTrusted(int port)
    {
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 3, 0); // AF_INET, OWNER_PID_LISTENER
        if (size is < 4 or > 1048576) return false;
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buffer, ref size, false, 2, 3, 0) != 0) return false;
            int count = Marshal.ReadInt32(buffer);
            if (count < 0 || count > (size - 4) / 24) return false;
            for (int i = 0; i < count; i++)
            {
                IntPtr row = IntPtr.Add(buffer, 4 + i * 24);
                int localPort = (Marshal.ReadByte(row, 8) << 8) | Marshal.ReadByte(row, 9);
                uint address = unchecked((uint)Marshal.ReadInt32(row, 4));
                if (localPort == port && address is 0 or 0x0100007F && OemServiceManager.IsTrustedAgentProcess(unchecked((uint)Marshal.ReadInt32(row, 20)))) return true;
            }
            return false;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
}
