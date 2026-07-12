namespace LeanKernel.Entities;

/// <summary>
/// Represents a persisted chat session for a channel and user pair.
/// </summary>
public sealed class SessionEntity
{
    /// <summary>
    /// Gets or sets the unique session identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the external OpenAI conversation identifier.
    /// </summary>
    public string? ConversationId { get; set; }

    /// <summary>
    /// Gets or sets the channel identifier associated with the session.
    /// </summary>
    public required Guid ChannelId { get; set; }

    /// <summary>
    /// Gets or sets the user identifier associated with the session.
    /// </summary>
    public required Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the tenant identifier associated with the session.
    /// </summary>
    public required Guid TenantId { get; set; }

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

    public UserEntity User { get; set; } = new();

    public ChannelEntity Channel { get; set; } = new();

    public TenantEntity Tenant { get; set; } = new();

    /// <summary>
    /// Gets or sets the conversation turns associated with the session.
    /// </summary>
    public ICollection<TurnEntity> Turns { get; set; } = new List<TurnEntity>();
}
