using System.Runtime.Versioning;

using Kestrel.PathTrace.Abstractions;

namespace Kestrel.PathTrace.Native.Linux;

/// <summary>
/// Reads RX hardware timestamps for an accepted connection by performing a
/// single <c>recvmsg()</c> on a socket that has SO_TIMESTAMPING enabled.
/// </summary>
/// <remarks>
/// The transport layer calls <see cref="TryReadRxTimestamp"/> immediately after
/// accepting the connection (or after the first read), before handing the data
/// to the HTTP parser.  The resulting <see cref="PacketTimestamps"/> is stored
/// in <see cref="ConnectionTelemetryState.LastRxTimestamp"/>.
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class SocketHwTimestampReader
{
    private readonly byte[] _peekBuffer = new byte[1];

    /// <summary>
    /// Attempts to read the RX timestamp from the socket's pending control message.
    /// </summary>
    /// <param name="socketFd">Linux socket file descriptor.</param>
    /// <param name="timestamps">Extracted timestamps on success.</param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="timestamps"/> contains at least
    /// one valid timestamp.
    /// </returns>
    public bool TryReadRxTimestamp(int socketFd, out PacketTimestamps timestamps)
    {
        int received = HwtstampInterop.RecvWithTimestamp(
            socketFd, _peekBuffer.AsSpan(), out timestamps);

        return received >= 0 && timestamps.HasAny;
    }

    /// <summary>
    /// Attempts to read a TX hardware timestamp from the socket error queue.
    /// </summary>
    /// <param name="socketFd">Linux socket file descriptor.</param>
    /// <param name="timestamps">Extracted timestamps on success.</param>
    /// <returns>
    /// <see langword="true"/> if a TX hardware timestamp was available.
    /// </returns>
    public bool TryReadTxTimestamp(int socketFd, out PacketTimestamps timestamps)
    {
        HwtsError err = HwtstampInterop.ReadTxTimestamp(socketFd, out timestamps);
        return err == HwtsError.Ok && timestamps.HasAny;
    }
}
