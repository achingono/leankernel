namespace LeanKernel.Entities;

/// <summary>
/// Represents a persisted agent runtime state checkpoint for durable storage.
/// </summary>
public sealed class AgentStateEntity
{
    /// <summary>
    /// Gets or sets the isolation-scoped conversation identifier (primary key).
    /// </summary>
    public string ScopedConversationId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the canonical tenant identifier.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets the canonical user identifier.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the canonical channel identifier.
    /// </summary>
    public Guid ChannelId { get; set; }

    /// <summary>
    /// Gets or sets the serialized agent session state as JSON.
    /// </summary>
    public string StateJson { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when the session state was created.
    /// </summary>
    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets when the session state was last updated.
    /// </summary>
    public DateTimeOffset UpdatedOn { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets the optimistic concurrency token.
    /// </summary>
    public byte[] RowVersion { get; set; } = [];
}