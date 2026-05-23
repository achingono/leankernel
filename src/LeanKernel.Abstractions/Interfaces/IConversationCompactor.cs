using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

public interface IConversationCompactor
{
    Task<string> CompactAsync(IReadOnlyList<ConversationTurn> turns, CancellationToken ct = default);
    Task<string> SummarizeAsync(IReadOnlyList<ConversationTurn> turns, CancellationToken ct = default);
}
