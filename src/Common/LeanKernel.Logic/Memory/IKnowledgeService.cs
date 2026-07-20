namespace LeanKernel.Logic.Memory;

/// <summary>
/// Provides callable knowledge/wiki operations backed by memory provider.
/// </summary>
public interface IMemoryService
{
    /// <summary>
    /// Searches for knowledge pages matching the given query.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="maxResults">Maximum number of results to return.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<MemorySearchResult>> SearchAsync(
        string query,
        int maxResults = 10,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves a knowledge page by its key.
    /// </summary>
    /// <param name="key">The page key.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<MemoryPage?> GetPageAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Creates or updates a knowledge page.
    /// </summary>
    /// <param name="key">The page key.</param>
    /// <param name="content">The page content.</param>
    /// <param name="ct">Cancellation token.</param>
    Task PutPageAsync(string key, string content, CancellationToken ct = default);
}