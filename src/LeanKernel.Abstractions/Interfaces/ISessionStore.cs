using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

public interface ISessionStore
{
    Task<string> GetOrCreateSessionIdAsync(string channelId, string userId, CancellationToken ct = default);
    Task AppendTurnAsync(string sessionId, ConversationTurn turn, CancellationToken ct = default);
    Task<IReadOnlyList<ConversationTurn>> GetHistoryAsync(string sessionId, int maxTurns = 50, CancellationToken ct = default);
}
