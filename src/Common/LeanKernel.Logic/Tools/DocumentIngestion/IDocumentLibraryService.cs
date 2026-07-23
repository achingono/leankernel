namespace LeanKernel.Logic.Tools.DocumentIngestion;

/// <summary>
/// Service that handles document ingestion: fingerprint, dedup, file storage, and catalog upsert.
/// </summary>
public interface IDocumentLibraryService
{
    /// <summary>
    /// Ingests a document job: compute fingerprint, check for duplicates, store on disk, upsert catalog entry.
    /// </summary>
    /// <param name="job">The document ingestion job.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An <see cref="IngestionResult"/> describing the outcome.</returns>
    Task<IngestionResult> IngestDocumentAsync(DocumentIngestionJob job, CancellationToken ct = default);
}
