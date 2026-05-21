using Kestrel.PathTrace.Abstractions;

using Microsoft.Extensions.DependencyInjection;

using Prometheus;

namespace Kestrel.PathTrace.Prometheus;

/// <summary>
/// Extension methods for registering the Prometheus telemetry sink.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="PrometheusSink"/> so that <c>TelemetryDispatcher</c>
    /// picks it up automatically.  Call after <c>AddKestrelPathTrace()</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="metricFactory">
    /// Optional custom <see cref="IMetricFactory"/>.
    /// Defaults to <see cref="Metrics.DefaultFactory"/> when <see langword="null"/>.
    /// </param>
    public static IServiceCollection AddKestrelPathTracePrometheus(
        this IServiceCollection services,
        IMetricFactory? metricFactory = null)
    {
        services.AddKeyedSingleton<IRequestPathTelemetrySink>(
            PathTraceDefaults.SinkKey,
            (_, _) => metricFactory is null
                ? new PrometheusSink()
                : new PrometheusSink(metricFactory));

        return services;
    }
}
