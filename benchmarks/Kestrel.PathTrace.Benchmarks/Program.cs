using System.Diagnostics;
using System.Runtime.CompilerServices;

using Kestrel.PathTrace.Abstractions;

namespace Kestrel.PathTrace.Benchmarks;

/// <summary>
/// Micro-benchmarks using a hand-rolled harness (no BenchmarkDotNet).
///
/// Run with:
///   dotnet run -c Release --project benchmarks/Kestrel.PathTrace.Benchmarks
/// </summary>
internal static class Program
{
    private const int WarmupIterations = 10_000;
    private const int BenchIterations  = 1_000_000;

    private static void Main(string[] args)
    {
        Console.WriteLine("Kestrel.PathTrace Micro-Benchmarks");
        Console.WriteLine(new string('=', 50));
        Console.WriteLine();

        RunBenchmark("HardwareTimestamp construction",   BenchHardwareTimestampConstruction);
        RunBenchmark("PacketTimestamps.HasAny (valid)",  BenchPacketTimestampsHasAnyValid);
        RunBenchmark("PacketTimestamps.HasAny (invalid)", BenchPacketTimestampsHasAnyInvalid);
        RunBenchmark("ClockCalibration.PhcToMonotonic",  BenchPhcToMonotonic);
        RunBenchmark("RequestPathTelemetry latency fields", BenchDerivedLatencies);
        RunBenchmark("TelemetryDispatcher.Record (no sinks)", BenchDispatcherNoSinks);
        RunBenchmark("TelemetryDispatcher.Record (1 sink)",   BenchDispatcherOneSink);

        if (OperatingSystem.IsLinux())
        {
            RunBenchmark("HwtstampInterop.SampleClocks [Linux]", BenchSampleClocks);
        }
    }

    // -----------------------------------------------------------------------
    // Benchmark runner
    // -----------------------------------------------------------------------

    private static void RunBenchmark(string name, Action<int> bench)
    {
        // Warm up
        bench(WarmupIterations);
        GC.Collect(2, GCCollectionMode.Forced, true);
        GC.WaitForPendingFinalizers();

        Stopwatch sw = Stopwatch.StartNew();
        bench(BenchIterations);
        sw.Stop();

        double nsPerOp = (double)sw.Elapsed.TotalNanoseconds / BenchIterations;
        Console.WriteLine($"  {name,-50}  {nsPerOp,8:F1} ns/op");
    }

    // -----------------------------------------------------------------------
    // Individual benchmarks
    // -----------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void BenchHardwareTimestampConstruction(int n)
    {
        HardwareTimestamp ts = default;

        for (int i = 0; i < n; i++)
        {
            ts = new HardwareTimestamp
            {
                Seconds     = i,
                Nanoseconds = i * 100L,
                IsValid     = true,
            };
        }

        _ = ts;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void BenchPacketTimestampsHasAnyValid(int n)
    {
        PacketTimestamps ts = new()
        {
            HardwareRaw = new HardwareTimestamp { IsValid = true, Seconds = 1 },
        };

        bool result = false;

        for (int i = 0; i < n; i++)
        {
            result = ts.HasAny;
        }

        _ = result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void BenchPacketTimestampsHasAnyInvalid(int n)
    {
        PacketTimestamps ts = default;
        bool result = false;

        for (int i = 0; i < n; i++)
        {
            result = ts.HasAny;
        }

        _ = result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void BenchPhcToMonotonic(int n)
    {
        ClockCalibration cal = new()
        {
            MonotonicNs = 1_000_000_000L,
            TaiNs       = 963_000_000L,
        };

        long result = 0;

        for (int i = 0; i < n; i++)
        {
            result = cal.PhcToMonotonic(i * 1_000L);
        }

        _ = result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void BenchDerivedLatencies(int n)
    {
        long tick = Stopwatch.GetTimestamp();

        RequestPathTelemetry t = new()
        {
            T2_TransportRead        = tick,
            T3_HttpParseStart       = tick + 100,
            T4_HttpHeadersComplete  = tick + 200,
            T5_MiddlewareStart      = tick + 200,
            T6_EndpointStart        = tick + 300,
            T7_EndpointEnd          = tick + 1300,
            T8_ResponseWriteStart   = tick + 1300,
            T9_ResponseWriteEnd     = tick + 1400,
            T10_TransportWriteStart = tick + 1500,
        };

        double result = 0;

        for (int i = 0; i < n; i++)
        {
            result = t.EndpointLatencyUs + t.WritebackLatencyUs;
        }

        _ = result;
    }

    private sealed class NullSink : IRequestPathTelemetrySink
    {
        public static readonly NullSink Instance = new();

        public void Record(Microsoft.AspNetCore.Http.HttpContext context, RequestPathTelemetry telemetry)
        {
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void BenchDispatcherNoSinks(int n)
    {
        TelemetryDispatcher dispatcher = new();
        Microsoft.AspNetCore.Http.HttpContext ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        RequestPathTelemetry t = new() { Method = "GET", StatusCode = 200 };

        for (int i = 0; i < n; i++)
        {
            dispatcher.Record(ctx, t);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void BenchDispatcherOneSink(int n)
    {
        TelemetryDispatcher dispatcher = new(NullSink.Instance);
        Microsoft.AspNetCore.Http.HttpContext ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        RequestPathTelemetry t = new() { Method = "GET", StatusCode = 200 };

        for (int i = 0; i < n; i++)
        {
            dispatcher.Record(ctx, t);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void BenchSampleClocks(int n)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        for (int i = 0; i < n; i++)
        {
            _ = Native.Linux.HwtstampInterop.SampleClocks();
        }
    }
}
