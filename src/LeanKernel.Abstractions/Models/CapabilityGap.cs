namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Represents a gap in capabilities detected by the system.
/// </summary>
public sealed record CapabilityGap
{
    /// <summary>
    /// Gets the category of the capability gap.
    /// </summary>
    public required string Category { get; init; }

    /// <summary>
    /// Gets a description of the detected capability gap.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Gets the session identifier where the gap was detected.
    /// </summary>
    public required string DetectedInSession { get; init; }

    /// <summary>
    /// Gets the turn identifier where the gap was detected.
    /// </summary>
    public required string DetectedInTurn { get; init; }

    /// <summary>
    /// Gets the timestamp when the gap was detected.
    /// </summary>
    public DateTimeOffset DetectedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets the number of times the gap has been detected.
    /// </summary>
    public int OccurrenceCount { get; init; } = 1;
}
