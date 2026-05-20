namespace Kestrel.PathTrace.Abstractions;

/// <summary>
/// Well-known constants shared across Kestrel.PathTrace packages.
/// </summary>
public static class PathTraceDefaults
{
    /// <summary>
    /// Keyed-service key used when registering individual
    /// <see cref="IRequestPathTelemetrySink"/> implementations so that
    /// <c>TelemetryDispatcher</c> can collect them via
    /// <c>IServiceProvider.GetKeyedServices&lt;IRequestPathTelemetrySink&gt;</c>.
    /// </summary>
    public const string SinkKey = "kestrel-path-trace-sink";
}
