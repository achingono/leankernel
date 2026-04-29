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

    /// <summary>
    /// Store metadata for a session (e.g., MAF middleware diagnostics).
    /// </summary>
    Task SetMetadataAsync(string sessionId, string key, string value, CancellationToken ct);

    /// <summary>
    /// Retrieve session metadata by key.
    /// </summary>
    Task<string?> GetMetadataAsync(string sessionId, string key, CancellationToken ct);

    /// <summary>
    /// Get all metadata for a session.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GetAllMetadataAsync(string sessionId, CancellationToken ct);
}
