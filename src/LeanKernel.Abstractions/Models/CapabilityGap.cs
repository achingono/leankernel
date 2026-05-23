namespace LeanKernel.Abstractions.Models;

public sealed record CapabilityGap
{
    public required string Category { get; init; }

    public required string Description { get; init; }

    public required string DetectedInSession { get; init; }

    public required string DetectedInTurn { get; init; }

    public DateTimeOffset DetectedAt { get; init; } = DateTimeOffset.UtcNow;

    public int OccurrenceCount { get; init; } = 1;
}
