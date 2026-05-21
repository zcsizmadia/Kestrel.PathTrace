using Kestrel.PathTrace.Abstractions;

using Microsoft.AspNetCore.Http;

using Prometheus;

namespace Kestrel.PathTrace.Prometheus;

/// <summary>
/// Records <see cref="RequestPathTelemetry"/> into Prometheus histograms.
/// Exposed at <c>/metrics</c> by the default prometheus-net middleware.
/// </summary>
/// <remarks>
/// Inject <see cref="IMetricFactory"/> via DI for production use, or supply
/// <c>Metrics.WithCustomRegistry(Metrics.NewCustomRegistry())</c> in tests
/// for fully isolated, per-test metric state.
/// </remarks>
public sealed class PrometheusSink : IRequestPathTelemetrySink
{
    // -----------------------------------------------------------------------
    // Bucket definitions
    // -----------------------------------------------------------------------

    private static readonly double[] LatencyBuckets =
    [
        1, 5, 10, 25, 50, 100, 250, 500, 1_000, 5_000, 10_000, 50_000, 100_000,
    ];

    private static readonly double[] NanosecondBuckets =
    [
        100, 500, 1_000, 5_000, 10_000, 50_000, 100_000, 500_000, 1_000_000,
    ];

    private static readonly string[] RouteMethodLabels = ["route", "method", "status"];

    // -----------------------------------------------------------------------
    // Instance histograms (scoped to the injected registry)
    // -----------------------------------------------------------------------

    private readonly Histogram _transportLatency;
    private readonly Histogram _httpParseLatency;
    private readonly Histogram _middlewareLatency;
    private readonly Histogram _endpointLatency;
    private readonly Histogram _serializationLatency;
    private readonly Histogram _writebackLatency;
    private readonly Histogram _nicToTransportLatency;

    /// <summary>
    /// Initialises the sink using <see cref="Metrics.DefaultFactory"/>.
    /// </summary>
    public PrometheusSink() : this(Metrics.DefaultFactory) { }

    /// <summary>
    /// Initialises the sink using the supplied <paramref name="metricFactory"/>.
    /// </summary>
    public PrometheusSink(IMetricFactory metricFactory)
    {
        _transportLatency = metricFactory.CreateHistogram(
            "kestrel_transport_latency_us",
            "Transport → HTTP parse handoff latency (µs).",
            new HistogramConfiguration { LabelNames = RouteMethodLabels, Buckets = LatencyBuckets });

        _httpParseLatency = metricFactory.CreateHistogram(
            "kestrel_http_parse_latency_us",
            "HTTP parsing latency (µs).",
            new HistogramConfiguration { LabelNames = RouteMethodLabels, Buckets = LatencyBuckets });

        _middlewareLatency = metricFactory.CreateHistogram(
            "kestrel_middleware_latency_us",
            "Middleware dispatch latency (µs).",
            new HistogramConfiguration { LabelNames = RouteMethodLabels, Buckets = LatencyBuckets });

        _endpointLatency = metricFactory.CreateHistogram(
            "kestrel_endpoint_latency_us",
            "Endpoint execution latency (µs).",
            new HistogramConfiguration { LabelNames = RouteMethodLabels, Buckets = LatencyBuckets });

        _serializationLatency = metricFactory.CreateHistogram(
            "kestrel_serialization_latency_us",
            "Response serialization latency (µs).",
            new HistogramConfiguration { LabelNames = RouteMethodLabels, Buckets = LatencyBuckets });

        _writebackLatency = metricFactory.CreateHistogram(
            "kestrel_writeback_latency_us",
            "Transport writeback latency (µs).",
            new HistogramConfiguration { LabelNames = RouteMethodLabels, Buckets = LatencyBuckets });

        _nicToTransportLatency = metricFactory.CreateHistogram(
            "kestrel_nic_to_transport_ns",
            "NIC hardware RX → transport read latency (ns). Only present when hardware timestamping is active.",
            new HistogramConfiguration { LabelNames = RouteMethodLabels, Buckets = NanosecondBuckets });
    }

    // -----------------------------------------------------------------------
    // IRequestPathTelemetrySink
    // -----------------------------------------------------------------------

    /// <inheritdoc />
    public void Record(HttpContext context, RequestPathTelemetry t)
    {
        string route  = t.Route;
        string method = t.Method;
        string status = t.StatusCode.ToString();

        _transportLatency    .WithLabels(route, method, status).Observe(t.TransportLatencyUs);
        _httpParseLatency    .WithLabels(route, method, status).Observe(t.HttpParseLatencyUs);
        _middlewareLatency   .WithLabels(route, method, status).Observe(t.MiddlewareLatencyUs);
        _endpointLatency     .WithLabels(route, method, status).Observe(t.EndpointLatencyUs);
        _serializationLatency.WithLabels(route, method, status).Observe(t.SerializationLatencyUs);
        _writebackLatency    .WithLabels(route, method, status).Observe(t.WritebackLatencyUs);

        if (t.NicToTransportNs is { } nicNs)
        {
            _nicToTransportLatency.WithLabels(route, method, status).Observe(nicNs);
        }
    }
}
