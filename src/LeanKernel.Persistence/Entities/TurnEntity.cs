namespace LeanKernel.Persistence.Entities;

/// <summary>
/// Represents a persisted conversation turn within a session.
/// </summary>
public sealed class TurnEntity
{
    /// <summary>
    /// Gets or sets the unique turn identifier.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Gets or sets the parent session identifier.
    /// </summary>
    public required string SessionId { get; set; }

    /// <summary>
    /// Gets or sets the speaker role for the turn.
    /// </summary>
    public required string Role { get; set; }

    /// <summary>
    /// Gets or sets the turn content.
    /// </summary>
    public required string Content { get; set; }

    /// <summary>
    /// Gets or sets when the turn was recorded.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets a value indicating whether the turn was created by compaction.
    /// </summary>
    public bool IsCompacted { get; set; }

    /// <summary>
    /// Gets or sets the source turn identifier when the turn is produced by compaction.
    /// </summary>
    public string? CompactionSourceId { get; set; }

    /// <summary>
    /// Gets or sets the parent session navigation property.
    /// </summary>
    public SessionEntity Session { get; set; } = null!;
}
