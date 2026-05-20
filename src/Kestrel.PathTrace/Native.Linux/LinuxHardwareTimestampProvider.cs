using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Kestrel.PathTrace.Abstractions;

namespace Kestrel.PathTrace.Native.Linux;

/// <summary>
/// <see cref="IHardwareTimestampProvider"/> implementation for Linux that uses
/// <c>hwtstamp_shim.so</c> to query NIC capabilities and enable SO_TIMESTAMPING.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxHardwareTimestampProvider : IHardwareTimestampProvider
{
    private readonly NicTimestampProbe _probe = new();

    /// <inheritdoc />
    public NicTimestampCapabilities? QueryCapabilities(nint socketHandle)
    {
        string ifname = GetIfname(socketHandle);

        if (string.IsNullOrEmpty(ifname))
        {
            return null;
        }

        return _probe.GetCapabilities(ifname);
    }

    /// <inheritdoc />
    public bool EnableTimestamping(nint socketHandle, bool preferHardware = true)
    {
        int fd = ToFd(socketHandle);

        if (fd < 0)
        {
            return false;
        }

        NicTimestampCapabilities? caps = preferHardware ? QueryCapabilities(socketHandle) : null;
        SoTimestampingFlags flags = HwtstampInterop.EnableBestAvailableTimestamps(fd, caps);

        return flags != SoTimestampingFlags.None;
    }

    /// <inheritdoc />
    public ClockCalibration SampleClocks() => HwtstampInterop.SampleClocks();

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private static int ToFd(nint socketHandle)
    {
        // On Linux the socket handle IS the file descriptor.
        int fd = (int)socketHandle;
        return fd >= 0 ? fd : -1;
    }

    private static string GetIfname(nint socketHandle)
    {
        int fd = ToFd(socketHandle);
        return fd < 0 ? string.Empty : HwtstampInterop.GetSocketInterfaceName(fd);
    }
}
