using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PredatorControlApp;

internal static class AcerAgentEndpoint
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);

    private const int AF_INET = 2;
    private const int TCP_TABLE_OWNER_PID_LISTENER = 3;
    private const int TCP_TABLE_OWNER_PID_CONNECTIONS = 4;

    internal static bool PipeTrusted(SafePipeHandle handle) => GetNamedPipeServerProcessId(handle, out uint pid) && OemServiceManager.IsTrustedAgentProcess(pid);
    internal static bool PipeTrusted(SafePipeHandle handle, string service) => GetNamedPipeServerProcessId(handle, out uint pid) && OemServiceManager.IsTrustedAgentProcess(pid, service);

    internal static bool TcpTrusted(int port) => TcpTrusted(port, out _);

    internal static bool TcpTrusted(int port, out uint listenerPid)
    {
        listenerPid = 0;
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_LISTENER, 0);
        if (size is < 4 or > 1048576) return false;
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buffer, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_LISTENER, 0) != 0) return false;
            int count = Marshal.ReadInt32(buffer);
            if (count < 0 || count > (size - 4) / 24) return false;
            for (int i = 0; i < count; i++)
            {
                IntPtr row = IntPtr.Add(buffer, 4 + i * 24);
                int localPort = (Marshal.ReadByte(row, 8) << 8) | Marshal.ReadByte(row, 9);
                uint address = unchecked((uint)Marshal.ReadInt32(row, 4));
                uint pid = unchecked((uint)Marshal.ReadInt32(row, 20));
                if (localPort == port && address is 0 or 0x0100007F && OemServiceManager.IsTrustedAgentProcess(pid))
                {
                    listenerPid = pid;
                    return true;
                }
            }
            return false;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    /// <summary>
    /// S5: Verifies that an already-connected loopback socket is owned on the server side by the trusted OEM agent process,
    /// closing the check-then-connect window between pre-connect listener inspection and ConnectAsync.
    /// </summary>
    internal static bool TcpConnectedToTrustedServer(Socket clientSocket, int serverPort, uint expectedServerPid)
    {
        if (clientSocket.LocalEndPoint is not IPEndPoint localEp) return false;
        int clientPort = localEp.Port;

        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_CONNECTIONS, 0);
        if (size is < 4 or > 1048576) return false;
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buffer, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_CONNECTIONS, 0) != 0) return false;
            int count = Marshal.ReadInt32(buffer);
            if (count < 0 || count > (size - 4) / 24) return false;
            for (int i = 0; i < count; i++)
            {
                IntPtr row = IntPtr.Add(buffer, 4 + i * 24);
                uint localAddr = unchecked((uint)Marshal.ReadInt32(row, 4));
                int localPort = (Marshal.ReadByte(row, 8) << 8) | Marshal.ReadByte(row, 9);
                uint remoteAddr = unchecked((uint)Marshal.ReadInt32(row, 12));
                int remotePort = (Marshal.ReadByte(row, 16) << 8) | Marshal.ReadByte(row, 17);
                uint pid = unchecked((uint)Marshal.ReadInt32(row, 20));

                // Server-side row of our loopback connection: localPort == serverPort, remotePort == clientPort
                if (localPort == serverPort && remotePort == clientPort &&
                    localAddr == 0x0100007F && remoteAddr == 0x0100007F)
                {
                    return pid == expectedServerPid || OemServiceManager.IsTrustedAgentProcess(pid);
                }
            }
            return false;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
}
