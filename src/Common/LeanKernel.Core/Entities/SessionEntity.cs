namespace LeanKernel.Entities;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Represents a persisted chat session for a channel and user pair.
/// </summary>
public class SessionEntity : IAuditable, IRecyclable, IEntity
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

    /// <summary>
    /// Gets or sets the date and time when the session was created.
    /// </summary>
    [Required]
    public DateTime CreatedOn { get; set; }

    /// <summary>
    /// Gets or sets the badge of the user who created the session.
    /// </summary>
    [Required]
    public Badge CreatedBy { get; set; } = default!;

    /// <summary>
    /// Gets or sets the date and time when the session was last updated.
    /// </summary>
    public DateTime? UpdatedOn { get; set; }

    /// <summary>
    /// Gets or sets the badge of the user who last updated the session.
    /// </summary>
    public Badge? UpdatedBy { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the session is deleted.
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>
    /// Gets or sets the user associated with the session.
    /// </summary>
    public UserEntity User { get; set; } = new();

    /// <summary>
    /// Gets or sets the channel associated with the session.
    /// </summary>
    public ChannelEntity Channel { get; set; } = new();

    /// <summary>
    /// Gets or sets the tenant associated with the session.
    /// </summary>
    public TenantEntity Tenant { get; set; } = new();

    /// <summary>
    /// Gets or sets the conversation turns associated with the session.
    /// </summary>
    public virtual ICollection<TurnEntity> Turns { get; set; } = new List<TurnEntity>();
}