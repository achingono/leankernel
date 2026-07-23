namespace LeanKernel.Logic.Tools.DocumentIngestion;

/// <summary>
/// Represents a document ingestion job with identity, file metadata, and availability scope.
/// </summary>
/// <param name="FilePath">The staged file path on disk.</param>
/// <param name="FileName">The original file name.</param>
/// <param name="ContentType">The MIME content type.</param>
/// <param name="TenantId">The tenant identifier.</param>
/// <param name="UserId">The user identifier.</param>
/// <param name="PersonId">The person identifier.</param>
/// <param name="ChannelId">The channel identifier.</param>
/// <param name="AvailabilityScope">The document availability scope.</param>
/// <param name="Source">The ingestion source discriminator.</param>
public sealed record DocumentIngestionJob(
    string FilePath,
    string FileName,
    string ContentType,
    Guid TenantId,
    Guid UserId,
    Guid PersonId,
    Guid ChannelId,
    DocumentAvailabilityScope AvailabilityScope,
    DocumentIngestionSource Source);
