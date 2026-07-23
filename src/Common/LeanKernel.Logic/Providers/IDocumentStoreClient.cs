namespace LeanKernel.Logic.Providers;

/// <summary>
/// Provider-agnostic document catalog and search abstraction.
/// File storage on disk is handled separately by <c>DocumentLibraryService</c>.
/// </summary>
public interface IDocumentStoreClient
{
    /// <summary>
    /// Checks whether a document with the given fingerprint already exists in the specified scope.
    /// </summary>
    /// <param name="scope">The identity and availability scope.</param>
    /// <param name="fingerprint">The SHA-256 content fingerprint.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if a document with the fingerprint exists in the scope.</returns>
    Task<bool> ExistsAsync(DocumentScopeContext scope, string fingerprint, CancellationToken ct = default);

    /// <summary>
    /// Creates or updates a document catalog entry in the specified scope.
    /// </summary>
    /// <param name="scope">The identity and availability scope.</param>
    /// <param name="fingerprint">The SHA-256 content fingerprint.</param>
    /// <param name="document">The document catalog entry to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task UpsertAsync(DocumentScopeContext scope, string fingerprint, DocumentCatalogEntry document, CancellationToken ct = default);

    /// <summary>
    /// Searches documents matching the query within the specified scope and optional channel filter.
    /// </summary>
    /// <param name="scope">The identity and availability scope.</param>
    /// <param name="query">The search query text.</param>
    /// <param name="channelIds">Optional channel identifiers to filter by.</param>
    /// <param name="maxResults">Maximum number of results to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of matching document search hits.</returns>
    Task<IReadOnlyList<DocumentSearchHit>> SearchAsync(DocumentScopeContext scope, string query, IReadOnlyList<Guid>? channelIds, int maxResults, CancellationToken ct = default);

    /// <summary>
    /// Lists documents in the specified scope, optionally filtered by channel.
    /// </summary>
    /// <param name="scope">The identity and availability scope.</param>
    /// <param name="channelIds">Optional channel identifiers to filter by.</param>
    /// <param name="limit">Maximum number of results to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of document catalog entries.</returns>
    Task<IReadOnlyList<DocumentCatalogEntry>> ListAsync(DocumentScopeContext scope, IReadOnlyList<Guid>? channelIds, int limit, CancellationToken ct = default);
}
