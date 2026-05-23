namespace LeanKernel.Persistence.Entities;

/// <summary>
/// Represents a persisted chat session for a channel and user pair.
/// </summary>
public sealed class SessionEntity
{
    /// <summary>
    /// Gets or sets the unique session identifier.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Gets or sets the channel identifier associated with the session.
    /// </summary>
    public required string ChannelId { get; set; }

    /// <summary>
    /// Gets or sets the user identifier associated with the session.
    /// </summary>
    public required string UserId { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the session was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets the timestamp when the session was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets optional JSON metadata for the session.
    /// </summary>
    public string? Metadata { get; set; }

    /// <summary>
    /// Gets or sets the conversation turns associated with the session.
    /// </summary>
    public List<TurnEntity> Turns { get; set; } = [];
}
