using Kestrel.PathTrace.Transport;

using TUnit.Assertions;
using TUnit.Core;

namespace Kestrel.PathTrace.Tests.Transport;

public sealed class TransportInstrumentationOptionsTests
{
    [Test]
    public async Task DefaultOptions_EnableHardwareTimestamping_IsTrue()
    {
        TransportInstrumentationOptions opts = new();
        await Assert.That(opts.EnableHardwareTimestamping).IsTrue();
    }

    [Test]
    public async Task DefaultOptions_EnableTxHardwareTimestamping_IsFalse()
    {
        TransportInstrumentationOptions opts = new();
        await Assert.That(opts.EnableTxHardwareTimestamping).IsFalse();
    }

    [Test]
    public async Task DefaultOptions_EnableWindowsTcpInfo_IsTrue()
    {
        TransportInstrumentationOptions opts = new();
        await Assert.That(opts.EnableWindowsTcpInfo).IsTrue();
    }
}
