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
}
