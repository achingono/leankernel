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
    Task<string?> SummarizeAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default);
}
