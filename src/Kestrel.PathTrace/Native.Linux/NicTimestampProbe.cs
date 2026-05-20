using System.Collections.Concurrent;
using System.Runtime.Versioning;

using Kestrel.PathTrace.Abstractions;

namespace Kestrel.PathTrace.Native.Linux;

/// <summary>
/// Probes and caches NIC hardware-timestamping capabilities per interface.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class NicTimestampProbe
{
    private readonly ConcurrentDictionary<string, NicTimestampCapabilities?> _cache = new();

    /// <summary>
    /// Queries capabilities for <paramref name="ifname"/>, caching the result.
    /// </summary>
    public NicTimestampCapabilities? GetCapabilities(string ifname)
    {
        if (string.IsNullOrEmpty(ifname))
        {
            return null;
        }

        return _cache.GetOrAdd(ifname, HwtstampInterop.QueryNicCapabilities);
    }

    /// <summary>Invalidates the cache entry for <paramref name="ifname"/>.</summary>
    public void Invalidate(string ifname) => _cache.TryRemove(ifname, out _);

    /// <summary>Clears all cached entries.</summary>
    public void InvalidateAll() => _cache.Clear();
}
