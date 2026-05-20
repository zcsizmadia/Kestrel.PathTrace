using System.Net.Sockets;

namespace Kestrel.PathTrace.Abstractions;

/// <summary>
/// Per-connection state shared between the transport wrapper, the NIC
/// timestamping path, and the per-request telemetry collection.
/// </summary>
public sealed class ConnectionTelemetryState
{
    /// <summary>OS socket handle for P/Invoke calls (tcpinfo_shim / hwtstamp_shim).</summary>
    public nint SocketHandle { get; set; }

    /// <summary>
    /// NIC timestamping capabilities resolved once per connection when
    /// hardware timestamping is requested.  <see langword="null"/> when
    /// hardware timestamping is not available or not enabled.
    /// </summary>
    public NicTimestampCapabilities? NicCapabilities { get; set; }

    /// <summary>
    /// Clock calibration snapshot taken at connection-accept time.
    /// Used by <see cref="RequestPathTelemetry.NicToTransportNs"/>.
    /// </summary>
    public ClockCalibration? ClockCalibration { get; set; }

    /// <summary>
    /// The interface name resolved for this connection (e.g. "eth0").
    /// Empty when unavailable.
    /// </summary>
    public string InterfaceName { get; set; } = string.Empty;

    /// <summary>
    /// The most recently sampled NIC RX timestamp for the current request.
    /// Cleared at the start of each request.
    /// </summary>
    public PacketTimestamps? LastRxTimestamp { get; set; }

    /// <summary>Stopwatch timestamp when the connection was accepted.</summary>
    public long T0_ConnectionAccepted { get; set; }

    /// <summary>Address family of the underlying socket.</summary>
    public AddressFamily AddressFamily { get; set; }
}
