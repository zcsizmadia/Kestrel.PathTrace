namespace Kestrel.PathTrace.Abstractions;

/// <summary>
/// Provides hardware or software timestamps for a socket / NIC.
/// Implementations differ per OS (Linux <c>hwtstamp_shim</c>, Windows stub).
/// </summary>
public interface IHardwareTimestampProvider
{
    /// <summary>
    /// Queries the NIC timestamp capabilities for the interface that owns
    /// <paramref name="socketHandle"/>.
    /// </summary>
    /// <param name="socketHandle">OS socket handle (nint / IntPtr).</param>
    /// <returns>Capability record, or <see langword="null"/> when unavailable.</returns>
    NicTimestampCapabilities? QueryCapabilities(nint socketHandle);

    /// <summary>
    /// Enables SO_TIMESTAMPING on the socket using the best available mode.
    /// </summary>
    /// <param name="socketHandle">OS socket handle.</param>
    /// <param name="preferHardware">
    /// When <see langword="true"/>, enables hardware timestamping if the NIC supports it;
    /// falls back to software timestamping otherwise.
    /// </param>
    /// <returns><see langword="true"/> if timestamping was successfully enabled.</returns>
    bool EnableTimestamping(nint socketHandle, bool preferHardware = true);

    /// <summary>
    /// Samples multiple system clocks simultaneously for PHC ↔ Stopwatch correlation.
    /// </summary>
    ClockCalibration SampleClocks();
}
