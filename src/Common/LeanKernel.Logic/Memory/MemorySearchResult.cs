namespace LeanKernel.Logic.Memory;

/// <summary>
/// Represents a single search result from a memory search.
/// </summary>
public sealed class MemorySearchResult
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