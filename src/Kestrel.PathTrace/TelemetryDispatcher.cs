using Kestrel.PathTrace.Abstractions;

using Microsoft.AspNetCore.Http;

namespace Kestrel.PathTrace;

/// <summary>
/// Fans out telemetry events to multiple <see cref="IRequestPathTelemetrySink"/> instances.
/// </summary>
public sealed class TelemetryDispatcher : IRequestPathTelemetrySink
{
    private readonly IRequestPathTelemetrySink[] _sinks;

    /// <summary>
    /// Initialises the dispatcher with one or more sinks.
    /// </summary>
    public TelemetryDispatcher(params IRequestPathTelemetrySink[] sinks)
    {
        _sinks = sinks;
    }

    /// <inheritdoc />
    public void Record(HttpContext context, RequestPathTelemetry telemetry)
    {
        foreach (IRequestPathTelemetrySink sink in _sinks)
        {
            sink.Record(context, telemetry);
        }
    }
}
