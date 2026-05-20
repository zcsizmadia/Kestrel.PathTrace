using Kestrel.PathTrace.Abstractions;

using Microsoft.AspNetCore.Http;

namespace Kestrel.PathTrace;

/// <summary>
/// No-op implementation used on platforms that don't support hardware timestamping.
/// </summary>
internal sealed class NullHardwareTimestampProvider : IHardwareTimestampProvider
{
    internal static readonly NullHardwareTimestampProvider Instance = new();

    private NullHardwareTimestampProvider() { }

    /// <inheritdoc />
    public NicTimestampCapabilities? QueryCapabilities(nint socketHandle) => null;

    /// <inheritdoc />
    public bool EnableTimestamping(nint socketHandle, bool preferHardware = true) => false;

    /// <inheritdoc />
    public ClockCalibration SampleClocks() => default;
}
