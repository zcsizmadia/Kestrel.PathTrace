using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Kestrel.PathTrace.Native.Windows;

using TUnit.Assertions;
using TUnit.Core;

namespace Kestrel.PathTrace.Tests.Native.Windows;

/// <summary>
/// Full coverage tests for <see cref="TcpInfoInterop"/>, <see cref="TcpInfoV0"/>,
/// and the underlying <c>tcpinfo_shim.dll</c> P/Invoke.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TcpInfoInteropTests
{
    // Windows TCPSTATE_ESTAB from the SDK TCPSTATE enum.
    private const uint TcpStateEstab = 4;

    // -----------------------------------------------------------------------
    // TcpInfoV0 struct layout — no native library required
    // -----------------------------------------------------------------------

    [Test]
    public async Task TcpInfoV0_HasSequentialLayout()
    {
        // Type.IsLayoutSequential is the reliable way to check StructLayout on .NET;
        // GetCustomAttribute<StructLayoutAttribute>() returns null for pseudo-custom attributes.
        await Assert.That(typeof(TcpInfoV0).IsLayoutSequential).IsTrue();
    }

    [Test]
    public async Task TcpInfoV0_MarshalSize_IsAtLeast73Bytes()
    {
        // 18 × uint32 (4 bytes) + 1 × uint8 (1 byte) = 73 bytes minimum.
        int size = Marshal.SizeOf<TcpInfoV0>();

        await Assert.That(size).IsGreaterThan(72);
    }

    [Test]
    public async Task TcpInfoV0_Default_IsAllZero()
    {
        TcpInfoV0 info = default;

        await Assert.That(info.State).IsEqualTo(0u);
        await Assert.That(info.Mss).IsEqualTo(0u);
        await Assert.That(info.ConnectionTimeMs).IsEqualTo(0u);
        await Assert.That(info.TimestampsEnabled).IsEqualTo(0u);
        await Assert.That(info.RttUs).IsEqualTo(0u);
        await Assert.That(info.MinRttUs).IsEqualTo(0u);
        await Assert.That(info.BytesInFlight).IsEqualTo(0u);
        await Assert.That(info.Cwnd).IsEqualTo(0u);
        await Assert.That(info.SndWnd).IsEqualTo(0u);
        await Assert.That(info.RcvWnd).IsEqualTo(0u);
        await Assert.That(info.RcvBuf).IsEqualTo(0u);
        await Assert.That(info.BytesOut).IsEqualTo(0u);
        await Assert.That(info.BytesIn).IsEqualTo(0u);
        await Assert.That(info.BytesReordered).IsEqualTo(0u);
        await Assert.That(info.BytesRetrans).IsEqualTo(0u);
        await Assert.That(info.FastRetrans).IsEqualTo(0u);
        await Assert.That(info.DupAcksIn).IsEqualTo(0u);
        await Assert.That(info.TimeoutEpisodes).IsEqualTo(0u);
        await Assert.That(info.SynRetrans).IsEqualTo((byte)0);
    }

    [Test]
    public async Task TcpInfoV0_FieldAssignment_RoundTrips()
    {
        TcpInfoV0 info = new()
        {
            State             = 4,
            Mss               = 1460,
            ConnectionTimeMs  = 100,
            TimestampsEnabled = 1,
            RttUs             = 500,
            MinRttUs          = 200,
            BytesInFlight     = 8,
            Cwnd              = 10,
            SndWnd            = 65535,
            RcvWnd            = 65535,
            RcvBuf            = 131072,
            BytesOut          = 1024,
            BytesIn           = 512,
            BytesReordered    = 1,
            BytesRetrans      = 2,
            FastRetrans       = 3,
            DupAcksIn         = 4,
            TimeoutEpisodes   = 5,
            SynRetrans        = 6,
        };

        await Assert.That(info.State).IsEqualTo(4u);
        await Assert.That(info.Mss).IsEqualTo(1460u);
        await Assert.That(info.ConnectionTimeMs).IsEqualTo(100u);
        await Assert.That(info.TimestampsEnabled).IsEqualTo(1u);
        await Assert.That(info.RttUs).IsEqualTo(500u);
        await Assert.That(info.MinRttUs).IsEqualTo(200u);
        await Assert.That(info.BytesInFlight).IsEqualTo(8u);
        await Assert.That(info.Cwnd).IsEqualTo(10u);
        await Assert.That(info.SndWnd).IsEqualTo(65535u);
        await Assert.That(info.RcvWnd).IsEqualTo(65535u);
        await Assert.That(info.RcvBuf).IsEqualTo(131072u);
        await Assert.That(info.BytesOut).IsEqualTo(1024u);
        await Assert.That(info.BytesIn).IsEqualTo(512u);
        await Assert.That(info.BytesReordered).IsEqualTo(1u);
        await Assert.That(info.BytesRetrans).IsEqualTo(2u);
        await Assert.That(info.FastRetrans).IsEqualTo(3u);
        await Assert.That(info.DupAcksIn).IsEqualTo(4u);
        await Assert.That(info.TimeoutEpisodes).IsEqualTo(5u);
        await Assert.That(info.SynRetrans).IsEqualTo((byte)6);
    }

    // -----------------------------------------------------------------------
    // TcpInfoInterop — zero handle (no native library required)
    // -----------------------------------------------------------------------

    [Test]
    public async Task TryGetTcpInfoV0_ReturnsFalse_ForZeroHandle()
    {
        bool result = TcpInfoInterop.TryGetTcpInfoV0(nint.Zero, out _);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task TryGetTcpInfoV0_OutputIsDefault_WhenHandleIsZero()
    {
        TcpInfoInterop.TryGetTcpInfoV0(nint.Zero, out TcpInfoV0 info);

        await Assert.That(info.State).IsEqualTo(0u);
        await Assert.That(info.Mss).IsEqualTo(0u);
        await Assert.That(info.RttUs).IsEqualTo(0u);
    }

    [Test]
    public async Task GetTcpInfoV0_ReturnsNull_ForZeroHandle()
    {
        TcpInfoV0? info = TcpInfoInterop.GetTcpInfoV0(nint.Zero);

        await Assert.That(info).IsNull();
    }

    // -----------------------------------------------------------------------
    // TcpInfoInterop — invalid non-zero handle (exercises DLL error path)
    // -----------------------------------------------------------------------

    [Test]
    public async Task TryGetTcpInfoV0_ReturnsFalse_ForInvalidHandle()
    {
        // A non-zero value that is not a valid SOCKET handle.
        nint invalidHandle = new(0x1234_5678);

        bool result = TcpInfoInterop.TryGetTcpInfoV0(invalidHandle, out _);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task GetTcpInfoV0_ReturnsNull_ForInvalidHandle()
    {
        nint invalidHandle = new(0x1234_5678);

        TcpInfoV0? info = TcpInfoInterop.GetTcpInfoV0(invalidHandle);

        await Assert.That(info).IsNull();
    }

    // -----------------------------------------------------------------------
    // TcpInfoInterop — real loopback TCP connection (full DLL happy path)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Establishes a loopback TCP connection on an ephemeral port.
    /// All three objects must be disposed by the caller.
    /// </summary>
    private static async Task<(TcpListener listener, TcpClient client, TcpClient serverSide)>
        CreateLoopbackConnectionAsync()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        TcpClient client = new();
        await client.ConnectAsync(IPAddress.Loopback, port);
        TcpClient serverSide = await acceptTask;

        return (listener, client, serverSide);
    }

    [Test]
    public async Task TryGetTcpInfoV0_ReturnsTrue_ForConnectedSocket()
    {
        var (listener, client, serverSide) = await CreateLoopbackConnectionAsync();
        try
        {
            bool result = TcpInfoInterop.TryGetTcpInfoV0(client.Client.Handle, out _);

            await Assert.That(result).IsTrue();
        }
        finally
        {
            client.Dispose();
            serverSide.Dispose();
            listener.Stop();
        }
    }

    [Test]
    public async Task GetTcpInfoV0_ReturnsNonNull_ForConnectedSocket()
    {
        var (listener, client, serverSide) = await CreateLoopbackConnectionAsync();
        try
        {
            TcpInfoV0? info = TcpInfoInterop.GetTcpInfoV0(client.Client.Handle);

            await Assert.That(info).IsNotNull();
        }
        finally
        {
            client.Dispose();
            serverSide.Dispose();
            listener.Stop();
        }
    }

    [Test]
    public async Task GetTcpInfoV0_State_IsEstablished_ForConnectedSocket()
    {
        var (listener, client, serverSide) = await CreateLoopbackConnectionAsync();
        try
        {
            TcpInfoV0? info = TcpInfoInterop.GetTcpInfoV0(client.Client.Handle);

            await Assert.That(info).IsNotNull();
            await Assert.That(info!.Value.State).IsEqualTo(TcpStateEstab);
        }
        finally
        {
            client.Dispose();
            serverSide.Dispose();
            listener.Stop();
        }
    }

    [Test]
    public async Task GetTcpInfoV0_Mss_MeetsMinimumTcpSpec_ForConnectedSocket()
    {
        var (listener, client, serverSide) = await CreateLoopbackConnectionAsync();
        try
        {
            TcpInfoV0? info = TcpInfoInterop.GetTcpInfoV0(client.Client.Handle);

            await Assert.That(info).IsNotNull();
            // RFC 791 minimum IP payload is 576; TCP MSS floor is 536.
            // Loopback typically negotiates 65495.
            await Assert.That(info!.Value.Mss).IsGreaterThan(535u);
        }
        finally
        {
            client.Dispose();
            serverSide.Dispose();
            listener.Stop();
        }
    }

    [Test]
    public async Task GetTcpInfoV0_RcvWnd_IsNonZero_ForConnectedSocket()
    {
        var (listener, client, serverSide) = await CreateLoopbackConnectionAsync();
        try
        {
            TcpInfoV0? info = TcpInfoInterop.GetTcpInfoV0(client.Client.Handle);

            await Assert.That(info).IsNotNull();
            await Assert.That(info!.Value.RcvWnd).IsGreaterThan(0u);
        }
        finally
        {
            client.Dispose();
            serverSide.Dispose();
            listener.Stop();
        }
    }

    [Test]
    public async Task GetTcpInfoV0_SndWnd_IsNonZero_ForConnectedSocket()
    {
        var (listener, client, serverSide) = await CreateLoopbackConnectionAsync();
        try
        {
            TcpInfoV0? info = TcpInfoInterop.GetTcpInfoV0(client.Client.Handle);

            await Assert.That(info).IsNotNull();
            await Assert.That(info!.Value.SndWnd).IsGreaterThan(0u);
        }
        finally
        {
            client.Dispose();
            serverSide.Dispose();
            listener.Stop();
        }
    }

    [Test]
    public async Task GetTcpInfoV0_RcvBuf_IsNonZero_ForConnectedSocket()
    {
        var (listener, client, serverSide) = await CreateLoopbackConnectionAsync();
        try
        {
            TcpInfoV0? info = TcpInfoInterop.GetTcpInfoV0(client.Client.Handle);

            await Assert.That(info).IsNotNull();
            await Assert.That(info!.Value.RcvBuf).IsGreaterThan(0u);
        }
        finally
        {
            client.Dispose();
            serverSide.Dispose();
            listener.Stop();
        }
    }

    [Test]
    public async Task GetTcpInfoV0_BytesOut_IsNonZero_AfterDataSent()
    {
        var (listener, client, serverSide) = await CreateLoopbackConnectionAsync();
        try
        {
            byte[] payload = new byte[256];
            Random.Shared.NextBytes(payload);

            NetworkStream stream = client.GetStream();
            await stream.WriteAsync(payload);
            await stream.FlushAsync();

            // Give the OS time to account for the bytes.
            await Task.Delay(50);

            TcpInfoV0? info = TcpInfoInterop.GetTcpInfoV0(client.Client.Handle);

            await Assert.That(info).IsNotNull();
            await Assert.That(info!.Value.BytesOut).IsGreaterThan(0u);
        }
        finally
        {
            client.Dispose();
            serverSide.Dispose();
            listener.Stop();
        }
    }

    [Test]
    public async Task GetTcpInfoV0_BytesIn_IsNonZero_AfterDataReceived()
    {
        var (listener, client, serverSide) = await CreateLoopbackConnectionAsync();
        try
        {
            byte[] payload = new byte[256];
            Random.Shared.NextBytes(payload);

            // Server sends → client receives.
            NetworkStream serverStream = serverSide.GetStream();
            await serverStream.WriteAsync(payload);
            await serverStream.FlushAsync();

            // Drain the data on the client side.
            byte[] buf = new byte[payload.Length];
            NetworkStream clientStream = client.GetStream();
            int total = 0;
            while (total < buf.Length)
            {
                total += await clientStream.ReadAsync(buf.AsMemory(total));
            }

            TcpInfoV0? info = TcpInfoInterop.GetTcpInfoV0(client.Client.Handle);

            await Assert.That(info).IsNotNull();
            await Assert.That(info!.Value.BytesIn).IsGreaterThan(0u);
        }
        finally
        {
            client.Dispose();
            serverSide.Dispose();
            listener.Stop();
        }
    }

    [Test]
    public async Task TryGetTcpInfoV0_CalledTwice_BothSucceed_WithEstablishedState()
    {
        var (listener, client, serverSide) = await CreateLoopbackConnectionAsync();
        try
        {
            nint handle = client.Client.Handle;

            bool r1 = TcpInfoInterop.TryGetTcpInfoV0(handle, out TcpInfoV0 info1);
            bool r2 = TcpInfoInterop.TryGetTcpInfoV0(handle, out TcpInfoV0 info2);

            await Assert.That(r1).IsTrue();
            await Assert.That(r2).IsTrue();
            await Assert.That(info1.State).IsEqualTo(TcpStateEstab);
            await Assert.That(info2.State).IsEqualTo(TcpStateEstab);
        }
        finally
        {
            client.Dispose();
            serverSide.Dispose();
            listener.Stop();
        }
    }

    [Test]
    public async Task GetTcpInfoV0_AllFields_Accessible_ForConnectedSocket()
    {
        // Smoke-test: verify all 19 struct fields can be read without throwing.
        var (listener, client, serverSide) = await CreateLoopbackConnectionAsync();
        try
        {
            TcpInfoV0? nullable = TcpInfoInterop.GetTcpInfoV0(client.Client.Handle);
            await Assert.That(nullable).IsNotNull();

            TcpInfoV0 info = nullable!.Value;
            _ = info.State;
            _ = info.Mss;
            _ = info.ConnectionTimeMs;
            _ = info.TimestampsEnabled;
            _ = info.RttUs;
            _ = info.MinRttUs;
            _ = info.BytesInFlight;
            _ = info.Cwnd;
            _ = info.SndWnd;
            _ = info.RcvWnd;
            _ = info.RcvBuf;
            _ = info.BytesOut;
            _ = info.BytesIn;
            _ = info.BytesReordered;
            _ = info.BytesRetrans;
            _ = info.FastRetrans;
            _ = info.DupAcksIn;
            _ = info.TimeoutEpisodes;
            _ = info.SynRetrans;

            // If we reached here every field was accessible without throwing.
            await Assert.That(info.State).IsEqualTo(TcpStateEstab);
        }
        finally
        {
            client.Dispose();
            serverSide.Dispose();
            listener.Stop();
        }
    }

    [Test]
    public async Task GetTcpInfoV0_MinRttUs_IsNotGreaterThan_RttUs_ForConnectedSocket()
    {
        var (listener, client, serverSide) = await CreateLoopbackConnectionAsync();
        try
        {
            TcpInfoV0? info = TcpInfoInterop.GetTcpInfoV0(client.Client.Handle);

            await Assert.That(info).IsNotNull();
            // MinRttUs tracks the lifetime minimum; it must be ≤ the current RTT.
            bool minLeRtt = info!.Value.MinRttUs <= info.Value.RttUs;
            await Assert.That(minLeRtt).IsTrue();
        }
        finally
        {
            client.Dispose();
            serverSide.Dispose();
            listener.Stop();
        }
    }
}
