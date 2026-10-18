using System.Runtime.InteropServices;

namespace UniLink.Windows;

// Driver-reported properties, rather than adapter names (a TAP driver may report Ethernet).
internal static class PhysicalNetworkInterface
{
    internal readonly record struct Facts(int Index, Guid Id, uint Type, uint TunnelType, byte Flags, uint OperStatus);
    internal static bool IsEligible(Facts info) => info.Index is > 0 and <= 0x00ffffff && info.Type is 6 or 71
        && info.TunnelType == 0 && (info.Flags & 3) == 1 && info.OperStatus == 1;

    internal static bool TryRead(int index, out Facts facts)
    {
        facts = default;
        if (!OperatingSystem.IsWindows() || index <= 0) return false;
        var row = new MibIfRow2 { InterfaceIndex = (uint)index, Alias = "", Description = "", PhysicalAddress = new byte[32], PermanentPhysicalAddress = new byte[32] };
        if (GetIfEntry2(ref row) != 0) return false;
        facts = new(index, row.InterfaceGuid, row.Type, row.TunnelType, row.Flags, row.OperStatus);
        return true;
    }

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint GetIfEntry2(ref MibIfRow2 row);

    // MIB_IF_ROW2, netioapi.h. Inline strings contain 257 UTF-16 code units.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MibIfRow2
    {
        public ulong InterfaceLuid;
        public uint InterfaceIndex;
        public Guid InterfaceGuid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string Alias;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string Description;
        public uint PhysicalAddressLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] PhysicalAddress;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] PermanentPhysicalAddress;
        public uint Mtu, Type, TunnelType, MediaType, PhysicalMediumType, AccessType, DirectionType;
        public byte Flags;
        public uint OperStatus, AdminStatus, MediaConnectState;
        public Guid NetworkGuid;
        public uint ConnectionType;
        public ulong TransmitLinkSpeed, ReceiveLinkSpeed, InOctets, InUcastPkts, InNUcastPkts, InDiscards, InErrors, InUnknownProtos;
        public ulong InUcastOctets, InMulticastOctets, InBroadcastOctets, OutOctets, OutUcastPkts, OutNUcastPkts, OutDiscards, OutErrors;
        public ulong OutUcastOctets, OutMulticastOctets, OutBroadcastOctets, OutQLen;
    }
}
