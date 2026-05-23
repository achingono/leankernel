namespace LeanKernel.Persistence.Entities;

public sealed class CompactionMarkerEntity
{
    public Guid Id { get; set; }
    public required string SessionId { get; set; }
    public required string MarkerType { get; set; }
    public required DateTimeOffset CompactedAt { get; set; }
    public required int OriginalTurnCount { get; set; }
    public required int OriginalTokenCount { get; set; }
    public required int CompactedTokenCount { get; set; }
    public string? CompactedContent { get; set; }
    public string? CompactedBy { get; set; }
}
