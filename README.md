# Kestrel.PathTrace

Sub-millisecond request-path telemetry for ASP.NET Core / Kestrel.

Instruments every layer of the Kestrel request pipeline — NIC hardware
timestamping, transport, HTTP parsing, middleware, endpoint, and response
writeback — and publishes the resulting latency breakdowns to Prometheus and/or
OpenTelemetry.

[![Build](https://github.com/zcsizmadia/Kestrel.PathTrace/actions/workflows/build.yml/badge.svg)](https://github.com/zcsizmadia/Kestrel.PathTrace/actions/workflows/build.yml)

---

## Table of contents

- [How it works](#how-it-works)
- [Quick start](#quick-start)
  - [1. Add NuGet packages](#1-add-nuget-packages)
  - [2. Register services](#2-register-services)
  - [3. Add middleware](#3-add-middleware)
  - [4. Expose metrics endpoint](#4-expose-metrics-endpoint)
  - [5. Build the native shim (Linux / Windows)](#5-build-the-native-shim-linux--windows)
- [Packages](#packages)
- [API reference](#api-reference)
  - [Service registration](#service-registration)
  - [PathTraceOptions](#pathtraceoptions)
  - [TransportInstrumentationOptions](#transportinstrumentationoptions)
  - [RequestPathTelemetry](#requestpathtelemetry)
  - [HardwareTimestamp](#hardwaretimestamp)
  - [PacketTimestamps](#packettimestamps)
  - [ClockCalibration](#clockcalibration)
  - [NicTimestampCapabilities](#nictimestampcapabilities)
  - [ConnectionTelemetryState](#connectiontelemetrystate)
  - [IHardwareTimestampProvider](#ihardwaretimestampprovider)
  - [IRequestPathTelemetrySink](#irequestpathtelemetrysink)
  - [Prometheus sink](#prometheus-sink)
  - [OpenTelemetry sink](#opentelemetry-sink)
  - [Custom sinks](#custom-sinks)
  - [PathTraceDefaults](#pathtracedefaults)
- [Prometheus metrics reference](#prometheus-metrics-reference)
- [OpenTelemetry spans reference](#opentelemetry-spans-reference)
- [Platform support and native shims](#platform-support-and-native-shims)
- [Code coverage](#code-coverage)
- [License](#license)

---

## How it works

```
NIC hardware clock (PHC / SO_TIMESTAMPING)   ← Linux only, optional
         │
         ▼
[T2] Transport read       ← InstrumentedTransportFactory wraps SocketTransportFactory
         │
         ▼
[T3] HTTP parse start     ┐
[T4] HTTP headers done    ┘ Kestrel internal hooks
         │
         ▼
[T5] Middleware start     ┐
[T6] Endpoint start       │ RequestPathTelemetryMiddleware
[T7] Endpoint end         ┘
         │
         ▼
[T8] Response write start ┐
[T9] Response write end   ┘ Kestrel internal hooks
[T10] Transport flush
         │
         ▼
      IRequestPathTelemetrySink
      ├─ PrometheusSink   → /metrics
      └─ OpenTelemetrySink → OTLP
```

All `T*` values are `Stopwatch.GetTimestamp()` ticks.  On Linux, NIC hardware
timestamps use the PHC clock and are correlated to `Stopwatch` via
`ClockCalibration` (sampled once per connection at accept time).

---

## Quick start

### 1. Add NuGet packages

```xml
<!-- Core (always required) -->
<PackageReference Include="Kestrel.PathTrace" Version="1.0.0" />

<!-- Pick one or both export sinks -->
<PackageReference Include="Kestrel.PathTrace.Export.Prometheus"     Version="1.0.0" />
<PackageReference Include="Kestrel.PathTrace.Export.OpenTelemetry" Version="1.0.0" />
```

### 2. Register services

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddKestrelPathTrace(options =>
    {
        options.Transport = new TransportInstrumentationOptions
        {
            EnableHardwareTimestamping = true,   // SO_TIMESTAMPING, Linux only
            EnableTxHardwareTimestamping = false, // TX loop measurement (higher overhead)
            EnableWindowsTcpInfo = true,          // TCP_INFO via tcpinfo_shim.dll, Windows only
        };
    })
    .AddKestrelPathTracePrometheus()      // optional
    .AddKestrelPathTraceOpenTelemetry();  // optional
```

### 3. Add middleware

```csharp
var app = builder.Build();

// Must be placed before UseRouting() so that T5/T6/T7 are captured correctly.
app.UseKestrelPathTrace();

app.UseRouting();
app.MapControllers();
// ...
app.Run();
```

### 4. Expose metrics endpoint

Prometheus (requires `prometheus-net.AspNetCore`):

```csharp
app.MapMetrics(); // exposes /metrics
```

OpenTelemetry: configure your OTLP exporter as usual — `OpenTelemetrySink`
writes to the `Kestrel.PathTrace` `ActivitySource`.

### 5. Build the native shim (Linux / Windows)

The NuGet package bundles pre-built native shims for all supported RIDs.  For
local development you need to build the shim from source once before running
`dotnet build`.

**Prerequisites:** `cmake`, a C compiler (`gcc` / `clang` / MSVC / MinGW).

```bash
# From the repository root (or your project's native/ folder)
./native/scripts/build-and-install.sh          # Release (default)
./native/scripts/build-and-install.sh Debug    # Debug build
```

The script auto-detects the OS and architecture, builds the appropriate shim
(`libhwtstamp_shim.so` on Linux, `tcpinfo_shim.dll` on Windows), and copies it
to `runtimes/<RID>/native/`.

> **VS Developer Command Prompt**: run the script from Git Bash inside the VS
> Developer Command Prompt. The script reads `VSINSTALLDIR` and
> `VSCMD_ARG_TGT_ARCH` automatically and selects Ninja (if available) or passes
> `-A x64`/`-A ARM64` to the Visual Studio CMake generator.

---

## Packages

| Package | Description |
|---|---|
| `Kestrel.PathTrace.Abstractions` | Interfaces and data types only; no ASP.NET Core dependency |
| `Kestrel.PathTrace` | Core: transport instrumentation, middleware, DI registration |
| `Kestrel.PathTrace.Export.Prometheus` | Prometheus histograms via `prometheus-net` |
| `Kestrel.PathTrace.Export.OpenTelemetry` | OpenTelemetry spans via `OpenTelemetry.Api` |

---

## API reference

### Service registration

All public registration methods live in the respective package namespaces.

#### `AddKestrelPathTrace`

```csharp
IServiceCollection AddKestrelPathTrace(
    this IServiceCollection services,
    Action<PathTraceOptions>? configure = null)
```

Registers:
- Platform-specific `IHardwareTimestampProvider` (`LinuxHardwareTimestampProvider`
  on Linux, `NullHardwareTimestampProvider` elsewhere).
- `InstrumentedTransportFactory` wrapping Kestrel's `SocketTransportFactory`.
- `TelemetryDispatcher` as the singleton `IRequestPathTelemetrySink`; it fans
  out to every sink registered under the
  [`PathTraceDefaults.SinkKey`](#pathtracedefaults) keyed-service key.

#### `UseKestrelPathTrace`

```csharp
IApplicationBuilder UseKestrelPathTrace(this IApplicationBuilder app)
```

Inserts `RequestPathTelemetryMiddleware` into the pipeline.  Must be placed
**before** `UseRouting()`.

#### `AddKestrelPathTraceSink<T>`

```csharp
IServiceCollection AddKestrelPathTraceSink<T>(this IServiceCollection services)
    where T : class, IRequestPathTelemetrySink
```

Registers a custom `IRequestPathTelemetrySink` implementation so that
`TelemetryDispatcher` picks it up.  Use this instead of implementing the keyed
registration manually.

#### `AddKestrelPathTracePrometheus`

```csharp
IServiceCollection AddKestrelPathTracePrometheus(
    this IServiceCollection services,
    IMetricFactory? metricFactory = null)
```

Registers `PrometheusSink`.  Pass a custom `IMetricFactory` to write to an
isolated registry (useful in tests: `Metrics.WithCustomRegistry(Metrics.NewCustomRegistry())`).

#### `AddKestrelPathTraceOpenTelemetry`

```csharp
IServiceCollection AddKestrelPathTraceOpenTelemetry(this IServiceCollection services)
```

Registers `OpenTelemetrySink`.  Spans are emitted on the `Kestrel.PathTrace`
`ActivitySource`; add it to your `TracerProvider`:

```csharp
services.AddOpenTelemetry()
    .WithTracing(b => b
        .AddSource("Kestrel.PathTrace")
        .AddOtlpExporter());
```

---

### PathTraceOptions

```csharp
public sealed class PathTraceOptions
{
    public TransportInstrumentationOptions? Transport { get; set; }
}
```

Top-level configuration object passed to `AddKestrelPathTrace`.

---

### TransportInstrumentationOptions

```csharp
public sealed class TransportInstrumentationOptions
{
    public bool EnableHardwareTimestamping    { get; set; } = true;
    public bool EnableTxHardwareTimestamping  { get; set; } = false;
    public bool EnableWindowsTcpInfo          { get; set; } = true;
}
```

| Property | Default | Description |
|---|---|---|
| `EnableHardwareTimestamping` | `true` | Enable `SO_TIMESTAMPING` on each accepted socket (Linux). Falls back to software timestamps if the NIC does not support hardware timestamps. No-op on Windows. |
| `EnableTxHardwareTimestamping` | `false` | Also enable TX hardware timestamps (`SOF_TIMESTAMPING_TX_HARDWARE`). Requires polling the socket error queue after every send; has measurable overhead. |
| `EnableWindowsTcpInfo` | `true` | Query `TCP_INFO` via `tcpinfo_shim.dll` on Windows and attach the result to the telemetry record. No-op on Linux. |

---

### RequestPathTelemetry

The central data object — one instance per HTTP request.

```csharp
public sealed class RequestPathTelemetry
```

#### Timestamp fields

All `T*` fields are `Stopwatch.GetTimestamp()` values (ticks, not nanoseconds).
Convert with `Stopwatch.Frequency`.

| Field | Description |
|---|---|
| `NicRxHardware` | Raw NIC PHC timestamp of the first received segment (Linux hardware path only). |
| `NicRxSoftware` | Kernel software RX timestamp (`CLOCK_REALTIME` ns). |
| `ClockCalibration` | Multi-clock snapshot taken at connection-accept time; used to convert `NicRxHardware` to Stopwatch-relative time. |
| `T2_TransportRead` | First byte arrived from the OS transport. |
| `T3_HttpParseStart` | HTTP parser started. |
| `T4_HttpHeadersComplete` | All HTTP headers parsed. |
| `T5_MiddlewareStart` | First middleware invoked. |
| `T6_EndpointStart` | Endpoint handler invoked. |
| `T7_EndpointEnd` | Endpoint handler returned. |
| `T8_ResponseWriteStart` | First response byte written to the pipe. |
| `T9_ResponseWriteEnd` | Last response byte written to the pipe. |
| `T10_TransportWriteStart` | First response byte flushed to the OS transport. |

#### HTTP metadata

| Property | Description |
|---|---|
| `Method` | HTTP method (`GET`, `POST`, …). |
| `Route` | Matched route template (`/api/orders/{id}`). |
| `StatusCode` | HTTP response status code. |

#### Derived latencies (computed properties, µs unless noted)

| Property | Formula | Unit |
|---|---|---|
| `TransportLatencyUs` | `T3 − T2` | µs |
| `HttpParseLatencyUs` | `T4 − T3` | µs |
| `MiddlewareLatencyUs` | `T6 − T5` | µs |
| `EndpointLatencyUs` | `T7 − T6` | µs |
| `SerializationLatencyUs` | `T9 − T8` | µs |
| `WritebackLatencyUs` | `T10 − T9` | µs |
| `NicToTransportNs` | NIC PHC → `T2` (requires hardware timestamping + calibration) | ns, nullable |

---

### HardwareTimestamp

```csharp
public readonly record struct HardwareTimestamp
{
    public long Seconds          { get; init; }
    public long Nanoseconds      { get; init; }   // [0, 999_999_999]
    public bool IsValid          { get; init; }
    public long TotalNanoseconds { get; }          // Seconds * 1_000_000_000 + Nanoseconds
    public static HardwareTimestamp Invalid => default;
}
```

A single hardware or kernel-software timestamp in nanoseconds.  Always check
`IsValid` before using the value; an unset timestamp returns `Invalid`.

---

### PacketTimestamps

```csharp
public readonly record struct PacketTimestamps
{
    public HardwareTimestamp Software       { get; init; }  // CLOCK_REALTIME, kernel RX path
    public HardwareTimestamp HardwareLegacy { get; init; }  // Deprecated; usually zero
    public HardwareTimestamp HardwareRaw    { get; init; }  // Raw NIC/PHC clock
    public bool HasAny { get; }                             // true if any field IsValid
}
```

Three-tuple of timestamps attached to a single packet event, as returned by
`recvmsg` with `SO_TIMESTAMPING`.

---

### ClockCalibration

```csharp
public readonly record struct ClockCalibration
{
    public long MonotonicNs    { get; init; }  // CLOCK_MONOTONIC (= Stopwatch on Linux)
    public long RealtimeNs     { get; init; }  // CLOCK_REALTIME (Unix epoch)
    public long TaiNs          { get; init; }  // CLOCK_TAI (UTC + leap-seconds)
    public long RawMonotonicNs { get; init; }  // CLOCK_MONOTONIC_RAW (no NTP adjustment)

    // Convert a raw PHC nanosecond value to an approximate CLOCK_MONOTONIC value.
    // Accurate when the NIC PHC is PTP-synchronized to TAI.
    public long PhcToMonotonic(long phcNs);
}
```

Snapshot of multiple Linux clocks sampled simultaneously at connection-accept
time.  Used by `RequestPathTelemetry.NicToTransportNs` to convert the NIC's
PHC clock into a value comparable to `Stopwatch`.

---

### NicTimestampCapabilities

```csharp
public sealed class NicTimestampCapabilities
{
    public string InterfaceName              { get; init; }   // e.g. "eth0"
    public uint   SoTimestampingFlags        { get; init; }   // raw SO_TIMESTAMPING bitmask
    public int    PhcIndex                   { get; init; }   // /dev/ptp<N>; -1 = none
    public uint   TxTypes                    { get; init; }   // hwtstamp_tx_types bitmask
    public uint   RxFilters                  { get; init; }   // hwtstamp_rx_filters bitmask
    public bool   HardwareRxAvailable        { get; init; }
    public bool   HardwareTxAvailable        { get; init; }
    public bool   SoftwareRxAvailable        { get; init; }
    public bool   SoftwareTxAvailable        { get; init; }
    public bool   RawHardwareAvailable       { get; init; }   // PHC (not converted to wall-clock)
    public bool   IsFullHardwareTimestampingAvailable { get; } // HwRx && RawHw && PhcIndex >= 0
}
```

NIC timestamping capabilities resolved once per connection via
`ETHTOOL_GET_TS_INFO`.  Accessible through `ConnectionTelemetryState.NicCapabilities`.

---

### ConnectionTelemetryState

```csharp
public sealed class ConnectionTelemetryState
{
    public nint                    SocketHandle        { get; set; }
    public NicTimestampCapabilities? NicCapabilities   { get; set; }
    public ClockCalibration?       ClockCalibration    { get; set; }
    public string                  InterfaceName       { get; set; }
    public PacketTimestamps?       LastRxTimestamp     { get; set; }
    public long                    T0_ConnectionAccepted { get; set; }
}
```

Per-connection state object stored in Kestrel's `IFeatureCollection`.  You can
read it from middleware via:

```csharp
var state = context.Features.Get<ConnectionTelemetryState>();
if (state?.NicCapabilities?.IsFullHardwareTimestampingAvailable == true)
{
    // hardware timestamping is active on this connection
}
```

---

### IHardwareTimestampProvider

```csharp
public interface IHardwareTimestampProvider
{
    NicTimestampCapabilities? QueryCapabilities(nint socketHandle);
    bool EnableTimestamping(nint socketHandle, bool preferHardware = true);
    ClockCalibration SampleClocks();
}
```

Abstracts platform-specific NIC timestamp operations.

| Method | Description |
|---|---|
| `QueryCapabilities` | Queries `ETHTOOL_GET_TS_INFO` for the NIC behind the given socket. Returns `null` when unavailable (e.g. Windows, virtual NIC). |
| `EnableTimestamping` | Sets `SO_TIMESTAMPING` socket options. Returns `false` if the operation fails. `preferHardware = true` tries hardware first, falls back to software. |
| `SampleClocks` | Samples `CLOCK_MONOTONIC`, `CLOCK_REALTIME`, `CLOCK_TAI`, and `CLOCK_MONOTONIC_RAW` in rapid succession for PHC ↔ Stopwatch calibration. |

The correct implementation is registered automatically by `AddKestrelPathTrace`:

| Platform | Implementation |
|---|---|
| Linux | `LinuxHardwareTimestampProvider` (P/Invoke → `libhwtstamp_shim.so`) |
| Windows / other | `NullHardwareTimestampProvider` (no-op) |

Replace with your own implementation via:

```csharp
services.AddSingleton<IHardwareTimestampProvider, MyProvider>();
services.AddKestrelPathTrace(); // registers after — TryAdd won't overwrite yours
```

---

### IRequestPathTelemetrySink

```csharp
public interface IRequestPathTelemetrySink
{
    void Record(HttpContext context, RequestPathTelemetry telemetry);
}
```

Called once at the end of every HTTP request by `RequestPathTelemetryMiddleware`
(via `TelemetryDispatcher`).  The call is synchronous and on the request thread;
keep implementations fast (queue / channel to a background writer if needed).

---

### Prometheus sink

`PrometheusSink` is registered by `AddKestrelPathTracePrometheus()`.

It writes to the default `prometheus-net` registry.  For test isolation, supply
a custom `IMetricFactory`:

```csharp
var registry = Metrics.NewCustomRegistry();
var factory  = Metrics.WithCustomRegistry(registry);
services.AddKestrelPathTracePrometheus(metricFactory: factory);
```

See [Prometheus metrics reference](#prometheus-metrics-reference) for the full
list of histograms.

---

### OpenTelemetry sink

`OpenTelemetrySink` is registered by `AddKestrelPathTraceOpenTelemetry()`.

It emits child spans on the `ActivitySource` named **`Kestrel.PathTrace`**.
You must add the source to your `TracerProvider`; otherwise all spans are
silently discarded (zero-allocation fast path).

```csharp
services.AddOpenTelemetry()
    .WithTracing(b => b
        .AddSource("Kestrel.PathTrace")
        .AddOtlpExporter(o => o.Endpoint = new Uri("http://localhost:4317")));
```

See [OpenTelemetry spans reference](#opentelemetry-spans-reference) for
details on span names and attributes.

---

### Custom sinks

Implement `IRequestPathTelemetrySink` and register it:

```csharp
public sealed class ConsoleSink : IRequestPathTelemetrySink
{
    public void Record(HttpContext context, RequestPathTelemetry t)
    {
        Console.WriteLine(
            $"{t.Method} {t.Route} → {t.StatusCode} | " +
            $"endpoint={t.EndpointLatencyUs:F1}µs  " +
            $"writeback={t.WritebackLatencyUs:F1}µs");
    }
}

// Registration
services.AddKestrelPathTrace()
        .AddKestrelPathTraceSink<ConsoleSink>();
```

Multiple sinks are all invoked; order is registration order.

---

### PathTraceDefaults

```csharp
public static class PathTraceDefaults
{
    public const string SinkKey = "kestrel-path-trace-sink";
}
```

Well-known DI keyed-service key shared between the core package and all export
packages.  `TelemetryDispatcher` collects every `IRequestPathTelemetrySink`
registered under this key via `IServiceProvider.GetKeyedServices<>`.  You
normally never need to reference this constant directly.

---

## Prometheus metrics reference

All histograms carry three labels: `route`, `method`, `status`.

| Metric | Unit | Description |
|---|---|---|
| `kestrel_transport_latency_us` | µs | Transport → HTTP parse handoff latency (`T3 − T2`) |
| `kestrel_http_parse_latency_us` | µs | HTTP header parsing latency (`T4 − T3`) |
| `kestrel_middleware_latency_us` | µs | Middleware dispatch latency (`T6 − T5`) |
| `kestrel_endpoint_latency_us` | µs | Endpoint execution latency (`T7 − T6`) |
| `kestrel_serialization_latency_us` | µs | Response serialization latency (`T9 − T8`) |
| `kestrel_writeback_latency_us` | µs | Transport writeback latency (`T10 − T9`) |
| `kestrel_nic_to_transport_ns` | ns | NIC HW RX → transport read latency. **Only present when hardware timestamping is active.** |

Bucket boundaries (µs histograms): `1, 5, 10, 25, 50, 100, 250, 500, 1000, 5000, 10000, 50000, 100000`

Bucket boundaries (ns histogram): `100, 500, 1000, 5000, 10000, 50000, 100000, 500000, 1000000`

---

## OpenTelemetry spans reference

Each request produces one root span and up to six child spans.

### Root span

| Attribute | Value |
|---|---|
| Name | `kestrel.request {METHOD} {route}` |
| Kind | `Server` |
| `http.method` | HTTP method |
| `http.route` | Matched route template |
| `http.status_code` | Response status code |
| `kestrel.nic_to_transport_ns` | NIC → transport latency in ns (only when HW timestamping active) |
| `nic.hw_rx_ns` | Raw NIC PHC timestamp in ns (only when HW timestamping active) |

### Child spans

| Span name | Covers | Tags |
|---|---|---|
| `kestrel.transport_to_parse` | `T2 → T3` | `http.method`, `http.route`, `http.status_code` |
| `kestrel.parse` | `T3 → T4` | same |
| `kestrel.middleware` | `T5 → T6` | same + `kestrel.endpoint_latency_us`, `kestrel.writeback_latency_us` |
| `kestrel.endpoint` | `T6 → T7` | same |
| `kestrel.serialization` | `T8 → T9` | same |
| `kestrel.writeback` | `T9 → T10` | same |

Spans with a zero or negative duration are suppressed.

---

## Platform support and native shims

| RID | Native shim | Capability |
|---|---|---|
| `linux-x64` | `libhwtstamp_shim.so` | Full hardware timestamping via `SO_TIMESTAMPING` / PHC |
| `linux-arm64` | `libhwtstamp_shim.so` | Same |
| `linux-musl-x64` | `libhwtstamp_shim.so` | Same (Alpine) |
| `linux-musl-arm64` | `libhwtstamp_shim.so` | Same (Alpine) |
| `win-x64` | `tcpinfo_shim.dll` | `TCP_INFO` per-request metrics; no hardware timestamps |
| `win-arm64` | `tcpinfo_shim.dll` | Same |

The shims are bundled in the `Kestrel.PathTrace` NuGet package under
`runtimes/<RID>/native/` and are loaded automatically by the .NET runtime.

### Requirements for Linux hardware timestamping

- Linux kernel ≥ 3.17 (`SO_TIMESTAMPING` with `SOF_TIMESTAMPING_RAW_HARDWARE`)
- NIC driver with `ETHTOOL_GET_TS_INFO` support
- For sub-µs accuracy: NIC synchronized to PTP/IEEE 1588 (`ptp4l`, `phc2sys`)
- The process must have `CAP_NET_ADMIN` **or** the NIC driver must allow
  `SO_TIMESTAMPING` without elevated privileges (driver-specific)

---

## Code coverage

Coverage measured on Windows (linux-specific paths skipped by platform guards):

| Assembly | Line | Block |
|---|---|---|
| `Kestrel.PathTrace.Abstractions` | 88.0% | 87.3% |
| `Kestrel.PathTrace.Export.OpenTelemetry` | 91.8% | 94.7% |
| `Kestrel.PathTrace.Export.Prometheus` | 82.0% | 84.9% |
| `Kestrel.PathTrace` | 5.3%\* | 5.3%\* |

\* The main assembly is dominated by Linux-native P/Invoke code (`hwtstamp_shim`)
which is guarded by `[SupportedOSPlatform("linux")]` and cannot execute on
Windows. Full coverage is only achievable in a Linux CI run.

---

## License

[MIT](LICENSE) © Zoltan Csizmadia
