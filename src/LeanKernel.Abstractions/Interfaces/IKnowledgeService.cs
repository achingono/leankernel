using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

/// <summary>
/// Manages knowledge in the knowledge base (e.g., retrieval and storage of wiki pages).
/// </summary>
public interface IKnowledgeService
{
    /// <summary>
    /// Searches for knowledge candidates matching the given query.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="maxResults">The maximum number of results to return.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous search operation.</returns>
    Task<IReadOnlyList<RetrievalCandidate>> SearchAsync(string query, int maxResults = 10, CancellationToken ct = default);

    /// <summary>
    /// Gets a knowledge page by its unique key.
    /// </summary>
    /// <param name="key">The key of the knowledge page.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The knowledge page if found, otherwise null.</returns>
    Task<KnowledgePage?> GetPageAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Stores or updates a knowledge page.
    /// </summary>
    /// <param name="key">The unique key for the page.</param>
    /// <param name="content">The content of the page.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous storage operation.</returns>
    Task PutPageAsync(string key, string content, CancellationToken ct = default);

    /// <summary>
    /// Deletes a knowledge page.
    /// </summary>
    /// <param name="key">The key of the page to delete.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous deletion operation.</returns>
    Task DeletePageAsync(string key, CancellationToken ct = default);
}
