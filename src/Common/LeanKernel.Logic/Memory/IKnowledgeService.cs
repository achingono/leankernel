namespace LeanKernel.Logic.Memory;

/// <summary>
/// Provides callable knowledge/wiki operations backed by memory provider.
/// </summary>
public interface IMemoryService
{
    /// <summary>
    /// Searches for knowledge pages matching the given query.
    /// </summary>
    Task<IReadOnlyList<MemorySearchResult>> SearchAsync(
        string query,
        int maxResults = 10,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves a knowledge page by its key.
    /// </summary>
    Task<MemoryPage?> GetPageAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Creates or updates a knowledge page.
    /// </summary>
    Task PutPageAsync(string key, string content, CancellationToken ct = default);
}