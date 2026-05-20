using System.Runtime.InteropServices;

namespace Kestrel.PathTrace.Native.Windows;

/// <summary>
/// Mirrors the <c>tcp_info_v0_shim</c> C struct from <c>tcpinfo_shim.dll</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct TcpInfoV0
{
    public uint State;
    public uint Mss;
    public uint ConnectionTimeMs;
    public uint TimestampsEnabled;
    public uint RttUs;
    public uint MinRttUs;
    public uint BytesInFlight;
    public uint Cwnd;
    public uint SndWnd;
    public uint RcvWnd;
    public uint RcvBuf;
    public uint BytesOut;
    public uint BytesIn;
    public uint BytesReordered;
    public uint BytesRetrans;
    public uint FastRetrans;
    public uint DupAcksIn;
    public uint TimeoutEpisodes;
    public byte SynRetrans;
}
