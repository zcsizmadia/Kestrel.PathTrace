using System.Runtime.InteropServices;

namespace Kestrel.PathTrace.Native.Linux;

/// <summary>
/// Mirrors <c>hwts_nic_caps</c> from <c>hwtstamp_shim.h</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct HwtsNicCaps
{
    public uint  SoTimestampingFlags;
    public int   PhcIndex;
    public uint  TxTypes;
    public uint  RxFilters;
    public byte  HwRxAvailable;
    public byte  HwTxAvailable;
    public byte  SwRxAvailable;
    public byte  SwTxAvailable;
    public byte  RawHwAvailable;
}

/// <summary>
/// Mirrors <c>hwts_nic_config</c> from <c>hwtstamp_shim.h</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct HwtsNicConfig
{
    public int TxType;
    public int RxFilter;
}

/// <summary>
/// Mirrors <c>hwts_timespec</c> from <c>hwtstamp_shim.h</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct HwtsTimespec
{
    public long  TvSec;
    public long  TvNsec;
    public byte  Valid;
}

/// <summary>
/// Mirrors <c>hwts_timestamps</c> (the three-tuple) from <c>hwtstamp_shim.h</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct HwtsTimestamps
{
    public HwtsTimespec Sw;
    public HwtsTimespec HwLegacy;
    public HwtsTimespec HwRaw;
}

/// <summary>
/// Mirrors <c>hwts_clock_sample</c> from <c>hwtstamp_shim.h</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct HwtsClockSample
{
    public long MonotonicNs;
    public long RealtimeNs;
    public long TaiNs;
    public long RawMonotonicNs;
}

/// <summary>
/// Mirrors <c>hwts_rx_result</c> from <c>hwtstamp_shim.h</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct HwtsRxResult
{
    public nint          BytesReceived;   /* ssize_t */
    public HwtsTimestamps Timestamps;
    public int            LastErrno;
}
