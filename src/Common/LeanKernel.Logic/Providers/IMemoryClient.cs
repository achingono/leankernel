namespace LeanKernel.Logic.Providers;

/// <summary>
/// Provides memory search and persistence capabilities backed by Memory or an in-memory stub.
/// </summary>
public interface IMemoryClient
{
    /// <summary>
    /// Searches for memories matching the query within the given scope.
    /// </summary>
    /// <param name="scope">The scope to search within.</param>
    /// <param name="query">The query text to search for.</param>
    /// <param name="maxResults">The maximum number of results to return.</param>
    /// <param name="ct">The token used to cancel the operation.</param>
    /// <returns>The matching memory items.</returns>
    Task<IReadOnlyList<MemoryItem>> SearchMemoriesAsync(
        MemoryScope scope,
        string query,
        int maxResults = 10,
        CancellationToken ct = default);

    /// <summary>
    /// Saves a memory item within the given scope.
    /// </summary>
    /// <param name="scope">The scope to save within.</param>
    /// <param name="key">The scope-relative memory key.</param>
    /// <param name="content">The memory content to persist.</param>
    /// <param name="ct">The token used to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SaveMemoryAsync(
        MemoryScope scope,
        string key,
        string content,
        CancellationToken ct = default);
}
