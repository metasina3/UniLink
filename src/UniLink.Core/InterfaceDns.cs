using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace UniLink.Core;

// Literal university IPs never invoke DNS. Windows hostname queries specify the same base interface.
internal static class InterfaceDns
{
    private static readonly SemaphoreSlim WindowsQueryGate = new(1, 1);
    internal static async Task<IPAddress[]> ResolveAsync(string host, int interfaceIndex, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (IPAddress.TryParse(host, out var literal)) return [literal];
        if (!OperatingSystem.IsWindows()) return await Dns.GetHostAddressesAsync(host, ct);
        if (interfaceIndex is <= 0 or > 0x00ffffff) throw new SocketException((int)SocketError.InvalidArgument);
        await WindowsQueryGate.WaitAsync(ct);
        Task<IPAddress[]> query;
        try { query = Task.Run(() => QueryWindows(host, interfaceIndex)); }
        catch { WindowsQueryGate.Release(); throw; }
        // DnsQueryEx is synchronous on this worker. A canceled HTTP request need not wait for Windows DNS;
        // the gate stays held until the native call finishes, bounding background DNS workers to one.
        _ = query.ContinueWith(task => { _ = task.Exception; WindowsQueryGate.Release(); }, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return await query.WaitAsync(ct);
    }
    private static IPAddress[] QueryWindows(string host, int interfaceIndex)
    {
        var request = new QueryRequest { Version = 1, QueryName = host, QueryType = 1, InterfaceIndex = (uint)interfaceIndex };
        var result = new QueryResult { Version = 1 };
        try
        {
            var status = DnsQueryEx(ref request, ref result, IntPtr.Zero);
            if (status != 0 || result.QueryStatus != 0) throw new SocketException(unchecked((int)(status != 0 ? status : result.QueryStatus)));
            var addresses = ReadAddresses(result.Records);
            if (addresses.Length == 0) throw new SocketException((int)SocketError.NoData);
            return addresses;
        }
        finally { if (result.Records != IntPtr.Zero) DnsRecordListFree(result.Records, 1); }
    }
    internal static IPAddress[] ReadAddresses(IntPtr first)
    {
        var addresses = new List<IPAddress>(); var visited = new HashSet<IntPtr>(); var headerSize = Marshal.SizeOf<RecordHeader>();
        for (var current = first; current != IntPtr.Zero && visited.Count < 256 && visited.Add(current);)
        {
            var header = Marshal.PtrToStructure<RecordHeader>(current);
            if (header.Type == 1 && header.DataLength >= 4)
            {
                var bytes = new byte[4]; Marshal.Copy(IntPtr.Add(current, headerSize), bytes, 0, 4);
                addresses.Add(new IPAddress(bytes));
            }
            current = header.Next;
        }
        return addresses.Distinct().ToArray();
    }
    [DllImport("dnsapi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint DnsQueryEx(ref QueryRequest request, ref QueryResult result, IntPtr cancelHandle);
    [DllImport("dnsapi.dll", ExactSpelling = true)] private static extern void DnsRecordListFree(IntPtr records, int freeType);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct QueryRequest
    {
        public uint Version;
        [MarshalAs(UnmanagedType.LPWStr)] public string QueryName;
        public ushort QueryType;
        public ulong QueryOptions;
        public IntPtr DnsServerList;
        public uint InterfaceIndex;
        public IntPtr CompletionCallback, QueryContext;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct QueryResult
    {
        public uint Version, QueryStatus;
        public ulong QueryOptions;
        public IntPtr Records, Reserved;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct RecordHeader
    {
        public IntPtr Next, Name;
        public ushort Type, DataLength;
        public uint Flags, Ttl, Reserved;
    }
}
