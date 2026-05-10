namespace LeanKernel.Core.Models;

/// <summary>
/// Describes a capability the agent could not satisfy during a completed turn.
/// </summary>
public sealed record CapabilityGap
{
    /// <summary>
    /// Gets the completed turn event identifier that produced the gap.
    /// </summary>
    public required string TurnEventId { get; init; }

    /// <summary>
    /// Gets the conversation session identifier.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Gets the user request associated with the gap.
    /// </summary>
    public required string UserRequest { get; init; }

    /// <summary>
    /// Gets the gap category or failure type.
    /// </summary>
    public required string GapType { get; init; }

    /// <summary>
    /// Gets the human-readable gap description.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Gets when the gap was observed.
    /// </summary>
    public DateTimeOffset ObservedAt { get; init; } = DateTimeOffset.UtcNow;
}
