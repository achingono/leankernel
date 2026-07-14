namespace LeanKernel.Gateway.Providers;

/// <summary>
/// Represents a knowledge page retrieved from GBrain.
/// </summary>
public sealed class KnowledgePage
{
    /// <summary>
    /// Gets the page key (slug).
    /// </summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// Gets the page content.
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// Gets the last modification date, if available.
    /// </summary>
    public DateTimeOffset? LastModified { get; init; }
}

/// <summary>
/// Represents a single search result from a knowledge search.
/// </summary>
public sealed class KnowledgeSearchResult
{
    /// <summary>
    /// Gets the page key.
    /// </summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// Gets an excerpt or summary of the matching content.
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// Gets the relevance score.
    /// </summary>
    public double Score { get; init; }
}

/// <summary>
/// Provides callable knowledge/wiki operations backed by GBrain.
/// </summary>
public interface IKnowledgeService
{
    /// <summary>
    /// Searches for knowledge pages matching the given query.
    /// </summary>
    Task<IReadOnlyList<KnowledgeSearchResult>> SearchAsync(
        string query,
        int maxResults = 10,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves a knowledge page by its key.
    /// </summary>
    Task<KnowledgePage?> GetPageAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Creates or updates a knowledge page.
    /// </summary>
    Task PutPageAsync(string key, string content, CancellationToken ct = default);
}
