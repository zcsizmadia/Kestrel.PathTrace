using Kestrel.PathTrace.Abstractions;
using Kestrel.PathTrace.Prometheus;

using Microsoft.AspNetCore.Http;

using Prometheus;

using TUnit.Assertions;
using TUnit.Core;

namespace Kestrel.PathTrace.Tests.Prometheus;

public sealed class PrometheusSinkTests
{
    private static PrometheusSink CreateSink(out CollectorRegistry registry)
    {
        registry = Metrics.NewCustomRegistry();
        return new PrometheusSink(Metrics.WithCustomRegistry(registry));
    }

    private static async Task<string> ScrapeAsync(CollectorRegistry registry)
    {
        using MemoryStream ms = new();
        await registry.CollectAndExportAsTextAsync(ms);
        ms.Position = 0;
        using StreamReader sr = new(ms);
        return await sr.ReadToEndAsync();
    }

    // -----------------------------------------------------------------------
    // Construction
    // -----------------------------------------------------------------------

    [Test]
    public void DefaultConstructor_DoesNotThrow()
    {
        // Uses Metrics.DefaultFactory — just verify it constructs without exception.
        _ = new PrometheusSink();
    }

    [Test]
    public void FactoryConstructor_DoesNotThrow()
    {
        CollectorRegistry registry = Metrics.NewCustomRegistry();
        _ = new PrometheusSink(Metrics.WithCustomRegistry(registry));
    }

    // -----------------------------------------------------------------------
    // Record — all six pipeline latency histograms are observed
    // -----------------------------------------------------------------------

    [Test]
    public async Task Record_ObservesTransportLatency()
    {
        PrometheusSink sink = CreateSink(out CollectorRegistry registry);

        sink.Record(new DefaultHttpContext(), new RequestPathTelemetry
        {
            Method = "GET", Route = "/test", StatusCode = 200,
            T2_TransportRead = 100, T3_HttpParseStart = 200,
        });

        string scrape = await ScrapeAsync(registry);
        await Assert.That(scrape).Contains("kestrel_transport_latency_us");
    }

    [Test]
    public async Task Record_ObservesHttpParseLatency()
    {
        PrometheusSink sink = CreateSink(out CollectorRegistry registry);

        sink.Record(new DefaultHttpContext(), new RequestPathTelemetry
        {
            Method = "POST", Route = "/api", StatusCode = 201,
            T3_HttpParseStart = 100, T4_HttpHeadersComplete = 300,
        });

        string scrape = await ScrapeAsync(registry);
        await Assert.That(scrape).Contains("kestrel_http_parse_latency_us");
    }

    [Test]
    public async Task Record_ObservesMiddlewareLatency()
    {
        PrometheusSink sink = CreateSink(out CollectorRegistry registry);
        sink.Record(new DefaultHttpContext(), new RequestPathTelemetry
        {
            Method = "GET", Route = "/mw", StatusCode = 200,
            T5_MiddlewareStart = 100, T6_EndpointStart = 400,
        });

        string scrape = await ScrapeAsync(registry);
        await Assert.That(scrape).Contains("kestrel_middleware_latency_us");
    }

    [Test]
    public async Task Record_ObservesEndpointLatency()
    {
        PrometheusSink sink = CreateSink(out CollectorRegistry registry);
        sink.Record(new DefaultHttpContext(), new RequestPathTelemetry
        {
            Method = "GET", Route = "/ep", StatusCode = 200,
            T6_EndpointStart = 100, T7_EndpointEnd = 500,
        });

        string scrape = await ScrapeAsync(registry);
        await Assert.That(scrape).Contains("kestrel_endpoint_latency_us");
    }

    [Test]
    public async Task Record_ObservesSerializationLatency()
    {
        PrometheusSink sink = CreateSink(out CollectorRegistry registry);
        sink.Record(new DefaultHttpContext(), new RequestPathTelemetry
        {
            Method = "GET", Route = "/ser", StatusCode = 200,
            T8_ResponseWriteStart = 100, T9_ResponseWriteEnd = 600,
        });

        string scrape = await ScrapeAsync(registry);
        await Assert.That(scrape).Contains("kestrel_serialization_latency_us");
    }

    [Test]
    public async Task Record_ObservesWritebackLatency()
    {
        PrometheusSink sink = CreateSink(out CollectorRegistry registry);
        sink.Record(new DefaultHttpContext(), new RequestPathTelemetry
        {
            Method = "GET", Route = "/wb", StatusCode = 200,
            T9_ResponseWriteEnd = 100, T10_TransportWriteStart = 700,
        });

        string scrape = await ScrapeAsync(registry);
        await Assert.That(scrape).Contains("kestrel_writeback_latency_us");
    }

    // -----------------------------------------------------------------------
    // NIC → transport histogram — only when NicToTransportNs is set
    // -----------------------------------------------------------------------

    [Test]
    public async Task Record_ObservesNicToTransportLatency_WhenPresent()
    {
        PrometheusSink sink = CreateSink(out CollectorRegistry registry);
        sink.Record(new DefaultHttpContext(), new RequestPathTelemetry
        {
            Method = "GET", Route = "/nic", StatusCode = 200,
            NicRxHardware = new HardwareTimestamp { Seconds = 0, Nanoseconds = 100_000, IsValid = true },
        });

        string scrape = await ScrapeAsync(registry);
        await Assert.That(scrape).Contains("kestrel_nic_to_transport_ns");
    }

    [Test]
    public async Task Record_DoesNotObserveNicToTransport_WhenNull()
    {
        PrometheusSink sink = CreateSink(out CollectorRegistry registry);

        // NicToTransportNs will be null when NicRxHardware is invalid
        sink.Record(new DefaultHttpContext(), new RequestPathTelemetry
        {
            Method = "GET", Route = "/no-nic", StatusCode = 200,
        });

        string scrape = await ScrapeAsync(registry);

        // When no hardware timestamp is present the NIC histogram must not be exported at all.
        await Assert.That(scrape).DoesNotContain("kestrel_nic_to_transport_ns_bucket");
    }

    // -----------------------------------------------------------------------
    // Labels are correctly applied
    // -----------------------------------------------------------------------

    [Test]
    public async Task Record_UsesCorrectLabels()
    {
        PrometheusSink sink = CreateSink(out CollectorRegistry registry);
        sink.Record(new DefaultHttpContext(), new RequestPathTelemetry
        {
            Method = "DELETE", Route = "/items/{id}", StatusCode = 204,
        });

        string scrape = await ScrapeAsync(registry);

        await Assert.That(scrape).Contains("method=\"DELETE\"");
        await Assert.That(scrape).Contains("route=\"/items/{id}\"");
        await Assert.That(scrape).Contains("status=\"204\"");
    }

    // -----------------------------------------------------------------------
    // Multiple calls accumulate observations
    // -----------------------------------------------------------------------

    [Test]
    public async Task Record_MultipleCallsAccumulateCount()
    {
        PrometheusSink sink = CreateSink(out CollectorRegistry registry);
        HttpContext ctx = new DefaultHttpContext();
        RequestPathTelemetry t = new() { Method = "GET", Route = "/count", StatusCode = 200 };

        for (int i = 0; i < 5; i++)
        {
            sink.Record(ctx, t);
        }

        string scrape = await ScrapeAsync(registry);

        // The _count line for this label set should report 5 observations.
        await Assert.That(scrape).Contains("kestrel_transport_latency_us_count{route=\"/count\",method=\"GET\",status=\"200\"} 5");
    }
}
