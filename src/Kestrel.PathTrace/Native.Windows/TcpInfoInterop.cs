using System.Runtime.Versioning;

namespace Kestrel.PathTrace.Native.Windows;

/// <summary>
/// High-level wrapper around <see cref="TcpInfoNative"/> that handles errors
/// and returns nullable results instead of raw error codes.
/// </summary>
[SupportedOSPlatform("windows")]
public static class TcpInfoInterop
{
    static TcpInfoInterop() => NativeLibraryResolver.EnsureRegistered();

    /// <summary>
    /// Attempts to read <see cref="TcpInfoV0"/> for the given socket handle.
    /// </summary>
    /// <param name="socketHandle">The native socket handle.</param>
    /// <param name="info">When successful, contains the TCP info; otherwise <see langword="default"/>.</param>
    /// <returns><see langword="true"/> on success.</returns>
    public static bool TryGetTcpInfoV0(nint socketHandle, out TcpInfoV0 info)
    {
        if (socketHandle == nint.Zero)
        {
            info = default;
            return false;
        }

        int rc = TcpInfoNative.GetTcpInfoV0(socketHandle, out info);
        return rc == 0;
    }

    /// <summary>
    /// Returns a nullable <see cref="TcpInfoV0"/> for the given socket handle.
    /// </summary>
    public static TcpInfoV0? GetTcpInfoV0(nint socketHandle)
    {
        if (!TryGetTcpInfoV0(socketHandle, out TcpInfoV0 info))
        {
            return null;
        }

        return info;
    }
}
