using Kestrel.PathTrace.Abstractions;

using Microsoft.Extensions.DependencyInjection;

namespace Kestrel.PathTrace.OpenTelemetry;

/// <summary>
/// Extension methods for registering the OpenTelemetry telemetry sink.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="OpenTelemetrySink"/> so that <c>TelemetryDispatcher</c>
    /// picks it up automatically.  Call after <c>AddKestrelPathTrace()</c>.
    /// </summary>
    public static IServiceCollection AddKestrelPathTraceOpenTelemetry(this IServiceCollection services)
    {
        services.AddKeyedSingleton<IRequestPathTelemetrySink, OpenTelemetrySink>(PathTraceDefaults.SinkKey);
        return services;
    }
}
