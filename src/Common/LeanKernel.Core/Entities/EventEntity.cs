namespace LeanKernel.Entities;

/// <summary>
/// Represents an append-only event persisted to the event spine store.
/// </summary>
public sealed class EventEntity : IEntity
{
    /// <summary>
    /// Gets or sets the primary key for the persisted event row.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the envelope event identifier.
    /// </summary>
    public Guid EventId { get; set; }

    /// <summary>
    /// Gets or sets the event type discriminator.
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the schema version used by the payload.
    /// </summary>
    public string SchemaVersion { get; set; } = "1.0";

    /// <summary>
    /// Gets or sets the tenant partition key.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets the person partition key.
    /// </summary>
    public Guid PersonId { get; set; }

    /// <summary>
    /// Gets or sets the user partition key.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the channel partition key.
    /// </summary>
    public Guid ChannelId { get; set; }

    /// <summary>
    /// Gets or sets the optional session partition key.
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Gets or sets the event timestamp from the envelope.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the optional correlation id.
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Gets or sets the optional causation id.
    /// </summary>
    public string? CausationId { get; set; }

    /// <summary>
    /// Gets or sets the CLR event record type name.
    /// </summary>
    public string RecordType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the serialized JSON payload.
    /// </summary>
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when the event row was persisted.
    /// </summary>
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
}
