using Kestrel.PathTrace;
using Kestrel.PathTrace.OpenTelemetry;
using Kestrel.PathTrace.Prometheus;

using OpenTelemetry.Trace;

using Prometheus;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Register Kestrel.PathTrace with both Prometheus and OpenTelemetry sinks.
// On Linux, hardware timestamping is enabled automatically when the NIC supports it.
builder.Services.AddKestrelPathTrace(opts =>
{
    opts.Transport = new()
    {
        EnableHardwareTimestamping    = true,
        EnableTxHardwareTimestamping  = false,
        EnableWindowsTcpInfo          = true,
    };
});
builder.Services.AddKestrelPathTracePrometheus();
builder.Services.AddKestrelPathTraceOpenTelemetry();

// Wire up OpenTelemetry tracing
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddSource("Kestrel.PathTrace");
        tracing.AddConsoleExporter();
    });

WebApplication app = builder.Build();

// Place the middleware first — before UseRouting.
app.UseKestrelPathTrace();

// Expose Prometheus metrics endpoint
app.UseHttpMetrics();
app.MapMetrics("/metrics");

// Sample endpoints
app.MapGet("/ping", () => "pong");

app.MapGet("/slow", async () =>
{
    await Task.Delay(TimeSpan.FromMilliseconds(50));
    return "done";
});

app.MapGet("/info", (HttpContext ctx) =>
{
    string platform = OperatingSystem.IsLinux()
        ? "Linux (hardware timestamping available)"
        : OperatingSystem.IsWindows()
            ? "Windows (TCP_INFO available)"
            : "Other";

    return new
    {
        platform,
        utcNow = DateTimeOffset.UtcNow,
    };
});

await app.RunAsync();
