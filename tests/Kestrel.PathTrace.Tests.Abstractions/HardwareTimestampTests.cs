using System.Diagnostics;

using Kestrel.PathTrace.Abstractions;

using TUnit.Assertions;
using TUnit.Core;

namespace Kestrel.PathTrace.Tests.Abstractions;

public sealed class HardwareTimestampTests
{
    [Test]
    public async Task Default_IsInvalid()
    {
        HardwareTimestamp ts = default;

        await Assert.That(ts.IsValid).IsFalse();
        await Assert.That(ts.Seconds).IsEqualTo(0L);
        await Assert.That(ts.Nanoseconds).IsEqualTo(0L);
    }

    [Test]
    public async Task Invalid_Singleton_IsInvalid()
    {
        await Assert.That(HardwareTimestamp.Invalid.IsValid).IsFalse();
    }

    [Test]
    public async Task TotalNanoseconds_IsCorrect()
    {
        HardwareTimestamp ts = new() { Seconds = 1L, Nanoseconds = 500_000_000L, IsValid = true };

        await Assert.That(ts.TotalNanoseconds).IsEqualTo(1_500_000_000L);
    }

    [Test]
    public async Task ToString_ValidTimestamp()
    {
        HardwareTimestamp ts = new() { Seconds = 42L, Nanoseconds = 123_456_789L, IsValid = true };
        string s = ts.ToString();

        await Assert.That(s).IsEqualTo("42.123456789");
    }

    [Test]
    public async Task ToString_InvalidTimestamp()
    {
        await Assert.That(HardwareTimestamp.Invalid.ToString()).IsEqualTo("<invalid>");
    }
}
