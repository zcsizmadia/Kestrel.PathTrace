using System.Diagnostics;

using Kestrel.PathTrace.Abstractions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kestrel.PathTrace.Middleware;

/// <summary>
/// Top-level ASP.NET Core middleware that instruments the HTTP request pipeline
/// and dispatches a <see cref="RequestPathTelemetry"/> record to all registered
/// <see cref="IRequestPathTelemetrySink"/> instances at the end of the request.
/// </summary>
public sealed class RequestPathTelemetryMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IRequestPathTelemetrySink _sink;

    /// <summary>
    /// Initialises the middleware.
    /// </summary>
    public RequestPathTelemetryMiddleware(
        RequestDelegate next,
        IRequestPathTelemetrySink sink)
    {
        _next = next;
        _sink = sink;
    }

    /// <summary>Processes an HTTP request.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        long middlewareStart = Stopwatch.GetTimestamp();

        ConnectionTelemetryState? connState =
            context.Features.Get<ConnectionTelemetryState>();

        RequestPathTelemetry telemetry = BuildInitialTelemetry(context, connState, middlewareStart);

        try
        {
            telemetry.T6_EndpointStart = Stopwatch.GetTimestamp();
            await _next(context);
            telemetry.T7_EndpointEnd = Stopwatch.GetTimestamp();
        }
        finally
        {
            telemetry.T8_ResponseWriteStart = Stopwatch.GetTimestamp();

            // Capture response metadata before the body might be flushed
            telemetry.StatusCode = context.Response.StatusCode;
            telemetry.Route      = GetRoute(context);

            telemetry.T9_ResponseWriteEnd    = Stopwatch.GetTimestamp();
            telemetry.T10_TransportWriteStart = Stopwatch.GetTimestamp();

            _sink.Record(context, telemetry);
        }
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private static RequestPathTelemetry BuildInitialTelemetry(
        HttpContext context,
        ConnectionTelemetryState? connState,
        long middlewareStart)
    {
        RequestPathTelemetry t = new()
        {
            Method             = context.Request.Method,
            T5_MiddlewareStart = middlewareStart,

            // The transport read and HTTP parse timings are best-effort
            // approximations when the internal transport hook is not used.
            T2_TransportRead       = middlewareStart,
            T3_HttpParseStart      = middlewareStart,
            T4_HttpHeadersComplete = middlewareStart,
        };

        if (connState is not null)
        {
            t.ClockCalibration = connState.ClockCalibration;

            if (connState.LastRxTimestamp is { } rxTs)
            {
                t.NicRxHardware = rxTs.HardwareRaw;
                t.NicRxSoftware = rxTs.Software;
            }
        }

        return t;
    }

    private static string GetRoute(HttpContext context)
    {
        if (context.GetEndpoint() is RouteEndpoint { RoutePattern.RawText: { } route })
        {
            return route;
        }

        return context.Request.Path.Value ?? string.Empty;
    }
}
