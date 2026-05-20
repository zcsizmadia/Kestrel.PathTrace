namespace Kestrel.PathTrace.Native.Linux;

/// <summary>
/// Error codes returned by <c>hwtstamp_shim.so</c>.
/// Must match the <c>HWTS_ERR_*</c> constants in <c>hwtstamp_shim.h</c>.
/// </summary>
public enum HwtsError : int
{
    Ok             =  0,
    NullArg        = -1,
    Unsupported    = -2,
    Ioctl          = -3,
    Socket         = -4,
    Recvmsg        = -5,
    NoTimestamp    = -6,
    BufTooSmall    = -7,
    NotLinux       = -8,
}

/// <summary>
/// SO_TIMESTAMPING flag bits that can be combined and passed to
/// <see cref="HwtstampInterop.EnableSocketTimestamps"/>.
/// Must match the <c>HWTS_SO_FLAG_*</c> constants in <c>hwtstamp_shim.h</c>.
/// </summary>
[Flags]
public enum SoTimestampingFlags : uint
{
    None           = 0,
    TxHardware     = 1u << 0,
    TxSoftware     = 1u << 1,
    RxHardware     = 1u << 2,
    RxSoftware     = 1u << 3,
    Software       = 1u << 4,
    SysHardware    = 1u << 5,  // deprecated
    RawHardware    = 1u << 6,
    OptId          = 1u << 7,
    OptCmsg        = 1u << 10,
    OptTsOnly      = 1u << 11,
    OptStats       = 1u << 12,
    OptPktInfo     = 1u << 13,
    OptTxSwhw      = 1u << 14,

    /// <summary>
    /// Recommended flag set for hardware RX timestamping with software fallback.
    /// </summary>
    RecommendedHwRx =
        RxHardware | RawHardware | RxSoftware | Software,

    /// <summary>
    /// Recommended flag set for software-only timestamping (no NIC HW required).
    /// </summary>
    RecommendedSwOnly =
        RxSoftware | Software,
}

/// <summary>TX timestamp type constants (hwtstamp_tx_types).</summary>
public enum HwtstampTxType : int
{
    Off            = 0,
    On             = 1,
    OneStepSync    = 2,
}

/// <summary>RX filter constants (hwtstamp_rx_filters).</summary>
public enum HwtstampRxFilter : int
{
    None              = 0,
    All               = 1,
    Some              = 2,
    PtpV1L4Event      = 3,
    PtpV1L4Sync       = 4,
    PtpV1L4DelayReq   = 5,
    PtpV2L4Event      = 6,
    PtpV2L4Sync       = 7,
    PtpV2L4DelayReq   = 8,
    PtpV2L2Event      = 9,
    PtpV2L2Sync       = 10,
    PtpV2L2DelayReq   = 11,
    PtpV2Event        = 12,
    PtpV2Sync         = 13,
    PtpV2DelayReq     = 14,
}
