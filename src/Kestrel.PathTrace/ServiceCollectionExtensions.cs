using System.Runtime.InteropServices;

using Kestrel.PathTrace.Abstractions;
using Kestrel.PathTrace.Middleware;
using Kestrel.PathTrace.Transport;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kestrel.PathTrace;

/// <summary>
/// Extension methods for registering Kestrel.PathTrace services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds all Kestrel.PathTrace services: transport instrumentation, middleware,
    /// Prometheus sink, and OpenTelemetry sink.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional callback to customise options.</param>
    public static IServiceCollection AddKestrelPathTrace(
        this IServiceCollection services,
        Action<PathTraceOptions>? configure = null)
    {
        PathTraceOptions options = new();
        configure?.Invoke(options);
        services.AddSingleton(options);

        // Hardware timestamp provider — platform-specific
        RegisterHardwareTimestampProvider(services);

        // Transport factory wrapper
        services.AddSingleton<TransportInstrumentationOptions>(
            _ => options.Transport ?? new TransportInstrumentationOptions());

        services.AddSingleton<IConnectionListenerFactory>(sp =>
        {
            SocketTransportFactory inner = ActivatorUtilities.CreateInstance<SocketTransportFactory>(sp);
            IHardwareTimestampProvider? hwts = sp.GetService<IHardwareTimestampProvider>();
            TransportInstrumentationOptions tOpts = sp.GetRequiredService<TransportInstrumentationOptions>();
            return new InstrumentedTransportFactory(inner, hwts, tOpts);
        });

        // Dispatcher collects all sinks registered under the well-known keyed-service key.
        services.AddSingleton<IRequestPathTelemetrySink>(sp =>
            new TelemetryDispatcher(
                [.. sp.GetKeyedServices<IRequestPathTelemetrySink>(PathTraceDefaults.SinkKey)]));

        return services;
    }

    /// <summary>
    /// Registers an individual <see cref="IRequestPathTelemetrySink"/> so that
    /// <see cref="TelemetryDispatcher"/> picks it up automatically.
    /// Call this — or the export-package helpers — before the host is built.
    /// </summary>
    public static IServiceCollection AddKestrelPathTraceSink<T>(this IServiceCollection services)
        where T : class, IRequestPathTelemetrySink
    {
        services.AddKeyedSingleton<IRequestPathTelemetrySink, T>(PathTraceDefaults.SinkKey);
        return services;
    }

    /// <summary>
    /// Registers the <see cref="RequestPathTelemetryMiddleware"/> in the pipeline.
    /// Must be called early, before <c>UseRouting()</c>.
    /// </summary>
    public static IApplicationBuilder UseKestrelPathTrace(this IApplicationBuilder app)
    {
        app.UseMiddleware<RequestPathTelemetryMiddleware>();
        return app;
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private static void RegisterHardwareTimestampProvider(IServiceCollection services)
    {
        if (OperatingSystem.IsLinux())
        {
#pragma warning disable CA1416 // Validated by OperatingSystem.IsLinux()
            services.TryAddSingleton<IHardwareTimestampProvider,
                Native.Linux.LinuxHardwareTimestampProvider>();
#pragma warning restore CA1416
        }
        else
        {
            // No hardware timestamp provider on Windows / macOS.
            // TCP_INFO on Windows is handled directly by the transport shim.
            services.TryAddSingleton<IHardwareTimestampProvider>(
                _ => NullHardwareTimestampProvider.Instance);
        }
    }
}
