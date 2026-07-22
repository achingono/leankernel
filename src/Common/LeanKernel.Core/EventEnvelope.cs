namespace LeanKernel;

/// <summary>
/// Metadata envelope attached to every event in the append-only event spine.
/// Provides the partitioning, correlation, and schema versioning fields
/// required for derived-read projections and multi-service event routing.
/// </summary>
public sealed record EventEnvelope
{
    /// <summary>
    /// Gets the unique event identifier.
    /// </summary>
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Gets the event type discriminator (e.g. "turn", "tool_call", "telemetry").
    /// </summary>
    public string EventType { get; init; } = string.Empty;

    /// <summary>
    /// Gets the schema version for forward-compatible deserialization.
    /// </summary>
    public string SchemaVersion { get; init; } = "1.0";

    /// <summary>
    /// Gets the tenant identifier for data partitioning.
    /// </summary>
    public Guid TenantId { get; init; }

    /// <summary>
    /// Gets the person identifier for memory-scoped partitioning.
    /// </summary>
    public Guid PersonId { get; init; }

    /// <summary>
    /// Gets the user identifier for session/transcript ownership.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Gets the channel identifier for channel-scoped partitioning.
    /// </summary>
    public Guid ChannelId { get; init; }

    /// <summary>
    /// Gets the optional session identifier for anonymous isolation.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Gets the timestamp when the event was emitted.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets an optional correlation identifier for tracing across events.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Gets an optional causation identifier linking this event to its parent event.
    /// </summary>
    public string? CausationId { get; init; }
}