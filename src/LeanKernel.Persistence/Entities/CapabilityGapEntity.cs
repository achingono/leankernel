namespace LeanKernel.Persistence.Entities;

/// <summary>
/// Represents a persisted capability gap detected during runtime analysis.
/// </summary>
public sealed class CapabilityGapEntity
{
    /// <summary>
    /// Gets or sets the unique capability gap identifier.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Gets or sets the gap category.
    /// </summary>
    public required string Category { get; set; }

    /// <summary>
    /// Gets or sets the gap description.
    /// </summary>
    public required string Description { get; set; }

    /// <summary>
    /// Gets or sets when the gap was detected.
    /// </summary>
    public DateTimeOffset DetectedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets the related session identifier when available.
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Gets or sets when the gap was resolved.
    /// </summary>
    public DateTimeOffset? ResolvedAt { get; set; }
}
