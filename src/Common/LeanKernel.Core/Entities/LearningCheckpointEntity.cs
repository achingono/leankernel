namespace LeanKernel.Entities;

/// <summary>
/// Tracks the last processed turn-completed event for learning replay recovery.
/// </summary>
public sealed class LearningCheckpointEntity
{
    /// <summary>
    /// Gets or sets the checkpoint key.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the timestamp of the last processed event row.
    /// </summary>
    public DateTime? LastProcessedCreatedOnUtc { get; set; }

    /// <summary>
    /// Gets or sets the event-row identifier of the last processed event.
    /// </summary>
    public Guid? LastProcessedEventRowId { get; set; }

    /// <summary>
    /// Gets or sets when the checkpoint was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}