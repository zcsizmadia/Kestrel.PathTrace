using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Kestrel.PathTrace.Abstractions;

using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;

namespace Kestrel.PathTrace.Transport;

/// <summary>
/// Wraps an accepted <see cref="IConnectionListener"/> and attaches a
/// <see cref="ConnectionTelemetryState"/> to every new connection.
/// </summary>
internal sealed class InstrumentedConnectionListener : IConnectionListener
{
    private readonly IConnectionListener _inner;
    private readonly IHardwareTimestampProvider? _hwTimestampProvider;
    private readonly TransportInstrumentationOptions _options;
    private readonly ClockCalibration _clockCalibration;

    internal InstrumentedConnectionListener(
        IConnectionListener inner,
        IHardwareTimestampProvider? hwTimestampProvider,
        TransportInstrumentationOptions options)
    {
        _inner               = inner;
        _hwTimestampProvider = hwTimestampProvider;
        _options             = options;

        // Sample clocks once per listener (bind point) so that all connections
        // share the same calibration reference.
        _clockCalibration = hwTimestampProvider?.SampleClocks() ?? default;
    }

    /// <inheritdoc />
    public EndPoint EndPoint => _inner.EndPoint;

    /// <inheritdoc />
    public async ValueTask<ConnectionContext?> AcceptAsync(CancellationToken cancellationToken = default)
    {
        ConnectionContext? ctx = await _inner.AcceptAsync(cancellationToken);

        if (ctx is null)
        {
            return null;
        }

        long acceptedAt = Stopwatch.GetTimestamp();

        ConnectionTelemetryState state = BuildState(ctx, acceptedAt);
        ctx.Features.Set(state);

        return ctx;
    }

    /// <inheritdoc />
    public ValueTask UnbindAsync(CancellationToken cancellationToken = default) =>
        _inner.UnbindAsync(cancellationToken);

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _inner.DisposeAsync();

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private ConnectionTelemetryState BuildState(ConnectionContext ctx, long acceptedAt)
    {
        nint socketHandle = TryGetSocketHandle(ctx);

        string ifname     = string.Empty;
        NicTimestampCapabilities? caps = null;

        if (_hwTimestampProvider is not null && socketHandle != nint.Zero)
        {
            if (_options.EnableHardwareTimestamping)
            {
                caps    = _hwTimestampProvider.QueryCapabilities(socketHandle);
                ifname  = caps?.InterfaceName ?? string.Empty;
                _hwTimestampProvider.EnableTimestamping(socketHandle, preferHardware: true);
            }
        }

        return new ConnectionTelemetryState
        {
            SocketHandle        = socketHandle,
            NicCapabilities     = caps,
            ClockCalibration    = caps is not null ? _clockCalibration : null,
            InterfaceName       = ifname,
            T0_ConnectionAccepted = acceptedAt,
            AddressFamily       = GetAddressFamily(ctx),
        };
    }

    private static nint TryGetSocketHandle(ConnectionContext ctx)
    {
        // Kestrel exposes the Socket via IConnectionSocketFeature.
        if (ctx.Features.Get<IConnectionSocketFeature>() is { } socketFeature)
        {
            try
            {
                return socketFeature.Socket.Handle;
            }
            catch
            {
                return nint.Zero;
            }
        }

        return nint.Zero;
    }

    private static System.Net.Sockets.AddressFamily GetAddressFamily(ConnectionContext ctx)
    {
        if (ctx.Features.Get<IConnectionSocketFeature>() is { } socketFeature)
        {
            try
            {
                return socketFeature.Socket.AddressFamily;
            }
            catch
            {
                // ignore
            }
        }

        return System.Net.Sockets.AddressFamily.Unknown;
    }
}
