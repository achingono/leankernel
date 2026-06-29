namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Defines the levels of history detail.
/// </summary>
public enum HistoryTier
{
    /// <summary>
    /// Original, unchanged turn.
    /// </summary>
    Verbatim,

    /// <summary>
    /// Compacted version of a turn.
    /// </summary>
    Compacted,

    /// <summary>
    /// Summarized version of a turn.
    /// </summary>
    Summarized,

    /// <summary>
    /// Turn that has been removed from history.
    /// </summary>
    Dropped
}

/// <summary>
/// Represents an entry in the shaped history.
/// </summary>
public sealed record ShapedHistoryEntry
{
    /// <summary>
    /// Gets the content of the entry.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Gets the tier of the history entry.
    /// </summary>
    public required HistoryTier Tier { get; init; }

    /// <summary>
    /// Gets the role associated with the entry.
    /// </summary>
    public required string Role { get; init; }

    /// <summary>
    /// Gets the original timestamp of the turn.
    /// </summary>
    public DateTimeOffset OriginalTimestamp { get; init; }

    /// <summary>
    /// Gets the ID of the original turn, if any.
    /// </summary>
    public string? OriginalTurnId { get; init; }

    /// <summary>
    /// Gets the number of tokens in the entry.
    /// </summary>
    public int TokenCount { get; init; }

    /// <summary>
    /// Gets information about the compaction process, if any.
    /// </summary>
    public CompactionMarker? CompactionInfo { get; init; }
}

/// <summary>
/// Represents metadata for a compaction operation.
/// </summary>
public sealed record CompactionMarker
{
    /// <summary>
    /// Gets the type of compaction.
    /// </summary>
    public required string MarkerType { get; init; }

    /// <summary>
    /// Gets the timestamp when the compaction occurred.
    /// </summary>
    public required DateTimeOffset CompactedAt { get; init; }

    /// <summary>
    /// Gets the number of original turns.
    /// </summary>
    public required int OriginalTurnCount { get; init; }

    /// <summary>
    /// Gets the original token count.
    /// </summary>
    public required int OriginalTokenCount { get; init; }

    /// <summary>
    /// Gets the compacted token count.
    /// </summary>
    public required int CompactedTokenCount { get; init; }

    /// <summary>
    /// Gets the identifier of the entity that performed the compaction.
    /// </summary>
    public string? CompactedBy { get; init; }
}
