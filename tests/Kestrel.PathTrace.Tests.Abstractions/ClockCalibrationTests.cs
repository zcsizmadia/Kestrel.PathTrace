using Kestrel.PathTrace.Abstractions;

using TUnit.Assertions;
using TUnit.Core;

namespace Kestrel.PathTrace.Tests.Abstractions;

public sealed class ClockCalibrationTests
{
    [Test]
    public async Task PhcToMonotonic_AppliesTaiOffset()
    {
        // TAI is +37 seconds ahead of REALTIME; MONOTONIC drifts from REALTIME.
        // Simulate: monotonic=1000ns, TAI=963ns → offset = +37ns
        ClockCalibration cal = new()
        {
            MonotonicNs     = 1_000_000_000L,
            TaiNs           = 963_000_000L,
            RealtimeNs      = 0L,
            RawMonotonicNs  = 0L,
        };

        // PHC timestamp of 963ns (TAI-based) → expected monotonic = 1000ns
        long converted = cal.PhcToMonotonic(963_000_000L);

        await Assert.That(converted).IsEqualTo(1_000_000_000L);
    }

    [Test]
    public async Task PhcToMonotonic_ZeroOffset_ReturnsSameValue()
    {
        ClockCalibration cal = new()
        {
            MonotonicNs = 500L,
            TaiNs       = 500L,
        };

        await Assert.That(cal.PhcToMonotonic(1234L)).IsEqualTo(1234L);
    }
}
