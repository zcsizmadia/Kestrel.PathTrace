using System.Runtime.Versioning;
using System.Text;

using Kestrel.PathTrace.Abstractions;

namespace Kestrel.PathTrace.Native.Linux;

/// <summary>
/// High-level managed wrapper around <c>hwtstamp_shim.so</c>.
/// Converts raw C structs into managed <see cref="Abstractions"/> types
/// and hides all P/Invoke error handling from callers.
/// </summary>
[SupportedOSPlatform("linux")]
public static class HwtstampInterop
{
    static HwtstampInterop() => NativeLibraryResolver.EnsureRegistered();

    // -----------------------------------------------------------------------
    // NIC capabilities
    // -----------------------------------------------------------------------

    /// <summary>
    /// Queries hardware-timestamping capabilities of the network interface.
    /// </summary>
    /// <param name="ifname">Interface name (e.g. "eth0").</param>
    /// <returns>Capabilities record, or <see langword="null"/> on failure.</returns>
    public static NicTimestampCapabilities? QueryNicCapabilities(string ifname)
    {
        int rc = HwtstampNative.QueryNicCapabilities(ifname, out HwtsNicCaps raw);

        if (rc != (int)HwtsError.Ok)
        {
            return null;
        }

        return new NicTimestampCapabilities
        {
            InterfaceName      = ifname,
            SoTimestampingFlags = raw.SoTimestampingFlags,
            PhcIndex           = raw.PhcIndex,
            TxTypes            = raw.TxTypes,
            RxFilters          = raw.RxFilters,
            HardwareRxAvailable = raw.HwRxAvailable  != 0,
            HardwareTxAvailable = raw.HwTxAvailable  != 0,
            SoftwareRxAvailable = raw.SwRxAvailable  != 0,
            SoftwareTxAvailable = raw.SwTxAvailable  != 0,
            RawHardwareAvailable = raw.RawHwAvailable != 0,
        };
    }

    // -----------------------------------------------------------------------
    // NIC configuration
    // -----------------------------------------------------------------------

    /// <summary>
    /// Configures hardware timestamping on a network interface.
    /// Requires <c>CAP_NET_ADMIN</c>.
    /// </summary>
    /// <param name="ifname">Interface name.</param>
    /// <param name="txType">Desired TX timestamp type.</param>
    /// <param name="rxFilter">Desired RX filter.</param>
    /// <param name="actualTxType">Kernel-applied TX type (may differ from requested).</param>
    /// <param name="actualRxFilter">Kernel-applied RX filter (may differ from requested).</param>
    /// <returns><see langword="true"/> on success.</returns>
    public static bool ConfigureNic(
        string           ifname,
        HwtstampTxType   txType,
        HwtstampRxFilter rxFilter,
        out HwtstampTxType   actualTxType,
        out HwtstampRxFilter actualRxFilter)
    {
        HwtsNicConfig cfg = new()
        {
            TxType   = (int)txType,
            RxFilter = (int)rxFilter,
        };

        int rc = HwtstampNative.ConfigureNic(ifname, ref cfg);

        actualTxType   = (HwtstampTxType)cfg.TxType;
        actualRxFilter = (HwtstampRxFilter)cfg.RxFilter;

        return rc == (int)HwtsError.Ok;
    }

    // -----------------------------------------------------------------------
    // Socket-level timestamping
    // -----------------------------------------------------------------------

    /// <summary>
    /// Enables SO_TIMESTAMPING on a socket file descriptor.
    /// </summary>
    /// <param name="fd">Linux socket file descriptor.</param>
    /// <param name="flags">Combination of <see cref="SoTimestampingFlags"/> bits.</param>
    /// <returns><see langword="true"/> on success.</returns>
    public static bool EnableSocketTimestamps(int fd, SoTimestampingFlags flags)
    {
        int rc = HwtstampNative.EnableSocketTimestamps(fd, (uint)flags);
        return rc == (int)HwtsError.Ok;
    }

    /// <summary>
    /// Enables the best available timestamping on a socket: hardware RX when
    /// the NIC supports it, otherwise software.
    /// </summary>
    /// <param name="fd">Linux socket file descriptor.</param>
    /// <param name="caps">
    /// Previously queried NIC capabilities.  Pass <see langword="null"/> to
    /// use software-only timestamping unconditionally.
    /// </param>
    /// <returns>The flags that were applied.</returns>
    public static SoTimestampingFlags EnableBestAvailableTimestamps(
        int fd,
        NicTimestampCapabilities? caps)
    {
        SoTimestampingFlags flags = caps?.IsFullHardwareTimestampingAvailable == true
            ? SoTimestampingFlags.RecommendedHwRx
            : SoTimestampingFlags.RecommendedSwOnly;

        EnableSocketTimestamps(fd, flags);
        return flags;
    }

    // -----------------------------------------------------------------------
    // RX timestamp extraction
    // -----------------------------------------------------------------------

    /// <summary>
    /// Performs <c>recvmsg()</c> and extracts any attached timestamps.
    /// </summary>
    /// <param name="fd">Socket file descriptor.</param>
    /// <param name="buffer">Data buffer to receive into.</param>
    /// <param name="timestamps">Extracted timestamps (check <c>HasAny</c>).</param>
    /// <returns>Number of bytes received, or -1 on error.</returns>
    public static unsafe int RecvWithTimestamp(
        int fd,
        Span<byte> buffer,
        out PacketTimestamps timestamps)
    {
        fixed (byte* ptr = buffer)
        {
            int rc = HwtstampNative.RecvmsgWithTimestamp(
                fd, ptr, (nuint)buffer.Length, out HwtsRxResult result);

            if (rc != (int)HwtsError.Ok)
            {
                timestamps = default;
                return -1;
            }

            timestamps = ToPacketTimestamps(result.Timestamps);
            return (int)result.BytesReceived;
        }
    }

    // -----------------------------------------------------------------------
    // TX timestamp extraction
    // -----------------------------------------------------------------------

    /// <summary>
    /// Reads a TX hardware timestamp from the socket error queue.
    /// Call after <c>poll()</c> signals <c>POLLERR</c> on the socket.
    /// </summary>
    /// <param name="fd">Socket file descriptor.</param>
    /// <param name="timestamps">Extracted timestamps on success.</param>
    /// <returns>
    /// <see cref="HwtsError.Ok"/> on success,
    /// <see cref="HwtsError.NoTimestamp"/> if not yet available,
    /// or another error code.
    /// </returns>
    public static HwtsError ReadTxTimestamp(int fd, out PacketTimestamps timestamps)
    {
        int rc = HwtstampNative.ReadTxTimestamp(fd, out HwtsTimestamps raw);
        timestamps = rc == (int)HwtsError.Ok ? ToPacketTimestamps(raw) : default;
        return (HwtsError)rc;
    }

    // -----------------------------------------------------------------------
    // Clock calibration
    // -----------------------------------------------------------------------

    /// <summary>
    /// Samples CLOCK_MONOTONIC, CLOCK_REALTIME, CLOCK_TAI, and
    /// CLOCK_MONOTONIC_RAW in rapid succession.
    /// </summary>
    public static ClockCalibration SampleClocks()
    {
        int rc = HwtstampNative.SampleClocks(out HwtsClockSample raw);

        if (rc != (int)HwtsError.Ok)
        {
            return default;
        }

        return new ClockCalibration
        {
            MonotonicNs     = raw.MonotonicNs,
            RealtimeNs      = raw.RealtimeNs,
            TaiNs           = raw.TaiNs,
            RawMonotonicNs  = raw.RawMonotonicNs,
        };
    }

    // -----------------------------------------------------------------------
    // Interface name resolution
    // -----------------------------------------------------------------------

    /// <summary>
    /// Resolves the network interface name for a connected socket file descriptor.
    /// </summary>
    /// <param name="fd">Socket file descriptor.</param>
    /// <returns>Interface name (e.g. "eth0"), or an empty string on failure.</returns>
    public static string GetSocketInterfaceName(int fd)
    {
        const int IfNameSiz = 16;
        unsafe
        {
            byte* buf = stackalloc byte[IfNameSiz];
            int rc = HwtstampNative.GetSocketIfname(fd, buf, (nuint)IfNameSiz);
            return rc == (int)HwtsError.Ok
                ? System.Text.Encoding.UTF8.GetString(buf, IfNameSiz).TrimEnd('\0')
                : string.Empty;
        }
    }

    // -----------------------------------------------------------------------
    // PTP Hardware Clock
    // -----------------------------------------------------------------------

    /// <summary>
    /// Opens the PTP Hardware Clock device for the given PHC index.
    /// </summary>
    /// <param name="phcIndex">PHC index from <see cref="NicTimestampCapabilities.PhcIndex"/>.</param>
    /// <returns>
    /// A valid file descriptor on success.  The caller must close it.
    /// Returns -1 on failure.
    /// </returns>
    public static int OpenPhc(int phcIndex) =>
        HwtstampNative.OpenPhc(phcIndex);

    /// <summary>
    /// Reads the current time from an open PHC file descriptor.
    /// </summary>
    /// <param name="phcFd">File descriptor returned by <see cref="OpenPhc"/>.</param>
    /// <param name="timestamp">Current PHC clock value on success.</param>
    /// <returns><see langword="true"/> on success.</returns>
    public static bool TryReadPhcTime(int phcFd, out HardwareTimestamp timestamp)
    {
        int rc = HwtstampNative.ReadPhcTime(phcFd, out HwtsTimespec raw);

        if (rc != (int)HwtsError.Ok)
        {
            timestamp = HardwareTimestamp.Invalid;
            return false;
        }

        timestamp = ToHardwareTimestamp(raw);
        return true;
    }

    // -----------------------------------------------------------------------
    // Private conversion helpers
    // -----------------------------------------------------------------------

    private static HardwareTimestamp ToHardwareTimestamp(HwtsTimespec ts) =>
        new()
        {
            Seconds     = ts.TvSec,
            Nanoseconds = ts.TvNsec,
            IsValid     = ts.Valid != 0,
        };

    private static PacketTimestamps ToPacketTimestamps(HwtsTimestamps raw) =>
        new()
        {
            Software      = ToHardwareTimestamp(raw.Sw),
            HardwareLegacy = ToHardwareTimestamp(raw.HwLegacy),
            HardwareRaw   = ToHardwareTimestamp(raw.HwRaw),
        };
}
