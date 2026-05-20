using Kestrel.PathTrace.Abstractions;
using Kestrel.PathTrace.Native.Linux;

using TUnit.Assertions;
using TUnit.Core;

using System.Runtime.Versioning;

namespace Kestrel.PathTrace.Tests.Native.Linux;

[SupportedOSPlatform("linux")]
public sealed class NicTimestampProbeTests
{
    [Test]
    public async Task GetCapabilities_ReturnsNull_ForEmptyInterfaceName()
    {
        NicTimestampProbe probe = new();
        NicTimestampCapabilities? caps = probe.GetCapabilities(string.Empty);

        await Assert.That(caps).IsNull();
    }

    [Test]
    public async Task GetCapabilities_ReturnsNull_ForNullInterfaceName()
    {
        NicTimestampProbe probe = new();
        NicTimestampCapabilities? caps = probe.GetCapabilities(null!);

        await Assert.That(caps).IsNull();
    }

    [Test]
    [Skip("Requires libhwtstamp_shim.so — run on Linux")]
    public async Task GetCapabilities_IsCached_SecondCallDoesNotHitNative()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        NicTimestampProbe probe = new();

        NicTimestampCapabilities? first  = probe.GetCapabilities("lo");
        NicTimestampCapabilities? second = probe.GetCapabilities("lo");

        // Reference equality — same object from cache.
        await Assert.That(ReferenceEquals(first, second)).IsTrue();
    }

    [Test]
    [Skip("Requires libhwtstamp_shim.so — run on Linux")]
    public async Task Invalidate_ClearsCache()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        NicTimestampProbe probe = new();

        NicTimestampCapabilities? first = probe.GetCapabilities("lo");
        probe.Invalidate("lo");
        NicTimestampCapabilities? second = probe.GetCapabilities("lo");

        // After invalidation, a new object is returned (different reference).
        // Both may be null if native lib is not present — but the references differ.
        bool sameRef = ReferenceEquals(first, second);
        await Assert.That(sameRef).IsFalse();
    }
}
