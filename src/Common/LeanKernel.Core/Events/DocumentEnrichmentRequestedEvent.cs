namespace LeanKernel.Events;

/// <summary>
/// Event emitted when a document ingestion completes and enrichment is requested.
/// Carries correlation metadata linking back to the original ingestion job.
/// </summary>
public sealed record DocumentEnrichmentRequestedEvent : IHasEnvelope
{
    /// <summary>
    /// Gets the event envelope with partitioning and correlation metadata.
    /// </summary>
    public required EventEnvelope Envelope { get; init; }

    /// <summary>
    /// Gets the originating ingestion job identifier.
    /// </summary>
    public required Guid IngestionJobId { get; init; }

    /// <summary>
    /// Gets the tenant identifier from the request context.
    /// </summary>
    public required Guid TenantId { get; init; }

    /// <summary>
    /// Gets the user identifier from the request context.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// Gets the person identifier from the request context.
    /// </summary>
    public required Guid PersonId { get; init; }

    /// <summary>
    /// Gets the channel identifier from the request context.
    /// </summary>
    public required Guid ChannelId { get; init; }
}