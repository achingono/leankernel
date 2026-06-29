using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

/// <summary>
/// Compresses or summarizes conversation turns for context window management.
/// </summary>
public interface IConversationCompactor
{
    /// <summary>
    /// Compresses a sequence of turns into a summarized representation.
    /// </summary>
    /// <param name="turns">The sequence of turns to compact.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The compressed conversation text.</returns>
    Task<string> CompactAsync(IReadOnlyList<ConversationTurn> turns, CancellationToken ct = default);

    /// <summary>
    /// Summarizes a sequence of turns.
    /// </summary>
    /// <param name="turns">The sequence of turns to summarize.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The summary text.</returns>
    Task<string> SummarizeAsync(IReadOnlyList<ConversationTurn> turns, CancellationToken ct = default);
}
