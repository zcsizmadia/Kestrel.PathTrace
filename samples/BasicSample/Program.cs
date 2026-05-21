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

// Allow CI / automated smoke tests to shut the app down cleanly after N seconds.
// CLI:     dotnet run -- --run-for-seconds 10
// Env var: RUN_FOR_SECONDS=10 dotnet run
int runForSeconds = 0;
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--run-for-seconds" && int.TryParse(args[i + 1], out int parsed))
    {
        runForSeconds = parsed;
        break;
    }
}

if (runForSeconds == 0)
{
    int.TryParse(Environment.GetEnvironmentVariable("RUN_FOR_SECONDS"), out runForSeconds);
}

if (runForSeconds > 0)
{
    IHostApplicationLifetime lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
    _ = Task.Run(async () =>
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(runForSeconds), lifetime.ApplicationStopping);
            lifetime.StopApplication();
        }
        catch (OperationCanceledException)
        {
            // App is already stopping (e.g. Ctrl+C) — nothing to do.
        }
    });
}

await app.RunAsync();
