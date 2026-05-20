using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;

using Kestrel.PathTrace;
using Kestrel.PathTrace.Abstractions;
using Kestrel.PathTrace.Native.Linux;
using Kestrel.PathTrace.Transport;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kestrel.PathTrace.Benchmarks;

/// <summary>
/// End-to-end HTTP throughput benchmark comparing raw Kestrel against Kestrel
/// with PathTrace instrumentation at various sample rates.
///
/// Run:
///   dotnet run -c Release --project benchmarks/Kestrel.PathTrace.Benchmarks -- --bandwidth
///   dotnet run -c Release --project benchmarks/Kestrel.PathTrace.Benchmarks -- --bandwidth \
///       --sample-rate 10 --concurrency 16 --duration 15 --response-size 4096
///
/// Hardware timestamps (Linux only) require a physical NIC — loopback (127.0.0.1)
/// never has HW timestamp support. Bind to your NIC's address instead:
///   dotnet run -c Release --project benchmarks/Kestrel.PathTrace.Benchmarks -- --bandwidth \
///       --bind 192.168.1.100   # server + client both use this address
/// </summary>
internal static class BandwidthBenchmark
{
    internal enum BenchMode { None, PathTrace }

    internal sealed class Options
    {
        public int    SampleRate        { get; init; } = 1;
        public int    DurationSeconds   { get; init; } = 10;
        public int    WarmupSeconds     { get; init; } = 3;
        public int    Concurrency       { get; init; } = 8;
        public int    ResponseSizeBytes { get; init; } = 1_024;
        /// <summary>
        /// IP address to listen/connect on. Defaults to 127.0.0.1 (loopback).
        /// Hardware timestamps require a physical NIC: set this to the NIC's IP.
        /// </summary>
        public string BindAddress       { get; init; } = "127.0.0.1";
    }

    // Per-worker mutable accumulator (not shared across threads).
    private sealed class WorkerResult
    {
        public long       Requests  { get; set; }
        public long       Bytes     { get; set; }
        public List<long> Latencies { get; } = new(capacity: 16_384);
    }

    // ── Entry point ──────────────────────────────────────────────────────────

    internal static async Task RunAsync(Options options, CancellationToken ct = default)
    {
        Console.WriteLine("Kestrel.PathTrace Bandwidth Benchmark");
        Console.WriteLine(new string('\u2500', 70));
        Console.WriteLine($"  Measurement  : {options.DurationSeconds}s  (+{options.WarmupSeconds}s warmup)");
        Console.WriteLine($"  Concurrency  : {options.Concurrency} workers");
        Console.WriteLine($"  Response     : {options.ResponseSizeBytes:N0} bytes/request");
        Console.WriteLine($"  Sample rate  : 1/{options.SampleRate}  ({100.0 / options.SampleRate:F1}% of requests)");
        Console.WriteLine($"  Bind address : {options.BindAddress}");
        Console.WriteLine("  Ctrl+C       : aborts and prints partial results");

        bool isLoopback = options.BindAddress is "127.0.0.1" or "::1" or "localhost";
        if (OperatingSystem.IsLinux() && isLoopback)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  NOTE: HW timestamps need a physical NIC.");
            Console.WriteLine("        Use --bind <nic-ip> so traffic flows over the NIC.");
            Console.ResetColor();
        }
        else if (OperatingSystem.IsLinux() && !isLoopback)
        {
            ReportNicCapabilities(options.BindAddress);
        }
        Console.WriteLine();

        BenchMode[] modes = BuildModeList();

        // Collect (mode, requests, bytes, elapsed, latencies[]) per mode.
        var rows = new List<(BenchMode Mode, long Req, long Bytes, double Elapsed, long[] Lats)>(modes.Length);

        foreach (BenchMode mode in modes)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }
            Console.WriteLine($"  [{ModeLabel(mode),-24}] running...");
            rows.Add(await RunMode(mode, options, ct));
        }

        Console.WriteLine();
        if (rows.Count > 0)
        {
            PrintTable(rows);
        }
        if (ct.IsCancellationRequested)
        {
            Console.WriteLine("  (benchmark aborted early)");
        }
    }

    // ── Modes ────────────────────────────────────────────────────────────────

    private static BenchMode[] BuildModeList() => [BenchMode.None, BenchMode.PathTrace];

    private static string ModeLabel(BenchMode mode) => mode switch
    {
        BenchMode.None      => "None (baseline)",
        BenchMode.PathTrace => "PathTrace",
        _                   => mode.ToString(),
    };

    // ── NIC capability probe (Linux + non-loopback only) ─────────────────────

    [SupportedOSPlatform("linux")]
    private static void ReportNicCapabilities(string bindAddress)
    {
        try
        {
            if (!IPAddress.TryParse(bindAddress, out IPAddress? bindIp))
            {
                return;
            }

            using var sock = new Socket(bindIp.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            sock.Bind(new IPEndPoint(bindIp, 0));

            var provider = new LinuxHardwareTimestampProvider();
            NicTimestampCapabilities? caps = provider.QueryCapabilities(sock.SafeHandle.DangerousGetHandle());
            if (caps == null)
            {
                Console.WriteLine("  NIC: capability query returned no data");
                return;
            }

            Console.Write($"  NIC {caps.InterfaceName}: ");
            if (caps.IsFullHardwareTimestampingAvailable)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("full HW timestamps supported (RX + PHC)");
            }
            else if (caps.HardwareRxAvailable)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write("HW RX timestamps available (no PHC — limited accuracy)");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write("HW timestamps NOT supported — will fall back to software");
            }
            Console.ResetColor();
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  NIC probe failed: {ex.Message}");
        }
    }

    // ── Server lifecycle ─────────────────────────────────────────────────────

    private static async Task<(WebApplication App, string BaseUrl)> StartServer(
        BenchMode mode, Options options, byte[] payload)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.None);

        // Port 0 → OS picks a free port; resolved after StartAsync.
        IPAddress bindIp = IPAddress.TryParse(options.BindAddress, out IPAddress? parsed)
            ? parsed
            : IPAddress.Loopback;
        builder.Services.Configure<KestrelServerOptions>(
            k => k.Listen(bindIp, 0));

        if (mode != BenchMode.None)
        {
            builder.Services.AddKestrelPathTrace(o =>
            {
                o.SampleRate = options.SampleRate;
                o.Transport  = new TransportInstrumentationOptions
                {
                    // EnableHardwareTimestamping defaults to true: the library automatically
                    // uses HW timestamps when the NIC supports them, falling back to SW.
                    EnableTxHardwareTimestamping = false,
                    EnableWindowsTcpInfo         = false,
                };
            });
            builder.Services.AddKestrelPathTraceSink<BenchNullSink>();
        }

        var app = builder.Build();

        if (mode != BenchMode.None)
        {
            app.UseKestrelPathTrace();
        }

        // Pre-computed response — same memory for all requests, zero allocation per call.
        var payloadMemory = new ReadOnlyMemory<byte>(payload);
        app.MapGet("/bench", async (HttpContext ctx) =>
        {
            ctx.Response.ContentType   = "application/octet-stream";
            ctx.Response.ContentLength = payloadMemory.Length;
            await ctx.Response.Body.WriteAsync(payloadMemory);
        });

        await app.StartAsync();

        string address = app.Services
            .GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses
            .First();

        return (app, address);
    }

    // ── Measurement ──────────────────────────────────────────────────────────

    private static async Task<(BenchMode, long, long, double, long[])> RunMode(
        BenchMode mode, Options options, CancellationToken ct)
    {
        byte[] payload = new byte[options.ResponseSizeBytes];
        for (int i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i & 0xFF);
        }

        var (app, baseUrl) = await StartServer(mode, options, payload);
        try
        {
            string url     = $"{baseUrl.TrimEnd('/')}/bench";
            HttpClient[] clients = CreateClients(options.Concurrency, baseUrl);
            try
            {
                // Warmup — discard results; also cancels early on Ctrl+C
                using var warmupTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(options.WarmupSeconds));
                using var warmupCts        = CancellationTokenSource.CreateLinkedTokenSource(ct, warmupTimeoutCts.Token);
                await FireWorkers(clients, url, options.ResponseSizeBytes, warmupCts.Token);

                // If Ctrl+C fired during warmup, return a null result (skipped in the table).
                if (ct.IsCancellationRequested)
                {
                    return (mode, 0, 0, 0.0, []);
                }

                GC.Collect(2, GCCollectionMode.Forced, blocking: true);
                GC.WaitForPendingFinalizers();

                // Measurement — Ctrl+C cuts the run short but still reports what was measured.
                var sw = Stopwatch.StartNew();
                using var measureTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(options.DurationSeconds));
                using var measureCts        = CancellationTokenSource.CreateLinkedTokenSource(ct, measureTimeoutCts.Token);
                WorkerResult[] results      = await FireWorkers(clients, url, options.ResponseSizeBytes, measureCts.Token);
                sw.Stop();

                long   totalReq   = results.Sum(r => r.Requests);
                long   totalBytes = results.Sum(r => r.Bytes);
                long[] allLats    = [.. results.SelectMany(r => r.Latencies)];

                return (mode, totalReq, totalBytes, sw.Elapsed.TotalSeconds, allLats);
            }
            finally
            {
                foreach (HttpClient c in clients)
                {
                    c.Dispose();
                }
            }
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static HttpClient[] CreateClients(int count, string baseUrl)
    {
        var uri     = new Uri(baseUrl);
        var clients = new HttpClient[count];
        for (int i = 0; i < count; i++)
        {
            clients[i] = new HttpClient(
                new SocketsHttpHandler
                {
                    MaxConnectionsPerServer = 1,
                    UseProxy                = false,
                    AllowAutoRedirect       = false,
                })
            { BaseAddress = uri };
        }
        return clients;
    }

    private static async Task<WorkerResult[]> FireWorkers(
        HttpClient[] clients, string url, int responseSize, CancellationToken ct)
    {
        Task<WorkerResult>[] tasks = clients
            .Select(c => WorkerLoop(c, url, responseSize, ct))
            .ToArray();
        return await Task.WhenAll(tasks);
    }

    private static async Task<WorkerResult> WorkerLoop(
        HttpClient client, string url, int responseSize, CancellationToken ct)
    {
        var result = new WorkerResult();

        while (!ct.IsCancellationRequested)
        {
            long t0 = Stopwatch.GetTimestamp();
            try
            {
                using HttpResponseMessage resp = await client.GetAsync(
                    url, HttpCompletionOption.ResponseContentRead, ct);

                long ns = (long)((Stopwatch.GetTimestamp() - t0)
                                 * (1_000_000_000.0 / Stopwatch.Frequency));
                result.Requests++;
                result.Bytes += resp.Content.Headers.ContentLength ?? responseSize;
                result.Latencies.Add(ns);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (HttpRequestException) { /* transient — keep running */ }
        }

        return result;
    }

    // ── Results display ──────────────────────────────────────────────────────

    private static void PrintTable(
        List<(BenchMode Mode, long Req, long Bytes, double Elapsed, long[] Lats)> rows)
    {
        const string header = "  Mode                      RPS        MB/s   Mean(ms)   p99(ms)   Overhead";
        Console.WriteLine(header);
        Console.WriteLine("  " + new string('─', header.Length - 2));

        double? baselineRps = null;

        foreach (var (mode, req, bytes, elapsed, lats) in rows)
        {
            // elapsed == 0 means the run was cancelled during warmup — skip the row.
            if (elapsed <= 0)
            {
                continue;
            }

            double rps  = req   / elapsed;
            double mbps = bytes / elapsed / (1024.0 * 1024.0);

            Array.Sort(lats);
            double meanMs = lats.Length > 0
                ? (double)lats.Sum() / lats.Length / 1_000_000.0
                : 0;
            double p99Ms = lats.Length > 0
                ? lats[(int)(lats.Length * 0.99)] / 1_000_000.0
                : 0;

            string overheadStr;
            if (mode == BenchMode.None)
            {
                baselineRps  = rps;
                overheadStr  = "—";
            }
            else
            {
                double pct  = (rps / baselineRps!.Value - 1.0) * 100.0;
                overheadStr = pct >= 0 ? $"+{pct:F1}%" : $"{pct:F1}%";
            }

            Console.WriteLine(
                $"  {ModeLabel(mode),-26}  {rps,8:N0}  {mbps,6:F1}  {meanMs,9:F3}  {p99Ms,8:F3}  {overheadStr,9}");
        }

        Console.WriteLine();
    }

    // ── Null sink (measures instrumentation overhead, not sink overhead) ──────

    private sealed class BenchNullSink : IRequestPathTelemetrySink
    {
        public void Record(HttpContext context, RequestPathTelemetry telemetry) { }
    }
}
