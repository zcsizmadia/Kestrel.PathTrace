namespace Kestrel.PathTrace.Abstractions;

/// <summary>
/// A single hardware or software timestamp in nanoseconds.
/// </summary>
public readonly record struct HardwareTimestamp
{
    /// <summary>Gets the seconds component.</summary>
    public long Seconds { get; init; }

    /// <summary>Gets the nanoseconds component [0, 999_999_999].</summary>
    public long Nanoseconds { get; init; }

    /// <summary>Gets a value indicating whether this timestamp was populated.</summary>
    public bool IsValid { get; init; }

    /// <summary>Gets the total nanoseconds since epoch.</summary>
    public long TotalNanoseconds => (Seconds * 1_000_000_000L) + Nanoseconds;

    /// <summary>Returns an invalid (empty) timestamp.</summary>
    public static HardwareTimestamp Invalid => default;

    /// <inheritdoc />
    public override string ToString() =>
        IsValid ? $"{Seconds}.{Nanoseconds:D9}" : "<invalid>";
}
