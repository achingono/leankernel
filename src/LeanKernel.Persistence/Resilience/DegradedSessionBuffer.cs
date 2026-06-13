using System.Collections.Concurrent;
using LeanKernel.Abstractions.Models;

namespace LeanKernel.Persistence.Resilience;

    /// <summary>
    /// Stores fallback session and history data while the database is degraded.
    /// </summary>
    public sealed class DegradedSessionBuffer
    {
        private readonly ConcurrentDictionary<string, string> _sessionIds = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, string> _sessionOwnerMap = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, ConcurrentQueue<ConversationTurn>> _histories = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or creates a fallback session identifier.
    /// </summary>
    /// <param name="channelId">The channel identifier.</param>
    /// <param name="userId">The user identifier.</param>
    /// <returns>The fallback session identifier.</returns>
    public string GetOrCreateSessionId(string channelId, string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var sessionId = _sessionIds.GetOrAdd($"{channelId}\u001f{userId}", _ => Guid.NewGuid().ToString("N"));
        _sessionOwnerMap.TryAdd(sessionId, userId);
        return sessionId;
    }

    public bool SessionBelongsToUser(string sessionId, string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        return _sessionOwnerMap.TryGetValue(sessionId, out var owner)
            && string.Equals(owner, userId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Appends a fallback turn to the in-memory buffer.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="turn">The conversation turn.</param>
    public void AppendTurn(string sessionId, ConversationTurn turn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(turn);

        var queue = _histories.GetOrAdd(sessionId, _ => new ConcurrentQueue<ConversationTurn>());
        queue.Enqueue(turn);
    }

    /// <summary>
    /// Gets fallback history for a session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="maxTurns">The maximum turn count to return.</param>
    /// <returns>The in-memory history.</returns>
    public IReadOnlyList<ConversationTurn> GetHistory(string sessionId, int maxTurns)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (!_histories.TryGetValue(sessionId, out var queue) || maxTurns <= 0)
        {
            return [];
        }

        return queue
            .ToArray()
            .TakeLast(maxTurns)
            .ToList();
    }
}
