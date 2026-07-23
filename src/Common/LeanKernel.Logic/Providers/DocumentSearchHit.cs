namespace LeanKernel.Logic.Providers;

/// <summary>
/// Represents a single search result from <see cref="IDocumentStoreClient.SearchAsync"/>.
/// </summary>
/// <param name="Fingerprint">The document fingerprint.</param>
/// <param name="FileName">The original file name.</param>
/// <param name="ContentType">The MIME content type.</param>
/// <param name="Excerpt">A short excerpt of the matched content.</param>
/// <param name="Score">The relevance score.</param>
/// <param name="IngestedAt">The ingestion timestamp.</param>
public sealed record DocumentSearchHit(
    string Fingerprint,
    string FileName,
    string ContentType,
    string Excerpt,
    double Score,
    DateTime IngestedAt);
