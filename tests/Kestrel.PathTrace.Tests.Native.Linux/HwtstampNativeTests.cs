using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;

using Kestrel.PathTrace.Abstractions;
using Kestrel.PathTrace.Native.Linux;

using TUnit.Assertions;
using TUnit.Core;

namespace Kestrel.PathTrace.Tests.Native.Linux;

/// <summary>
/// Integration tests that exercise <c>libhwtstamp_shim.so</c> directly.
/// Every test that P/Invokes into the native library is guarded with
/// <c>if (!OperatingSystem.IsLinux()) return;</c> so the suite passes
/// on non-Linux without attempting to load the shared object.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class HwtstampNativeTests
{
    // -----------------------------------------------------------------------
    // SampleClocks
    // -----------------------------------------------------------------------

    [Test]
    public async Task SampleClocks_AllFourClocks_ArePositive()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        ClockCalibration cal = HwtstampInterop.SampleClocks();

        await Assert.That(cal.MonotonicNs).IsGreaterThan(0L);
        await Assert.That(cal.RealtimeNs).IsGreaterThan(0L);
        await Assert.That(cal.TaiNs).IsGreaterThan(0L);
        await Assert.That(cal.RawMonotonicNs).IsGreaterThan(0L);
    }

    [Test]
    public async Task SampleClocks_CalledTwice_MonotonicIsNonDecreasing()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        ClockCalibration first  = HwtstampInterop.SampleClocks();
        ClockCalibration second = HwtstampInterop.SampleClocks();

        // CLOCK_MONOTONIC must never go backwards.
        await Assert.That(second.MonotonicNs).IsGreaterThan(first.MonotonicNs - 1);
    }

    // -----------------------------------------------------------------------
    // QueryNicCapabilities
    // -----------------------------------------------------------------------

    [Test]
    public async Task QueryNicCapabilities_Loopback_IsNotNull()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        NicTimestampCapabilities? caps = HwtstampInterop.QueryNicCapabilities("lo");

        await Assert.That(caps).IsNotNull();
    }

    [Test]
    public async Task QueryNicCapabilities_Loopback_HasNoHardwareTimestamping()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        NicTimestampCapabilities? caps = HwtstampInterop.QueryNicCapabilities("lo");

        await Assert.That(caps).IsNotNull();
        await Assert.That(caps!.HardwareRxAvailable).IsFalse();
        await Assert.That(caps.PhcIndex).IsEqualTo(-1);
    }

    [Test]
    public async Task QueryNicCapabilities_Loopback_PopulatesInterfaceName()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        NicTimestampCapabilities? caps = HwtstampInterop.QueryNicCapabilities("lo");

        await Assert.That(caps).IsNotNull();
        await Assert.That(caps!.InterfaceName).IsEqualTo("lo");
    }

    [Test]
    public async Task QueryNicCapabilities_NonexistentInterface_ReturnsNull()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        NicTimestampCapabilities? caps = HwtstampInterop.QueryNicCapabilities("nonexistent99");

        await Assert.That(caps).IsNull();
    }

    // -----------------------------------------------------------------------
    // EnableSocketTimestamps
    // -----------------------------------------------------------------------

    [Test]
    public async Task EnableSocketTimestamps_ValidUdpSocket_ReturnsTrue()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using Socket sock = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        sock.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        int fd = (int)sock.Handle;

        bool result = HwtstampInterop.EnableSocketTimestamps(fd, SoTimestampingFlags.RecommendedSwOnly);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task EnableSocketTimestamps_InvalidFd_ReturnsFalse()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        bool result = HwtstampInterop.EnableSocketTimestamps(-1, SoTimestampingFlags.RecommendedSwOnly);

        await Assert.That(result).IsFalse();
    }

    // -----------------------------------------------------------------------
    // EnableBestAvailableTimestamps
    // -----------------------------------------------------------------------

    [Test]
    public async Task EnableBestAvailableTimestamps_NullCaps_ReturnsSwOnlyFlags()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using Socket sock = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        sock.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        int fd = (int)sock.Handle;

        SoTimestampingFlags applied = HwtstampInterop.EnableBestAvailableTimestamps(fd, caps: null);

        await Assert.That(applied).IsEqualTo(SoTimestampingFlags.RecommendedSwOnly);
    }

    [Test]
    public async Task EnableBestAvailableTimestamps_LoopbackCaps_ReturnsSwOnlyFlags()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using Socket sock = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        sock.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        int fd = (int)sock.Handle;

        // Loopback has no PHC, so IsFullHardwareTimestampingAvailable = false.
        NicTimestampCapabilities? loopbackCaps = HwtstampInterop.QueryNicCapabilities("lo");
        SoTimestampingFlags applied = HwtstampInterop.EnableBestAvailableTimestamps(fd, loopbackCaps);

        await Assert.That(applied).IsEqualTo(SoTimestampingFlags.RecommendedSwOnly);
    }

    [Test]
    public async Task EnableBestAvailableTimestamps_FullHwCaps_ReturnsHwRxFlags()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using Socket sock = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        sock.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        int fd = (int)sock.Handle;

        // Synthesize capabilities that pass IsFullHardwareTimestampingAvailable.
        NicTimestampCapabilities fullHwCaps = new()
        {
            InterfaceName        = "eth0",
            HardwareRxAvailable  = true,
            RawHardwareAvailable = true,
            PhcIndex             = 0,
        };

        SoTimestampingFlags applied = HwtstampInterop.EnableBestAvailableTimestamps(fd, fullHwCaps);

        await Assert.That(applied).IsEqualTo(SoTimestampingFlags.RecommendedHwRx);
    }

    // -----------------------------------------------------------------------
    // GetSocketInterfaceName
    // -----------------------------------------------------------------------

    [Test]
    public async Task GetSocketInterfaceName_BoundLoopbackSocket_ReturnsLo()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using Socket sock = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        sock.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        int fd = (int)sock.Handle;

        string ifname = HwtstampInterop.GetSocketInterfaceName(fd);

        await Assert.That(ifname).IsEqualTo("lo");
    }

    [Test]
    public async Task GetSocketInterfaceName_InvalidFd_ReturnsEmpty()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string ifname = HwtstampInterop.GetSocketInterfaceName(-1);

        await Assert.That(string.IsNullOrEmpty(ifname)).IsTrue();
    }

    // -----------------------------------------------------------------------
    // SW timestamp round-trip via UDP loopback
    // -----------------------------------------------------------------------

    /// <summary>Creates a bound UDP receiver and an unbound sender on loopback.</summary>
    private static (Socket sender, Socket receiver, int port) CreateLoopbackUdpPair()
    {
        Socket receiver = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        receiver.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)receiver.LocalEndPoint!).Port;

        Socket sender = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        return (sender, receiver, port);
    }

    [Test]
    public async Task RecvWithTimestamp_SwTimestamp_IsValid_AfterSend()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var (sender, receiver, port) = CreateLoopbackUdpPair();
        using (sender)
        using (receiver)
        {
            int fd = (int)receiver.Handle;
            HwtstampInterop.EnableSocketTimestamps(fd, SoTimestampingFlags.RecommendedSwOnly);

            sender.SendTo("hwtstamp-test"u8.ToArray(), new IPEndPoint(IPAddress.Loopback, port));

            byte[] buf = new byte[64];
            int received = HwtstampInterop.RecvWithTimestamp(fd, buf.AsSpan(), out PacketTimestamps timestamps);

            await Assert.That(received).IsGreaterThan(0);
            await Assert.That(timestamps.HasAny).IsTrue();
            await Assert.That(timestamps.Software.IsValid).IsTrue();
        }
    }

    [Test]
    public async Task RecvWithTimestamp_SwTimestamp_HasPositiveNanotime()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var (sender, receiver, port) = CreateLoopbackUdpPair();
        using (sender)
        using (receiver)
        {
            int fd = (int)receiver.Handle;
            HwtstampInterop.EnableSocketTimestamps(fd, SoTimestampingFlags.RecommendedSwOnly);

            sender.SendTo("probe"u8.ToArray(), new IPEndPoint(IPAddress.Loopback, port));

            HwtstampInterop.RecvWithTimestamp(fd, new byte[64].AsSpan(), out PacketTimestamps ts);

            // SW timestamp is CLOCK_REALTIME — billions of ns since the epoch.
            await Assert.That(ts.Software.TotalNanoseconds).IsGreaterThan(0L);
        }
    }

    [Test]
    public async Task RecvWithTimestamp_BytesReceived_MatchesSentLength()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var (sender, receiver, port) = CreateLoopbackUdpPair();
        using (sender)
        using (receiver)
        {
            int fd = (int)receiver.Handle;
            HwtstampInterop.EnableSocketTimestamps(fd, SoTimestampingFlags.RecommendedSwOnly);

            byte[] payload = new byte[42];
            Random.Shared.NextBytes(payload);
            sender.SendTo(payload, new IPEndPoint(IPAddress.Loopback, port));

            int received = HwtstampInterop.RecvWithTimestamp(fd, new byte[256].AsSpan(), out _);

            await Assert.That(received).IsEqualTo(payload.Length);
        }
    }

    // -----------------------------------------------------------------------
    // SocketHwTimestampReader
    // -----------------------------------------------------------------------

    [Test]
    public async Task SocketHwTimestampReader_TryReadRxTimestamp_WithSwData_ReturnsTrueAndValidTimestamp()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var (sender, receiver, port) = CreateLoopbackUdpPair();
        using (sender)
        using (receiver)
        {
            int fd = (int)receiver.Handle;
            HwtstampInterop.EnableSocketTimestamps(fd, SoTimestampingFlags.RecommendedSwOnly);

            sender.SendTo("reader-test"u8.ToArray(), new IPEndPoint(IPAddress.Loopback, port));

            SocketHwTimestampReader reader = new();
            bool ok = reader.TryReadRxTimestamp(fd, out PacketTimestamps ts);

            await Assert.That(ok).IsTrue();
            await Assert.That(ts.HasAny).IsTrue();
            await Assert.That(ts.Software.IsValid).IsTrue();
        }
    }

    // -----------------------------------------------------------------------
    // NicTimestampProbe — InvalidateAll
    // -----------------------------------------------------------------------

    [Test]
    public async Task NicTimestampProbe_InvalidateAll_CausesRefreshOnNextQuery()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        NicTimestampProbe probe = new();

        // Prime the cache.
        NicTimestampCapabilities? before = probe.GetCapabilities("lo");

        probe.InvalidateAll();

        // After clearing, the probe must re-query; a new object is returned.
        NicTimestampCapabilities? after = probe.GetCapabilities("lo");

        await Assert.That(before).IsNotNull();
        await Assert.That(after).IsNotNull();
        // Reference equality must differ — the cache was cleared.
        await Assert.That(ReferenceEquals(before, after)).IsFalse();
    }

    // -----------------------------------------------------------------------
    // LinuxHardwareTimestampProvider
    // -----------------------------------------------------------------------

    [Test]
    public async Task LinuxHardwareTimestampProvider_QueryCapabilities_ReturnsNull_ForNegativeHandle()
    {
        // nint(-1) → fd = -1 → early return in ToFd; no DLL call needed.
        LinuxHardwareTimestampProvider provider = new();

        NicTimestampCapabilities? caps = provider.QueryCapabilities(new nint(-1));

        await Assert.That(caps).IsNull();
    }

    [Test]
    public async Task LinuxHardwareTimestampProvider_EnableTimestamping_NegativeHandle_ReturnsFalse()
    {
        // nint(-1) → fd = -1 → early return false; no DLL call needed.
        LinuxHardwareTimestampProvider provider = new();

        bool result = provider.EnableTimestamping(new nint(-1));

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task LinuxHardwareTimestampProvider_EnableTimestamping_BoundSocket_ReturnsTrue()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        LinuxHardwareTimestampProvider provider = new();

        using Socket sock = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        sock.Bind(new IPEndPoint(IPAddress.Loopback, 0));

        bool result = provider.EnableTimestamping(sock.Handle, preferHardware: false);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task LinuxHardwareTimestampProvider_SampleClocks_ReturnsNonZeroMonotonic()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        LinuxHardwareTimestampProvider provider = new();
        ClockCalibration cal = provider.SampleClocks();

        await Assert.That(cal.MonotonicNs).IsGreaterThan(0L);
    }
}
