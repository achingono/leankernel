namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Represents a single turn in a conversation.
/// </summary>
public sealed record ConversationTurn
{
    /// <summary>
    /// Gets or sets the role of the participant (e.g., "user", "assistant").
    /// </summary>
    public required string Role { get; init; }

    /// <summary>
    /// Gets or sets the content of the turn.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Gets or sets the timestamp when the turn occurred.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets the unique identifier for the turn.
    /// </summary>
    public string? TurnId { get; init; }

    /// <summary>
    /// Gets a value indicating whether the turn has been compacted.
    /// </summary>
    public bool IsCompacted { get; init; }

    /// <summary>
    /// Gets or sets the ID of the turn that was used as a source for compaction.
    /// </summary>
    public string? CompactionSourceId { get; init; }
}
