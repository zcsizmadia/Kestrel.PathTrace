namespace Kestrel.PathTrace.Abstractions;

/// <summary>
/// Three-tuple of timestamps attached to a single packet event:
/// software clock, legacy HW (deprecated, usually zero), and raw NIC/PHC clock.
/// </summary>
public readonly record struct PacketTimestamps
{
    /// <summary>
    /// CLOCK_REALTIME software timestamp set by the kernel receive path
    /// (SOF_TIMESTAMPING_SOFTWARE / SOF_TIMESTAMPING_RX_SOFTWARE).
    /// </summary>
    public HardwareTimestamp Software { get; init; }

    /// <summary>
    /// Deprecated hardware-to-system-clock converted timestamp
    /// (SOF_TIMESTAMPING_SYS_HARDWARE).  Usually zeroed by modern kernels.
    /// </summary>
    public HardwareTimestamp HardwareLegacy { get; init; }

    /// <summary>
    /// Raw NIC / PHC clock timestamp (SOF_TIMESTAMPING_RAW_HARDWARE).
    /// Uses the NIC's own free-running or PTP-synchronized clock.
    /// Correlate with <see cref="ClockCalibration"/> to convert to system time.
    /// </summary>
    public HardwareTimestamp HardwareRaw { get; init; }

    /// <summary>
    /// Gets a value indicating whether any timestamp in this tuple is valid.
    /// </summary>
    public bool HasAny => Software.IsValid || HardwareRaw.IsValid;
}
