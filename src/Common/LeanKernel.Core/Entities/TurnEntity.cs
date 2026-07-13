using System.ComponentModel.DataAnnotations;

namespace LeanKernel.Entities;

/// <summary>
/// Represents a persisted conversation turn within a session.
/// </summary>
public sealed class TurnEntity: IAuditable, IRecyclable
{
    /// <summary>
    /// Gets or sets the unique turn identifier.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the parent session identifier.
    /// </summary>
    public required Guid SessionId { get; set; }

    /// <summary>
    /// Gets or sets the speaker role for the turn.
    /// </summary>
    public required string Role { get; set; }

    /// <summary>
    /// Gets or sets the turn content.
    /// </summary>
    public required string Content { get; set; }

    /// <summary>
    /// Gets or sets the display name of the message author (e.g., tool name for tool calls).
    /// </summary>
    public string? AuthorName { get; set; }

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
    /// Gets or sets optional turn metadata persisted as JSON.
    /// </summary>
    public string? Metadata { get; set; }
    
    /// <summary>
    /// Date and time when the tenant was created.
    /// </summary>
    [Required]
    public DateTime CreatedOn { get; set; }

    /// <summary>
    /// Badge of the user who created the tenant.
    /// </summary>
    [Required]
    public Badge CreatedBy { get; set; } = default!;

    /// <summary>
    /// Date and time when the tenant was last updated.
    /// </summary>
    public DateTime? UpdatedOn { get; set; }

    /// <summary>
    /// Badge of the user who last updated the tenant.
    /// </summary>
    public Badge? UpdatedBy { get; set; }

    /// <summary>
    /// Indicates whether the tenant is deleted.
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>
    /// Gets or sets the parent session navigation property.
    /// </summary>
    public SessionEntity Session { get; set; } = null!;
}
