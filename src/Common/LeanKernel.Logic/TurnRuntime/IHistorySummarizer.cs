using Microsoft.Extensions.AI;

namespace LeanKernel.Logic.TurnRuntime;

/// <summary>
/// Summarizes older conversation turns into a compact message.
/// </summary>
public interface IHistorySummarizer
{
    /// <summary>
    /// Produces a summary for the provided history messages.
    /// Returns null when no summary can be produced.
    /// </summary>
    /// <param name="messages">The chat messages to summarize.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The summary text, or null if no summary can be produced.</returns>
    Task<string?> SummarizeAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default);
}