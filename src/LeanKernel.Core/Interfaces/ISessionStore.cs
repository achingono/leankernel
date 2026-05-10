using LeanKernel.Core.Models;

namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Conversation session persistence. Stores turn history per conversation.
/// </summary>
public interface ISessionStore
{
    /// <summary>
    /// Gets the stored conversation turns for a session.
    /// </summary>
    Task<List<ConversationTurn>> GetHistoryAsync(string sessionId, CancellationToken ct);
    /// <summary>
    /// Appends a conversation turn to the session history.
    /// </summary>
    Task AppendTurnAsync(string sessionId, ConversationTurn turn, CancellationToken ct);
    /// <summary>
    /// Gets the existing session identifier for a channel/sender pair or creates one.
    /// </summary>
    Task<string> GetOrCreateSessionIdAsync(string channelId, string senderId, CancellationToken ct);
    /// <summary>
    /// Compacts a session's stored history when it grows beyond the configured retention shape.
    /// </summary>
    Task CompactAsync(string sessionId, CancellationToken ct);
    /// <summary>
    /// Lists known conversation session identifiers.
    /// </summary>
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
