using Kestrel.PathTrace.Abstractions;
using Kestrel.PathTrace.Native.Linux;

using TUnit.Assertions;
using TUnit.Core;

namespace Kestrel.PathTrace.Tests.Native.Linux;

/// <summary>
/// Tests for <see cref="HwtstampInterop"/> and related types.
/// Tests that require the actual native library are skipped on non-Linux.
/// </summary>
public sealed class HwtstampInteropTests
{
    // -----------------------------------------------------------------------
    // Enum / flag consistency
    // -----------------------------------------------------------------------

    [Test]
    public async Task SoTimestampingFlags_RecommendedHwRx_IncludesRequiredBits()
    {
        SoTimestampingFlags flags = SoTimestampingFlags.RecommendedHwRx;

        await Assert.That(flags.HasFlag(SoTimestampingFlags.RxHardware)).IsTrue();
        await Assert.That(flags.HasFlag(SoTimestampingFlags.RawHardware)).IsTrue();
        await Assert.That(flags.HasFlag(SoTimestampingFlags.RxSoftware)).IsTrue();
    }

    [Test]
    public async Task SoTimestampingFlags_RecommendedSwOnly_DoesNotIncludeHwBits()
    {
        SoTimestampingFlags flags = SoTimestampingFlags.RecommendedSwOnly;

        await Assert.That(flags.HasFlag(SoTimestampingFlags.RxHardware)).IsFalse();
        await Assert.That(flags.HasFlag(SoTimestampingFlags.RawHardware)).IsFalse();
    }

    // -----------------------------------------------------------------------
    // NicTimestampCapabilities mapping
    // -----------------------------------------------------------------------

    [Test]
    public async Task NicCapabilities_IsFullHardwareTimestampingAvailable_RequiresPhc()
    {
        NicTimestampCapabilities caps = new()
        {
            HardwareRxAvailable  = true,
            RawHardwareAvailable = true,
            PhcIndex             = -1, // no PHC
        };

        await Assert.That(caps.IsFullHardwareTimestampingAvailable).IsFalse();
    }

    [Test]
    public async Task NicCapabilities_IsFullHardwareTimestampingAvailable_TrueWhenPhcPresent()
    {
        NicTimestampCapabilities caps = new()
        {
            HardwareRxAvailable  = true,
            RawHardwareAvailable = true,
            PhcIndex             = 0,
        };

        await Assert.That(caps.IsFullHardwareTimestampingAvailable).IsTrue();
    }

    // -----------------------------------------------------------------------
    // SampleClocks — available on Linux only
    // -----------------------------------------------------------------------

    [Test]
    [Skip("Requires libhwtstamp_shim.so — run on Linux with native build")]
    public async Task SampleClocks_ReturnsNonZeroMonotonicNs()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        ClockCalibration cal = HwtstampInterop.SampleClocks();

        await Assert.That(cal.MonotonicNs).IsGreaterThan(0L);
        await Assert.That(cal.RealtimeNs).IsGreaterThan(0L);
        await Assert.That(cal.TaiNs).IsGreaterThan(0L);
    }

    // -----------------------------------------------------------------------
    // QueryNicCapabilities — available on Linux only
    // -----------------------------------------------------------------------

    [Test]
    [Skip("Requires libhwtstamp_shim.so and a real network interface")]
    public async Task QueryNicCapabilities_LoopbackHasNoHwTimestamping()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        // The loopback interface never supports hardware timestamping.
        NicTimestampCapabilities? caps = HwtstampInterop.QueryNicCapabilities("lo");

        await Assert.That(caps).IsNotNull();
        await Assert.That(caps!.HardwareRxAvailable).IsFalse();
        await Assert.That(caps.PhcIndex).IsEqualTo(-1);
    }

    // -----------------------------------------------------------------------
    // EnableBestAvailableTimestamps — falls back to SW when caps is null
    // -----------------------------------------------------------------------

    [Test]
    [Skip("Requires libhwtstamp_shim.so — run on Linux")]
    public async Task EnableBestAvailableTimestamps_NullCaps_UsesSoftwareFlags()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        // Open a loopback UDP socket for the test.
        using System.Net.Sockets.Socket sock = new(
            System.Net.Sockets.AddressFamily.InterNetwork,
            System.Net.Sockets.SocketType.Dgram,
            System.Net.Sockets.ProtocolType.Udp);

        sock.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));

        int fd = (int)sock.Handle;
        SoTimestampingFlags applied = HwtstampInterop.EnableBestAvailableTimestamps(fd, caps: null);

        await Assert.That(applied).IsEqualTo(SoTimestampingFlags.RecommendedSwOnly);
    }
}
