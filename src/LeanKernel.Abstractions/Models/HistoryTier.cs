namespace LeanKernel.Abstractions.Models;

public enum HistoryTier
{
    Verbatim,
    Compacted,
    Summarized,
    Dropped
}

public sealed record ShapedHistoryEntry
{
    public required string Content { get; init; }
    public required HistoryTier Tier { get; init; }
    public required string Role { get; init; }
    public DateTimeOffset OriginalTimestamp { get; init; }
    public string? OriginalTurnId { get; init; }
    public int TokenCount { get; init; }
    public CompactionMarker? CompactionInfo { get; init; }
}

public sealed record CompactionMarker
{
    public required string MarkerType { get; init; }
    public required DateTimeOffset CompactedAt { get; init; }
    public required int OriginalTurnCount { get; init; }
    public required int OriginalTokenCount { get; init; }
    public required int CompactedTokenCount { get; init; }
    public string? CompactedBy { get; init; }
}
