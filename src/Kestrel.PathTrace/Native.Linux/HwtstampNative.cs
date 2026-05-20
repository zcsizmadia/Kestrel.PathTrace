using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Kestrel.PathTrace.Native.Linux;

/// <summary>
/// P/Invoke declarations for <c>libhwtstamp_shim.so</c>.
/// All entry points are gated behind <see cref="OperatingSystem.IsLinux()"/>.
/// </summary>
[SupportedOSPlatform("linux")]
internal static partial class HwtstampNative
{
    private const string LibName = "hwtstamp_shim";

    [LibraryImport(LibName, EntryPoint = "hwts_query_nic_capabilities",
        StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int QueryNicCapabilities(
        string ifname,
        out HwtsNicCaps caps);

    [LibraryImport(LibName, EntryPoint = "hwts_configure_nic",
        StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int ConfigureNic(
        string ifname,
        ref HwtsNicConfig config);

    [LibraryImport(LibName, EntryPoint = "hwts_enable_socket_timestamps")]
    internal static partial int EnableSocketTimestamps(
        int fd,
        uint flags);

    [LibraryImport(LibName, EntryPoint = "hwts_recvmsg_with_timestamp")]
    internal static unsafe partial int RecvmsgWithTimestamp(
        int fd,
        void* buf,
        nuint bufLen,
        out HwtsRxResult result);

    [LibraryImport(LibName, EntryPoint = "hwts_read_tx_timestamp")]
    internal static partial int ReadTxTimestamp(
        int fd,
        out HwtsTimestamps timestamps);

    [LibraryImport(LibName, EntryPoint = "hwts_sample_clocks")]
    internal static partial int SampleClocks(out HwtsClockSample sample);

    [LibraryImport(LibName, EntryPoint = "hwts_get_socket_ifname")]
    internal static unsafe partial int GetSocketIfname(
        int fd,
        byte* ifname,
        nuint ifnameLen);

    [LibraryImport(LibName, EntryPoint = "hwts_open_phc")]
    internal static partial int OpenPhc(int phcIndex);

    [LibraryImport(LibName, EntryPoint = "hwts_read_phc_time")]
    internal static partial int ReadPhcTime(
        int phcFd,
        out HwtsTimespec ts);
}
