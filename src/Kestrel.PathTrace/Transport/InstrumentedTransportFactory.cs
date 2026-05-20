using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

using Kestrel.PathTrace.Abstractions;

using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;

namespace Kestrel.PathTrace.Transport;

/// <summary>
/// Wraps Kestrel's default <see cref="SocketTransportFactory"/> to inject
/// per-connection timestamping and TCP_INFO instrumentation.
/// </summary>
public sealed class InstrumentedTransportFactory : IConnectionListenerFactory
{
    private readonly IConnectionListenerFactory _inner;
    private readonly IHardwareTimestampProvider? _hwTimestampProvider;
    private readonly TransportInstrumentationOptions _options;

    /// <summary>
    /// Initialises the factory.
    /// </summary>
    /// <param name="inner">The real transport factory to delegate to.</param>
    /// <param name="hwTimestampProvider">
    /// Platform-specific hardware timestamp provider.
    /// Pass <see langword="null"/> to disable NIC timestamping.
    /// </param>
    /// <param name="options">Instrumentation options.</param>
    public InstrumentedTransportFactory(
        IConnectionListenerFactory inner,
        IHardwareTimestampProvider? hwTimestampProvider,
        TransportInstrumentationOptions? options = null)
    {
        _inner                = inner;
        _hwTimestampProvider  = hwTimestampProvider;
        _options              = options ?? new TransportInstrumentationOptions();
    }

    /// <inheritdoc />
    public async ValueTask<IConnectionListener> BindAsync(
        EndPoint endpoint,
        CancellationToken cancellationToken = default)
    {
        IConnectionListener listener = await _inner.BindAsync(endpoint, cancellationToken);
        return new InstrumentedConnectionListener(listener, _hwTimestampProvider, _options);
    }
}
