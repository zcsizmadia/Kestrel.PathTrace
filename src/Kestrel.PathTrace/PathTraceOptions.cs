using Kestrel.PathTrace.Transport;

namespace Kestrel.PathTrace;

/// <summary>
/// Top-level options for Kestrel.PathTrace.
/// </summary>
public sealed class PathTraceOptions
{
    /// <summary>
    /// Gets or sets transport instrumentation options.
    /// <see langword="null"/> uses the defaults.
    /// </summary>
    public TransportInstrumentationOptions? Transport { get; set; }

    /// <summary>
    /// Samples 1 in every N requests (1 = all, 10 = every 10th, 100 = every 100th).
    /// Values less than 1 are treated as 1 (full sampling).
    /// Use the bandwidth benchmark to find a suitable value for your workload:
    ///   dotnet run -c Release --project benchmarks/Kestrel.PathTrace.Benchmarks -- --bandwidth
    /// </summary>
    public int SampleRate { get; set; } = 1;

    /// <summary>
    /// Request path prefixes excluded from all instrumentation (case-insensitive prefix match).
    /// Bypasses timestamp collection and sink dispatch entirely for matching paths.
    /// Example: ["/health", "/ready", "/metrics"]
    /// </summary>
    public IList<string> ExcludedRoutePrefixes { get; set; } = [];
}
