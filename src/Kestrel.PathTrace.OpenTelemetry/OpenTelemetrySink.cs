using System.Diagnostics;

using Kestrel.PathTrace.Abstractions;

using Microsoft.AspNetCore.Http;

using OpenTelemetry.Trace;

namespace Kestrel.PathTrace.OpenTelemetry;

/// <summary>
/// Emits per-request OpenTelemetry spans that model each stage of the Kestrel
/// request path from NIC ingress to transport writeback.
/// </summary>
public sealed class OpenTelemetrySink : IRequestPathTelemetrySink
{
    private static readonly ActivitySource ActivitySource =
        new("Kestrel.PathTrace", "1.0.0");

    /// <inheritdoc />
    public void Record(HttpContext context, RequestPathTelemetry t)
    {
        // The root span (started by ASP.NET Core tracing) is already active on
        // the current Activity.  We emit child spans for each pipeline stage.

        using Activity? root = ActivitySource.StartActivity(
            $"kestrel.request {t.Method} {t.Route}",
            ActivityKind.Server);

        if (root is null)
        {
            // No listener attached; skip to avoid allocations.
            return;
        }

        root.SetTag("http.method",  t.Method);
        root.SetTag("http.route",   t.Route);
        root.SetTag("http.status_code", t.StatusCode);

        if (t.NicToTransportNs is { } nicNs)
        {
            root.SetTag("kestrel.nic_to_transport_ns", (long)nicNs);
        }

        if (t.NicRxHardware.IsValid)
        {
            root.SetTag("nic.hw_rx_ns", t.NicRxHardware.TotalNanoseconds);
        }

        double freq = Stopwatch.Frequency;

        // Each child span covers one pipeline stage.
        EmitSpan("kestrel.transport_to_parse",
            t.T2_TransportRead, t.T3_HttpParseStart, freq, root, t);

        EmitSpan("kestrel.parse",
            t.T3_HttpParseStart, t.T4_HttpHeadersComplete, freq, root, t);

        EmitSpan("kestrel.middleware",
            t.T5_MiddlewareStart, t.T6_EndpointStart, freq, root, t);

        EmitSpan("kestrel.endpoint",
            t.T6_EndpointStart, t.T7_EndpointEnd, freq, root, t);

        EmitSpan("kestrel.serialization",
            t.T8_ResponseWriteStart, t.T9_ResponseWriteEnd, freq, root, t);

        EmitSpan("kestrel.writeback",
            t.T9_ResponseWriteEnd, t.T10_TransportWriteStart, freq, root, t);
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private static void EmitSpan(
        string spanName,
        long startTick,
        long endTick,
        double frequency,
        Activity parent,
        RequestPathTelemetry t)
    {
        if (endTick <= startTick)
        {
            return;
        }

        // Convert Stopwatch ticks to DateTime offset from the root activity start.
        double startOffsetSec = (double)(startTick - 0) / frequency;
        double durationSec    = (double)(endTick - startTick) / frequency;

        ActivityContext parentCtx = parent.Context;

        using Activity? span = ActivitySource.StartActivity(
            spanName,
            ActivityKind.Internal,
            parentCtx,
            startTime: DateTimeOffset.UtcNow.AddSeconds(-((double)(Stopwatch.GetTimestamp() - startTick) / frequency)));

        if (span is null)
        {
            return;
        }

        span.SetTag("http.method",      t.Method);
        span.SetTag("http.route",       t.Route);
        span.SetTag("http.status_code", t.StatusCode);
        span.SetTag("kestrel.endpoint_latency_us",  t.EndpointLatencyUs);
        span.SetTag("kestrel.writeback_latency_us", t.WritebackLatencyUs);

        span.SetEndTime(span.StartTimeUtc.AddSeconds(durationSec));
    }
}
