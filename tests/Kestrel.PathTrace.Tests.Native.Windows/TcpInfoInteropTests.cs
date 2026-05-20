using Kestrel.PathTrace.Native.Windows;

using TUnit.Assertions;
using TUnit.Core;

using System.Runtime.Versioning;

namespace Kestrel.PathTrace.Tests.Native.Windows;

[SupportedOSPlatform("windows")]
public sealed class TcpInfoInteropTests
{
    [Test]
    public async Task TryGetTcpInfoV0_ReturnsFalse_ForZeroHandle()
    {
        bool result = TcpInfoInterop.TryGetTcpInfoV0(nint.Zero, out _);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task GetTcpInfoV0_ReturnsNull_ForZeroHandle()
    {
        TcpInfoV0? info = TcpInfoInterop.GetTcpInfoV0(nint.Zero);
        await Assert.That(info).IsNull();
    }

    [Test]
    [Skip("Requires tcpinfo_shim.dll — run on Windows with native build")]
    public async Task TryGetTcpInfoV0_SucceedsForRealSocket()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using System.Net.Sockets.TcpClient client = new();
        await client.ConnectAsync("127.0.0.1", 80);

        bool result = TcpInfoInterop.TryGetTcpInfoV0(client.Client.Handle, out TcpInfoV0 info);

        if (result)
        {
            await Assert.That(info.RttUs).IsGreaterThan(0u);
        }
    }
}
