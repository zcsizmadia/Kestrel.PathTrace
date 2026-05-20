namespace Kestrel.PathTrace.Abstractions;

/// <summary>
/// Snapshot of multiple Linux clocks taken in rapid succession.
/// Used to compute the offset between the NIC PHC clock and
/// <see cref="System.Diagnostics.Stopwatch"/> (CLOCK_MONOTONIC).
/// </summary>
public readonly record struct ClockCalibration
{
    /// <summary>CLOCK_MONOTONIC nanoseconds (matches Stopwatch on Linux).</summary>
    public long MonotonicNs { get; init; }

    /// <summary>CLOCK_REALTIME nanoseconds (Unix epoch).</summary>
    public long RealtimeNs { get; init; }

    /// <summary>
    /// CLOCK_TAI nanoseconds. NIC hardware timestamps are often TAI-based
    /// when the NIC is PTP-synchronized (TAI = UTC + leap-seconds offset).
    /// </summary>
    public long TaiNs { get; init; }

    /// <summary>CLOCK_MONOTONIC_RAW nanoseconds (hardware, no NTP adjustment).</summary>
    public long RawMonotonicNs { get; init; }

    /// <summary>
    /// Converts a raw PHC hardware timestamp (nanoseconds) to an approximate
    /// CLOCK_MONOTONIC value by applying the TAI→Monotonic offset captured
    /// at calibration time.  Accurate only when the PHC is TAI-synchronized.
    /// </summary>
    public long PhcToMonotonic(long phcNs)
    {
        long taiOffset = MonotonicNs - TaiNs;
        return phcNs + taiOffset;
    }
}
