using System.Diagnostics;

namespace Kestrel.PathTrace.Abstractions;

/// <summary>
/// All timestamps and derived metrics for a single HTTP request as it travels
/// from the NIC through Kestrel to the application and back.
/// All <c>T*</c> fields hold <see cref="Stopwatch.GetTimestamp"/> values.
/// </summary>
public sealed class RequestPathTelemetry
{
    // -----------------------------------------------------------------------
    // NIC / kernel ingress (Linux hardware-timestamping path only)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Raw NIC PHC timestamp for the first received segment (nanoseconds,
    /// PHC epoch).  Valid only when hardware timestamping is active.
    /// </summary>
    public HardwareTimestamp NicRxHardware { get; set; }

    /// <summary>
    /// Software (kernel) RX timestamp for the first received segment
    /// (CLOCK_REALTIME nanoseconds).
    /// </summary>
    public HardwareTimestamp NicRxSoftware { get; set; }

    /// <summary>
    /// Clock calibration snapshot taken at connection-accept time.
    /// Used to convert <see cref="NicRxHardware"/> into Stopwatch-relative time.
    /// </summary>
    public ClockCalibration? ClockCalibration { get; set; }

    // -----------------------------------------------------------------------
    // Transport layer (Stopwatch ticks)
    // -----------------------------------------------------------------------

    /// <summary>Stopwatch timestamp: first byte arrived from the OS transport.</summary>
    public long T2_TransportRead { get; set; }

    // -----------------------------------------------------------------------
    // HTTP parse layer
    // -----------------------------------------------------------------------

    /// <summary>Stopwatch timestamp: HTTP parser started.</summary>
    public long T3_HttpParseStart { get; set; }

    /// <summary>Stopwatch timestamp: all HTTP headers were parsed.</summary>
    public long T4_HttpHeadersComplete { get; set; }

    // -----------------------------------------------------------------------
    // Middleware / endpoint
    // -----------------------------------------------------------------------

    /// <summary>Stopwatch timestamp: first middleware invoked.</summary>
    public long T5_MiddlewareStart { get; set; }

    /// <summary>Stopwatch timestamp: endpoint handler invoked.</summary>
    public long T6_EndpointStart { get; set; }

    /// <summary>Stopwatch timestamp: endpoint handler returned.</summary>
    public long T7_EndpointEnd { get; set; }

    // -----------------------------------------------------------------------
    // Response write
    // -----------------------------------------------------------------------

    /// <summary>Stopwatch timestamp: first response byte written to the pipe.</summary>
    public long T8_ResponseWriteStart { get; set; }

    /// <summary>Stopwatch timestamp: last response byte written to the pipe.</summary>
    public long T9_ResponseWriteEnd { get; set; }

    /// <summary>Stopwatch timestamp: first response byte flushed to the OS transport.</summary>
    public long T10_TransportWriteStart { get; set; }

    // -----------------------------------------------------------------------
    // HTTP metadata
    // -----------------------------------------------------------------------

    /// <summary>HTTP method (GET, POST, …).</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>Matched route template (e.g. "/api/orders/{id}").</summary>
    public string Route { get; set; } = string.Empty;

    /// <summary>HTTP response status code.</summary>
    public int StatusCode { get; set; }

    // -----------------------------------------------------------------------
    // Derived latencies (computed on first access, in microseconds)
    // -----------------------------------------------------------------------

    private static double TicksToMicros(long ticks) =>
        (double)ticks / Stopwatch.Frequency * 1_000_000.0;

    /// <summary>Transport → HTTP parser handoff latency (µs).</summary>
    public double TransportLatencyUs => TicksToMicros(T3_HttpParseStart - T2_TransportRead);

    /// <summary>HTTP parsing latency (µs).</summary>
    public double HttpParseLatencyUs => TicksToMicros(T4_HttpHeadersComplete - T3_HttpParseStart);

    /// <summary>Middleware dispatch latency (µs).</summary>
    public double MiddlewareLatencyUs => TicksToMicros(T6_EndpointStart - T5_MiddlewareStart);

    /// <summary>Endpoint execution latency (µs).</summary>
    public double EndpointLatencyUs => TicksToMicros(T7_EndpointEnd - T6_EndpointStart);

    /// <summary>Response serialization latency (µs).</summary>
    public double SerializationLatencyUs => TicksToMicros(T9_ResponseWriteEnd - T8_ResponseWriteStart);

    /// <summary>Transport writeback latency (µs).</summary>
    public double WritebackLatencyUs => TicksToMicros(T10_TransportWriteStart - T9_ResponseWriteEnd);

    /// <summary>
    /// NIC hardware → transport latency (ns).  Valid only when both
    /// <see cref="NicRxHardware"/> is valid and <see cref="ClockCalibration"/>
    /// is set.
    /// </summary>
    public double? NicToTransportNs
    {
        get
        {
            if (!NicRxHardware.IsValid || ClockCalibration is not { } cal)
            {
                return null;
            }

            long nicMono = cal.PhcToMonotonic(NicRxHardware.TotalNanoseconds);
            long t2Ns    = (long)((double)T2_TransportRead / Stopwatch.Frequency * 1_000_000_000.0);
            long monoNs  = cal.MonotonicNs + t2Ns;

            return monoNs - nicMono;
        }
    }
}
