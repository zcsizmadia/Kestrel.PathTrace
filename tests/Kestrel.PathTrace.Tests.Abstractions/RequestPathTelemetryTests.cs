using System.Diagnostics;

using Kestrel.PathTrace.Abstractions;

using TUnit.Assertions;
using TUnit.Core;

namespace Kestrel.PathTrace.Tests.Abstractions;

public sealed class RequestPathTelemetryTests
{
    [Test]
    public async Task DerivedLatencies_AreCorrectlyComputed()
    {
        // Build a telemetry record with known tick deltas.
        // Use 1 tick = 1 µs by setting timestamps 1 µs apart via Stopwatch.Frequency.
        long oneMicrosInTicks = Stopwatch.Frequency / 1_000_000;
        if (oneMicrosInTicks == 0)
        {
            oneMicrosInTicks = 1;
        }

        RequestPathTelemetry t = new()
        {
            T2_TransportRead       = 0,
            T3_HttpParseStart      = oneMicrosInTicks * 10,
            T4_HttpHeadersComplete = oneMicrosInTicks * 20,
            T5_MiddlewareStart     = oneMicrosInTicks * 20,
            T6_EndpointStart       = oneMicrosInTicks * 30,
            T7_EndpointEnd         = oneMicrosInTicks * 80,
            T8_ResponseWriteStart  = oneMicrosInTicks * 80,
            T9_ResponseWriteEnd    = oneMicrosInTicks * 85,
            T10_TransportWriteStart = oneMicrosInTicks * 90,
        };

        // Each delta is within floating-point rounding; allow 1 µs tolerance.
        await Assert.That(t.TransportLatencyUs).IsGreaterThanOrEqualTo(9.0).And
                                               .IsLessThanOrEqualTo(11.0);
        await Assert.That(t.HttpParseLatencyUs).IsGreaterThanOrEqualTo(9.0).And
                                               .IsLessThanOrEqualTo(11.0);
        await Assert.That(t.EndpointLatencyUs).IsGreaterThanOrEqualTo(49.0).And
                                              .IsLessThanOrEqualTo(51.0);
        await Assert.That(t.SerializationLatencyUs).IsGreaterThanOrEqualTo(4.0).And
                                                   .IsLessThanOrEqualTo(6.0);
        await Assert.That(t.WritebackLatencyUs).IsGreaterThanOrEqualTo(4.0).And
                                               .IsLessThanOrEqualTo(6.0);
    }

    [Test]
    public async Task NicToTransportNs_IsNull_WhenNoHwTimestamp()
    {
        RequestPathTelemetry t = new()
        {
            NicRxHardware = HardwareTimestamp.Invalid,
        };

        await Assert.That(t.NicToTransportNs).IsNull();
    }

    [Test]
    public async Task NicToTransportNs_IsNull_WhenNoCalibration()
    {
        RequestPathTelemetry t = new()
        {
            NicRxHardware    = new HardwareTimestamp { IsValid = true, Seconds = 1, Nanoseconds = 0 },
            ClockCalibration = null,
        };

        await Assert.That(t.NicToTransportNs).IsNull();
    }
}
