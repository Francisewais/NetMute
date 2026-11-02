using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace NetMute
{
    // Reads and closes TCP connections, for the connection count in the UI and so that a
    // program loses its open connections the moment it is blocked.
    internal static class NetStat
    {
        private const int AfInet = 2;
        private const int AfInet6 = 23;
        private const int TcpTableOwnerPidAll = 5;
        private const uint ErrorInsufficientBuffer = 122;

        private const uint StateSynSent = 3;
        private const uint StateLastAck = 10;
        private const uint StateDeleteTcb = 12;

        // MIB_TCPROW_OWNER_PID: state, localAddr, localPort, remoteAddr, remotePort, pid
        private const int RowSizeV4 = 24;
        private const int V4RemoteAddr = 12;
        private const int V4RemotePort = 16;
        private const int V4Pid = 20;

        // MIB_TCP6ROW_OWNER_PID
        private const int RowSizeV6 = 56;
        private const int V6RemoteAddr = 24;
        private const int V6RemotePort = 44;
        private const int V6State = 48;
        private const int V6Pid = 52;

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_TCPROW
        {
            public uint state;
            public uint localAddr;
            public uint localPort;
            public uint remoteAddr;
            public uint remotePort;
        }

        [DllImport("iphlpapi.dll")]
        private static extern uint GetExtendedTcpTable(
            IntPtr table, ref int size, bool sorted, int family, int tableClass, uint reserved);

        [DllImport("iphlpapi.dll")]
        private static extern uint SetTcpEntry(ref MIB_TCPROW row);

        // Open connections per process that leave the machine, directly or through a local
        // proxy. Loopback traffic between a program's own parts does not count.
        public static Dictionary<int, int> ActiveByPid()
        {
            Dictionary<int, int> counts = new Dictionary<int, int>();
            HashSet<ushort> proxyPorts = new HashSet<ushort>(LocalProxy.Ports());

            byte[] v4 = ReadTable(AfInet);
            for (int row = 0; row < RowCount(v4, RowSizeV4); row++)
            {
                int o = 4 + row * RowSizeV4;
                if (IsOpen(BitConverter.ToUInt32(v4, o)) && IsExternal(v4[o + V4RemoteAddr] == 127, Port(v4, o + V4RemotePort), proxyPorts))
                    Increment(counts, BitConverter.ToInt32(v4, o + V4Pid));
            }

            byte[] v6 = ReadTable(AfInet6);
            for (int row = 0; row < RowCount(v6, RowSizeV6); row++)
            {
                int o = 4 + row * RowSizeV6;
                if (IsOpen(BitConverter.ToUInt32(v6, o + V6State)) && IsExternal(IsLoopbackV6(v6, o + V6RemoteAddr), Port(v6, o + V6RemotePort), proxyPorts))
                    Increment(counts, BitConverter.ToInt32(v6, o + V6Pid));
            }
            return counts;
        }

        // Windows can only do this for IPv4. An IPv6 connection dies when its next packet is
        // checked against the new rules, which WFP does on its own after a rule change.
        public static int CloseConnections(ICollection<int> pids)
        {
            HashSet<ushort> proxyPorts = new HashSet<ushort>(LocalProxy.Ports());
            byte[] v4 = ReadTable(AfInet);
            int closed = 0;
            for (int i = 0; i < RowCount(v4, RowSizeV4); i++)
            {
                int o = 4 + i * RowSizeV4;
                if (!IsOpen(BitConverter.ToUInt32(v4, o)) || !pids.Contains(BitConverter.ToInt32(v4, o + V4Pid)))
                    continue;
                if (!IsExternal(v4[o + V4RemoteAddr] == 127, Port(v4, o + V4RemotePort), proxyPorts))
                    continue;

                MIB_TCPROW row = new MIB_TCPROW();
                row.state = StateDeleteTcb;
                row.localAddr = BitConverter.ToUInt32(v4, o + 4);
                row.localPort = BitConverter.ToUInt32(v4, o + 8) & 0xFFFF;
                row.remoteAddr = BitConverter.ToUInt32(v4, o + V4RemoteAddr);
                row.remotePort = BitConverter.ToUInt32(v4, o + V4RemotePort) & 0xFFFF;
                if (SetTcpEntry(ref row) == 0)
                    closed++;
            }
            return closed;
        }

        private static bool IsOpen(uint state)
        {
            return state >= StateSynSent && state <= StateLastAck;
        }

        private static bool IsExternal(bool remoteIsLoopback, ushort remotePort, HashSet<ushort> proxyPorts)
        {
            return !remoteIsLoopback || proxyPorts.Contains(remotePort);
        }

        // Ports sit in the table in network byte order.
        private static ushort Port(byte[] table, int offset)
        {
            return (ushort)(table[offset] << 8 | table[offset + 1]);
        }

        // ::1 or an IPv4-mapped ::ffff:127.x.x.x
        private static bool IsLoopbackV6(byte[] table, int o)
        {
            for (int i = 0; i < 10; i++)
                if (table[o + i] != 0)
                    return false;
            if (table[o + 10] == 0xFF && table[o + 11] == 0xFF)
                return table[o + 12] == 127;
            for (int i = 10; i < 15; i++)
                if (table[o + i] != 0)
                    return false;
            return table[o + 15] == 1;
        }

        private static void Increment(Dictionary<int, int> counts, int pid)
        {
            int count;
            counts.TryGetValue(pid, out count);
            counts[pid] = count + 1;
        }

        private static int RowCount(byte[] table, int rowSize)
        {
            return Math.Min((int)BitConverter.ToUInt32(table, 0), (table.Length - 4) / rowSize);
        }

        // The table can grow between asking for its size and reading it, hence the loop.
        // A family that is not available reads as an empty table.
        private static byte[] ReadTable(int family)
        {
            int size = 0;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                int allocated = size;
                IntPtr buffer = allocated > 0 ? Marshal.AllocHGlobal(allocated) : IntPtr.Zero;
                try
                {
                    uint code = GetExtendedTcpTable(buffer, ref size, false, family, TcpTableOwnerPidAll, 0);
                    if (code == 0 && allocated > 0)
                    {
                        byte[] table = new byte[allocated];
                        Marshal.Copy(buffer, table, 0, allocated);
                        return table;
                    }
                    if (code != ErrorInsufficientBuffer)
                        break;
                }
                finally
                {
                    if (buffer != IntPtr.Zero)
                        Marshal.FreeHGlobal(buffer);
                }
            }
            return new byte[4];
        }
    }
}
