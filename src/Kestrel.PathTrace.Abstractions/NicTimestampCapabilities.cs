namespace Kestrel.PathTrace.Abstractions;

/// <summary>
/// NIC hardware-timestamping capabilities reported by the driver.
/// </summary>
public sealed class NicTimestampCapabilities
{
    /// <summary>Gets the interface name (e.g. "eth0").</summary>
    public string InterfaceName { get; init; } = string.Empty;

    /// <summary>
    /// Raw SO_TIMESTAMPING bitmask reported by the driver via ETHTOOL_GET_TS_INFO.
    /// </summary>
    public uint SoTimestampingFlags { get; init; }

    /// <summary>
    /// PTP Hardware Clock index.  -1 means no PHC is associated with this NIC.
    /// The PHC device is accessible at /dev/ptp&lt;PhcIndex&gt;.
    /// </summary>
    public int PhcIndex { get; init; } = -1;

    /// <summary>Bitmask of supported TX timestamp types (hwtstamp_tx_types).</summary>
    public uint TxTypes { get; init; }

    /// <summary>Bitmask of supported RX filter modes (hwtstamp_rx_filters).</summary>
    public uint RxFilters { get; init; }

    /// <summary>Gets a value indicating whether the NIC supports hardware RX timestamping.</summary>
    public bool HardwareRxAvailable { get; init; }

    /// <summary>Gets a value indicating whether the NIC supports hardware TX timestamping.</summary>
    public bool HardwareTxAvailable { get; init; }

    /// <summary>Gets a value indicating whether software RX timestamping is supported.</summary>
    public bool SoftwareRxAvailable { get; init; }

    /// <summary>Gets a value indicating whether software TX timestamping is supported.</summary>
    public bool SoftwareTxAvailable { get; init; }

    /// <summary>
    /// Gets a value indicating whether raw hardware timestamps (PHC clock, not
    /// converted to system time) are available.
    /// </summary>
    public bool RawHardwareAvailable { get; init; }

    /// <summary>
    /// Gets a value indicating whether a PTP hardware clock is present and hardware
    /// timestamping is fully available (RX + RAW).
    /// </summary>
    public bool IsFullHardwareTimestampingAvailable =>
        HardwareRxAvailable && RawHardwareAvailable && PhcIndex >= 0;
}
