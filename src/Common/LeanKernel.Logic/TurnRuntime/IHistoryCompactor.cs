using Microsoft.Extensions.AI;

namespace LeanKernel.Logic.TurnRuntime;

/// <summary>
/// Compacts conversation history by extracting the most salient sentences
/// from older turns using embedding-based relevance scoring.
/// Returns null when compaction cannot be performed.
/// </summary>
public interface IHistoryCompactor
{
    /// <summary>
    /// Compacts the provided history messages into a reduced set of salient sentences.
    /// </summary>
    /// <param name="messages">The history messages to compact.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// A compacted text representation, or null when compaction is unavailable.
    /// </returns>
    Task<string?> CompactAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default);
}