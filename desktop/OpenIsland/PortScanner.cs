using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;

namespace OpenIsland;

/// <summary>
/// Слушающие TCP/UDP-порты и их владельцы через IP Helper API
/// (<c>GetExtendedTcpTable</c> / <c>GetExtendedUdpTable</c>).
/// Читаются только таблицы «listener + owner pid», поэтому результат — это
/// именно занятые локальные порты, а не все соединения.
/// </summary>
static class PortScanner
{
    const int AfInet = 2, AfInet6 = 23;
    const int TcpTableOwnerPidListener = 3;
    const int UdpTableOwnerPid = 1;
    const uint ErrorInsufficientBuffer = 122;

    /// <summary>PID → отсортированные порты, которые процесс слушает.</summary>
    public static IReadOnlyDictionary<int, IReadOnlyList<int>> ListenersByPid()
    {
        var ports = new Dictionary<int, HashSet<int>>();

        try
        {
            ReadTcp(ports, AfInet, Marshal.SizeOf<MibTcpRow>(), ReadTcpRow);
        }
        catch (Exception ex)
        {
            App.LogDebug($"Ports: IPv4 TCP table failed ({ex.Message}).");
        }

        try
        {
            ReadTcp(ports, AfInet6, Marshal.SizeOf<MibTcp6Row>(), ReadTcp6Row);
        }
        catch (Exception ex)
        {
            App.LogDebug($"Ports: IPv6 TCP table failed ({ex.Message}).");
        }

        try
        {
            ReadUdp(ports, AfInet, Marshal.SizeOf<MibUdpRow>(), ReadUdpRow);
        }
        catch (Exception ex)
        {
            App.LogDebug($"Ports: IPv4 UDP table failed ({ex.Message}).");
        }

        try
        {
            ReadUdp(ports, AfInet6, Marshal.SizeOf<MibUdp6Row>(), ReadUdp6Row);
        }
        catch (Exception ex)
        {
            App.LogDebug($"Ports: IPv6 UDP table failed ({ex.Message}).");
        }

        return ports.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<int>)pair.Value.OrderBy(port => port).ToList());
    }

    // ------------------------------------------------------------------ таблицы

    delegate uint TableQuery(IntPtr table, ref int size);

    static void ReadTcp(Dictionary<int, HashSet<int>> ports, int family, int rowSize, Action<IntPtr, Dictionary<int, HashSet<int>>> readRow)
    {
        IntPtr table = Query((IntPtr buffer, ref int size) => GetExtendedTcpTable(buffer, ref size, false, family, TcpTableOwnerPidListener, 0));
        if (table == IntPtr.Zero) return;
        try
        {
            ReadRows(table, rowSize, readRow, ports);
        }
        finally
        {
            Marshal.FreeHGlobal(table);
        }
    }

    static void ReadUdp(Dictionary<int, HashSet<int>> ports, int family, int rowSize, Action<IntPtr, Dictionary<int, HashSet<int>>> readRow)
    {
        IntPtr table = Query((IntPtr buffer, ref int size) => GetExtendedUdpTable(buffer, ref size, false, family, UdpTableOwnerPid, 0));
        if (table == IntPtr.Zero) return;
        try
        {
            ReadRows(table, rowSize, readRow, ports);
        }
        finally
        {
            Marshal.FreeHGlobal(table);
        }
    }

    static void ReadRows(IntPtr table, int rowSize, Action<IntPtr, Dictionary<int, HashSet<int>>> readRow, Dictionary<int, HashSet<int>> ports)
    {
        int count = Marshal.ReadInt32(table);
        IntPtr row = IntPtr.Add(table, sizeof(int));
        for (int i = 0; i < count; i++, row = IntPtr.Add(row, rowSize))
        {
            readRow(row, ports);
        }
    }

    /// <summary>
    /// Двухшаговый вызов IP Helper: сначала узнать размер, затем заполнить буфер.
    /// Таблица может подрасти между вызовами — тогда пробуем ещё раз.
    /// </summary>
    static IntPtr Query(TableQuery query)
    {
        int size = 0;
        uint result = query(IntPtr.Zero, ref size);
        if (result == 0) return IntPtr.Zero;
        if (result != ErrorInsufficientBuffer) throw new Win32Exception((int)result);

        for (int attempt = 0; attempt < 3; attempt++)
        {
            IntPtr buffer = Marshal.AllocHGlobal(size);
            uint again = query(buffer, ref size);
            if (again == 0) return buffer;
            Marshal.FreeHGlobal(buffer);
            if (again != ErrorInsufficientBuffer) throw new Win32Exception((int)again);
        }

        throw new Win32Exception((int)ErrorInsufficientBuffer);
    }

    static void ReadTcpRow(IntPtr row, Dictionary<int, HashSet<int>> ports)
    {
        MibTcpRow value = Marshal.PtrToStructure<MibTcpRow>(row);
        Add(ports, (int)value.OwningPid, PortOf(value.LocalPort));
    }

    static void ReadTcp6Row(IntPtr row, Dictionary<int, HashSet<int>> ports)
    {
        MibTcp6Row value = Marshal.PtrToStructure<MibTcp6Row>(row);
        Add(ports, (int)value.OwningPid, PortOf(value.LocalPort));
    }

    static void ReadUdpRow(IntPtr row, Dictionary<int, HashSet<int>> ports)
    {
        MibUdpRow value = Marshal.PtrToStructure<MibUdpRow>(row);
        Add(ports, (int)value.OwningPid, PortOf(value.LocalPort));
    }

    static void ReadUdp6Row(IntPtr row, Dictionary<int, HashSet<int>> ports)
    {
        MibUdp6Row value = Marshal.PtrToStructure<MibUdp6Row>(row);
        Add(ports, (int)value.OwningPid, PortOf(value.LocalPort));
    }

    static void Add(Dictionary<int, HashSet<int>> ports, int pid, int port)
    {
        if (pid <= 0 || port <= 0 || port > 65535) return;
        if (!ports.TryGetValue(pid, out HashSet<int>? list))
        {
            list = [];
            ports[pid] = list;
        }
        list.Add(port);
    }

    /// <summary>Порт в таблице хранится в сетевом порядке в младших 16 битах.</summary>
    static int PortOf(uint raw) => (ushort)IPAddress.NetworkToHostOrder((short)(raw & 0xFFFF));

    // ------------------------------------------------------------------ P/Invoke

    [DllImport("iphlpapi.dll", SetLastError = true)]
    static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    static extern uint GetExtendedUdpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);

    [StructLayout(LayoutKind.Sequential)]
    struct MibTcpRow
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MibTcp6Row
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] LocalAddr;
        public uint LocalScopeId;
        public uint LocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] RemoteAddr;
        public uint RemotePort;
        public uint State;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MibUdpRow
    {
        public uint LocalAddr;
        public uint LocalPort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MibUdp6Row
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] LocalAddr;
        public uint LocalScopeId;
        public uint LocalPort;
        public uint OwningPid;
    }
}
