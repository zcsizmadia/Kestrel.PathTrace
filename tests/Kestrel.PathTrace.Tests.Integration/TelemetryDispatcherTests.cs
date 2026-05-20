using Kestrel.PathTrace.Abstractions;

using Microsoft.AspNetCore.Http;

using TUnit.Assertions;
using TUnit.Core;

namespace Kestrel.PathTrace.Tests.Integration;

public sealed class TelemetryDispatcherTests
{
    private sealed class CaptureSink : IRequestPathTelemetrySink
    {
        public List<RequestPathTelemetry> Captured { get; } = [];

        public void Record(HttpContext context, RequestPathTelemetry telemetry)
        {
            Captured.Add(telemetry);
        }
    }

    [Test]
    public async Task Dispatcher_FansOutToAllSinks()
    {
        CaptureSink sink1 = new();
        CaptureSink sink2 = new();
        TelemetryDispatcher dispatcher = new(sink1, sink2);

        HttpContext ctx = new DefaultHttpContext();
        RequestPathTelemetry telemetry = new() { Method = "GET", Route = "/test", StatusCode = 200 };

        dispatcher.Record(ctx, telemetry);

        await Assert.That(sink1.Captured).Count().IsEqualTo(1);
        await Assert.That(sink2.Captured).Count().IsEqualTo(1);
        await Assert.That(sink1.Captured[0].Method).IsEqualTo("GET");
        await Assert.That(sink2.Captured[0].Route).IsEqualTo("/test");
    }

    [Test]
    public async Task Dispatcher_NoSinks_DoesNotThrow()
    {
        TelemetryDispatcher dispatcher = new();
        HttpContext ctx = new DefaultHttpContext();
        RequestPathTelemetry telemetry = new();

        // Should not throw
        dispatcher.Record(ctx, telemetry);
    }

    [Test]
    public async Task Dispatcher_RecordsToAllSinks_MultipleRequests()
    {
        CaptureSink sink = new();
        TelemetryDispatcher dispatcher = new(sink);
        HttpContext ctx = new DefaultHttpContext();

        for (int i = 0; i < 10; i++)
        {
            dispatcher.Record(ctx, new RequestPathTelemetry { StatusCode = 200 + i });
        }

        await Assert.That(sink.Captured).Count().IsEqualTo(10);
    }
}
