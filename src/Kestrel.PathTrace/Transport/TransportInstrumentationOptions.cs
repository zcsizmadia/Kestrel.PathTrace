namespace Kestrel.PathTrace.Transport;

/// <summary>
/// Controls what the instrumented transport layer collects.
/// </summary>
public sealed class TransportInstrumentationOptions
{
    /// <summary>
    /// When <see langword="true"/> (default), the transport attempts to enable
    /// SO_TIMESTAMPING on every accepted socket, using hardware timestamps if
    /// the NIC supports them and falling back to software timestamps otherwise.
    /// </summary>
    public bool EnableHardwareTimestamping { get; set; } = true;

    /// <summary>
    /// When <see langword="true"/>, the transport also enables TX hardware
    /// timestamps (SOF_TIMESTAMPING_TX_HARDWARE) so the full
    /// NIC-RX → transport → … → NIC-TX loop can be measured.
    /// Requires additional poll() on the error queue after each send.
    /// Default is <see langword="false"/> to minimise overhead.
    /// </summary>
    public bool EnableTxHardwareTimestamping { get; set; }

    /// <summary>
    /// When <see langword="true"/> (default), queries TCP_INFO on Windows via
    /// <c>tcpinfo_shim.dll</c> and attaches it to the telemetry record.
    /// </summary>
    public bool EnableWindowsTcpInfo { get; set; } = true;
}
