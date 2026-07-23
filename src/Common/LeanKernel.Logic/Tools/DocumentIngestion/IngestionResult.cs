namespace LeanKernel.Logic.Tools.DocumentIngestion;

/// <summary>
/// Result of a document ingestion operation.
/// </summary>
/// <param name="Fingerprint">The SHA-256 content fingerprint.</param>
/// <param name="Success">Whether the ingestion succeeded.</param>
/// <param name="IsDuplicate">Whether the document was a duplicate within the same scope.</param>
public sealed record IngestionResult(
    string Fingerprint,
    bool Success,
    bool IsDuplicate);
