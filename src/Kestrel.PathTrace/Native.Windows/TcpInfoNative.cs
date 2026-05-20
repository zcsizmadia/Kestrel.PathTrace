using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Kestrel.PathTrace.Native.Windows;

/// <summary>
/// P/Invoke declarations for <c>tcpinfo_shim.dll</c>.
/// Only call from Windows; the C# layer gates all entry points behind
/// <see cref="OperatingSystem.IsWindows()"/>.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class TcpInfoNative
{
    private const string LibName = "tcpinfo_shim";

    /// <summary>
    /// Queries TCP_INFO_v0 for the given socket handle via <c>SIO_TCP_INFO</c>.
    /// </summary>
    /// <param name="socketHandle">Native socket handle (SOCKET).</param>
    /// <param name="info">Output: populated TCP info structure.</param>
    /// <returns>0 on success, or a WSA error code on failure.</returns>
    [LibraryImport(LibName, EntryPoint = "get_tcp_info_v0")]
    internal static partial int GetTcpInfoV0(nint socketHandle, out TcpInfoV0 info);
}
