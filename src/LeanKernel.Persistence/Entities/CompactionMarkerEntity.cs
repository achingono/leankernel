namespace LeanKernel.Persistence.Entities;

/// <summary>
/// Provides functionality for compaction marker entity.
/// </summary>
public sealed class CompactionMarkerEntity
{
    /// <summary>
    /// Gets or sets id.
    /// </summary>
    public Guid Id { get; set; }
    /// <summary>
    /// Gets or sets session id.
    /// </summary>
    public required string SessionId { get; set; }
    /// <summary>
    /// Gets or sets marker type.
    /// </summary>
    public required string MarkerType { get; set; }
    /// <summary>
    /// Gets or sets compacted at.
    /// </summary>
    public required DateTimeOffset CompactedAt { get; set; }
    /// <summary>
    /// Gets or sets original turn count.
    /// </summary>
    public required int OriginalTurnCount { get; set; }
    /// <summary>
    /// Gets or sets original token count.
    /// </summary>
    public required int OriginalTokenCount { get; set; }
    /// <summary>
    /// Gets or sets compacted token count.
    /// </summary>
    public required int CompactedTokenCount { get; set; }
    /// <summary>
    /// Gets or sets compacted content.
    /// </summary>
    public string? CompactedContent { get; set; }
    /// <summary>
    /// Gets or sets compacted by.
    /// </summary>
    public string? CompactedBy { get; set; }
}
