namespace LeanKernel.Events;

/// <summary>
/// Event emitted when a document ingestion is requested from a channel attachment.
/// Carries staged file reference and metadata; implements <see cref="IHasEnvelope"/>
/// for generic envelope resolution in <c>DbEventStore</c>.
/// </summary>
public sealed record DocumentIngestionRequestedEvent : IHasEnvelope
{
    /// <summary>
    /// Gets the event envelope with partitioning and correlation metadata.
    /// </summary>
    public required EventEnvelope Envelope { get; init; }

    /// <summary>
    /// Gets the staged file path on disk where the attachment bytes were saved.
    /// </summary>
    public required string StagedFilePath { get; init; }

    /// <summary>
    /// Gets the original file name of the attachment.
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Gets the MIME content type of the attachment.
    /// </summary>
    public required string ContentType { get; init; }

    /// <summary>
    /// Gets the availability scope for the ingested document.
    /// </summary>
    public required DocumentAvailabilityScope AvailabilityScope { get; init; }

    /// <summary>
    /// Gets the tenant identifier from the request context.
    /// </summary>
    public Guid TenantId { get; init; }

    /// <summary>
    /// Gets the user identifier from the request context.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Gets the person identifier from the request context.
    /// </summary>
    public Guid PersonId { get; init; }

    /// <summary>
    /// Gets the channel identifier from the request context.
    /// </summary>
    public Guid ChannelId { get; init; }
}
