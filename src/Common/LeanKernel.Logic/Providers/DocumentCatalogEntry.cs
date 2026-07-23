namespace LeanKernel.Logic.Providers;

/// <summary>
/// Represents a document catalog entry stored via <see cref="IDocumentStoreClient"/>.
/// </summary>
/// <param name="Fingerprint">The SHA-256 content fingerprint.</param>
/// <param name="FileName">The original file name.</param>
/// <param name="ContentType">The MIME content type.</param>
/// <param name="ExtractedText">The extracted text content.</param>
/// <param name="TenantId">The tenant identifier.</param>
/// <param name="UserId">The user identifier.</param>
/// <param name="PersonId">The person identifier.</param>
/// <param name="ChannelId">The channel identifier.</param>
/// <param name="AvailabilityScope">The document availability scope.</param>
/// <param name="IngestedAt">The ingestion timestamp.</param>
public sealed record DocumentCatalogEntry(
    string Fingerprint,
    string FileName,
    string ContentType,
    string ExtractedText,
    Guid TenantId,
    Guid UserId,
    Guid PersonId,
    Guid ChannelId,
    DocumentAvailabilityScope AvailabilityScope,
    DateTime IngestedAt);
