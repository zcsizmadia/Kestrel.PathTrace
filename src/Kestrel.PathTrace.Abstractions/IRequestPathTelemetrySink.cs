using Microsoft.AspNetCore.Http;

namespace Kestrel.PathTrace.Abstractions;

/// <summary>
/// Receives a completed <see cref="RequestPathTelemetry"/> record at the end
/// of every HTTP request.  Implementations write to Prometheus, OTel, etc.
/// </summary>
public interface IRequestPathTelemetrySink
{
    /// <summary>Records telemetry for a completed request.</summary>
    void Record(HttpContext context, RequestPathTelemetry telemetry);
}
