using LeanKernel.Core.Models;

namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Conversation session persistence. Stores turn history per conversation.
/// </summary>
public interface ISessionStore
{
    Task<List<ConversationTurn>> GetHistoryAsync(string sessionId, CancellationToken ct);
    Task AppendTurnAsync(string sessionId, ConversationTurn turn, CancellationToken ct);
    Task<string> GetOrCreateSessionIdAsync(string channelId, string senderId, CancellationToken ct);
    Task CompactAsync(string sessionId, CancellationToken ct);
    Task<IReadOnlyList<string>> ListSessionsAsync(CancellationToken ct);
}
