using System.Diagnostics;

using Kestrel.PathTrace.Abstractions;
using Kestrel.PathTrace.OpenTelemetry;

using Microsoft.AspNetCore.Http;

using TUnit.Assertions;
using TUnit.Core;

namespace Kestrel.PathTrace.Tests.OpenTelemetry;

[NotInParallel]
public sealed class OpenTelemetrySinkTests
{
    /// <summary>
    /// Runs <paramref name="action"/> with a scoped <see cref="ActivityListener"/> that
    /// captures all activities emitted by the "Kestrel.PathTrace" source.
    /// Returns the captured list after the action completes.
    /// </summary>
    private static List<Activity> RunWithListener(Action<OpenTelemetrySink> action)
    {
        List<Activity> captured = [];

        using ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name == "Kestrel.PathTrace",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => captured.Add(activity),
        };
        ActivitySource.AddActivityListener(listener);

        action(new OpenTelemetrySink());
        return captured;
    }

    // -----------------------------------------------------------------------
    // No listener → no allocations, no throw
    // -----------------------------------------------------------------------

    [Test]
    public void Record_WithNoListener_DoesNotThrow()
    {
        // No listener registered → StartActivity returns null → should not throw.
        OpenTelemetrySink sink = new();
        sink.Record(new DefaultHttpContext(), new RequestPathTelemetry
        {
            Method = "GET", Route = "/noop", StatusCode = 200,
        });
    }

    // -----------------------------------------------------------------------
    // Root activity
    // -----------------------------------------------------------------------

    [Test]
    public async Task Record_CreatesRootActivity_WithCorrectName()
    {
        List<Activity> captured = RunWithListener(sink => sink.Record(new DefaultHttpContext(), new RequestPathTelemetry
        {
            Method = "GET", Route = "/ping", StatusCode = 200,
        }));

        Activity? root = captured.FirstOrDefault(a => a.OperationName.StartsWith("kestrel.request", StringComparison.Ordinal));
        await Assert.That(root).IsNotNull();
        await Assert.That(root!.OperationName).IsEqualTo("kestrel.request GET /ping");
    }

    [Test]
    public async Task Record_RootActivity_HasHttpMethodTag()
    {
        List<Activity> captured = RunWithListener(sink => sink.Record(new DefaultHttpContext(), new RequestPathTelemetry
        {
            Method = "POST", Route = "/items", StatusCode = 201,
        }));

        Activity? root = captured.FirstOrDefault(a => a.OperationName.StartsWith("kestrel.request", StringComparison.Ordinal));
        await Assert.That(root).IsNotNull();
        await Assert.That(root!.GetTagItem("http.method")).IsEqualTo("POST");
    }

    [Test]
    public async Task Record_RootActivity_HasHttpRouteTag()
    {
        List<Activity> captured = RunWithListener(sink => sink.Record(new DefaultHttpContext(), new RequestPathTelemetry
        {
            Method = "GET", Route = "/items/{id}", StatusCode = 200,
        }));

        Activity? root = captured.FirstOrDefault(a => a.OperationName.StartsWith("kestrel.request", StringComparison.Ordinal));
        await Assert.That(root!.GetTagItem("http.route")).IsEqualTo("/items/{id}");
    }

    [Test]
    public async Task Record_RootActivity_HasStatusCodeTag()
    {
        List<Activity> captured = RunWithListener(sink => sink.Record(new DefaultHttpContext(), new RequestPathTelemetry
        {
            Method = "DELETE", Route = "/items/{id}", StatusCode = 204,
        }));

        Activity? root = captured.FirstOrDefault(a => a.OperationName.StartsWith("kestrel.request", StringComparison.Ordinal));
        await Assert.That(root!.GetTagItem("http.status_code")).IsEqualTo(204);
    }

    [Test]
    public async Task Record_RootActivity_HasNicToTransportTag_WhenPresent()
    {
        List<Activity> captured = RunWithListener(sink => sink.Record(new DefaultHttpContext(), new RequestPathTelemetry
        {
            Method = "GET", Route = "/nic", StatusCode = 200,
            NicRxHardware = new HardwareTimestamp { Seconds = 0, Nanoseconds = 500_000, IsValid = true },
            ClockCalibration = new ClockCalibration(), // enables NicToTransportNs computation
        }));

        Activity? root = captured.FirstOrDefault(a => a.OperationName.StartsWith("kestrel.request", StringComparison.Ordinal));
        await Assert.That(root!.GetTagItem("kestrel.nic_to_transport_ns")).IsNotNull();
    }

    [Test]
    public async Task Record_RootActivity_HasNicHwRxTag_WhenHardwareTimestampValid()
    {
        List<Activity> captured = RunWithListener(sink => sink.Record(new DefaultHttpContext(), new RequestPathTelemetry
        {
            Method = "GET", Route = "/hw", StatusCode = 200,
            NicRxHardware = new HardwareTimestamp { Seconds = 1, Nanoseconds = 200_000, IsValid = true },
        }));

        Activity? root = captured.FirstOrDefault(a => a.OperationName.StartsWith("kestrel.request", StringComparison.Ordinal));
        await Assert.That(root!.GetTagItem("nic.hw_rx_ns")).IsNotNull();
    }

    [Test]
    public async Task Record_RootActivity_DoesNotHaveNicHwRxTag_WhenHardwareTimestampInvalid()
    {
        List<Activity> captured = RunWithListener(sink => sink.Record(new DefaultHttpContext(), new RequestPathTelemetry
        {
            Method = "GET", Route = "/no-hw", StatusCode = 200,
            NicRxHardware = HardwareTimestamp.Invalid,
        }));

        Activity? root = captured.FirstOrDefault(a => a.OperationName.StartsWith("kestrel.request", StringComparison.Ordinal));
        await Assert.That(root!.GetTagItem("nic.hw_rx_ns")).IsNull();
    }

    // -----------------------------------------------------------------------
    // Child spans are emitted for each pipeline stage
    // -----------------------------------------------------------------------

    [Test]
    public async Task Record_EmitsTransportToParseSpan_WhenTicksOrdered()
    {
        long t2 = Stopwatch.GetTimestamp();
        long t3 = t2 + 1_000;

        List<Activity> captured = RunWithListener(sink => sink.Record(new DefaultHttpContext(), new RequestPathTelemetry
        {
            Method = "GET", Route = "/spans", StatusCode = 200,
            T2_TransportRead = t2, T3_HttpParseStart = t3,
        }));

        await Assert.That(captured.Any(a => a.OperationName == "kestrel.transport_to_parse")).IsTrue();
    }

    [Test]
    public async Task Record_EmitsParseSpan_WhenTicksOrdered()
    {
        long t3 = Stopwatch.GetTimestamp();
        long t4 = t3 + 2_000;

        List<Activity> captured = RunWithListener(sink => sink.Record(new DefaultHttpContext(), new RequestPathTelemetry
        {
            Method = "GET", Route = "/spans", StatusCode = 200,
            T3_HttpParseStart = t3, T4_HttpHeadersComplete = t4,
        }));

        await Assert.That(captured.Any(a => a.OperationName == "kestrel.parse")).IsTrue();
    }

    [Test]
    public async Task Record_EmitsMiddlewareSpan_WhenTicksOrdered()
    {
        long t5 = Stopwatch.GetTimestamp();
        long t6 = t5 + 3_000;

        List<Activity> captured = RunWithListener(sink => sink.Record(new DefaultHttpContext(), new RequestPathTelemetry
        {
            Method = "GET", Route = "/spans", StatusCode = 200,
            T5_MiddlewareStart = t5, T6_EndpointStart = t6,
        }));

        await Assert.That(captured.Any(a => a.OperationName == "kestrel.middleware")).IsTrue();
    }

    [Test]
    public async Task Record_EmitsEndpointSpan_WhenTicksOrdered()
    {
        long t6 = Stopwatch.GetTimestamp();
        long t7 = t6 + 5_000;

        List<Activity> captured = RunWithListener(sink => sink.Record(new DefaultHttpContext(), new RequestPathTelemetry
        {
            Method = "GET", Route = "/spans", StatusCode = 200,
            T6_EndpointStart = t6, T7_EndpointEnd = t7,
        }));

        await Assert.That(captured.Any(a => a.OperationName == "kestrel.endpoint")).IsTrue();
    }

    [Test]
    public async Task Record_EmitsSerializationSpan_WhenTicksOrdered()
    {
        long t8 = Stopwatch.GetTimestamp();
        long t9 = t8 + 1_500;

        List<Activity> captured = RunWithListener(sink => sink.Record(new DefaultHttpContext(), new RequestPathTelemetry
        {
            Method = "GET", Route = "/spans", StatusCode = 200,
            T8_ResponseWriteStart = t8, T9_ResponseWriteEnd = t9,
        }));

        await Assert.That(captured.Any(a => a.OperationName == "kestrel.serialization")).IsTrue();
    }

    [Test]
    public async Task Record_EmitsWritebackSpan_WhenTicksOrdered()
    {
        long t9 = Stopwatch.GetTimestamp();
        long t10 = t9 + 800;

        List<Activity> captured = RunWithListener(sink => sink.Record(new DefaultHttpContext(), new RequestPathTelemetry
        {
            Method = "GET", Route = "/spans", StatusCode = 200,
            T9_ResponseWriteEnd = t9, T10_TransportWriteStart = t10,
        }));

        await Assert.That(captured.Any(a => a.OperationName == "kestrel.writeback")).IsTrue();
    }

    [Test]
    public async Task Record_DoesNotEmitSpan_WhenEndTickNotGreaterThanStart()
    {
        long tick = Stopwatch.GetTimestamp();

        List<Activity> captured = RunWithListener(sink => sink.Record(new DefaultHttpContext(), new RequestPathTelemetry
        {
            Method = "GET", Route = "/zero-dur", StatusCode = 200,
            T2_TransportRead  = tick,
            T3_HttpParseStart = tick, // equal -> no span
        }));

        await Assert.That(captured.Any(a => a.OperationName == "kestrel.transport_to_parse")).IsFalse();
    }

    // -----------------------------------------------------------------------
    // Child span tags
    // -----------------------------------------------------------------------

    [Test]
    public async Task Record_ChildSpan_HasHttpMethodTag()
    {
        long t6 = Stopwatch.GetTimestamp();
        long t7 = t6 + 5_000;

        List<Activity> captured = RunWithListener(sink => sink.Record(new DefaultHttpContext(), new RequestPathTelemetry
        {
            Method = "PATCH", Route = "/child-tags", StatusCode = 200,
            T6_EndpointStart = t6, T7_EndpointEnd = t7,
        }));

        Activity? span = captured.FirstOrDefault(a => a.OperationName == "kestrel.endpoint");
        await Assert.That(span).IsNotNull();
        await Assert.That(span!.GetTagItem("http.method")).IsEqualTo("PATCH");
    }
}
