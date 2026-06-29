using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

/// <summary>
/// Provides persistent storage for conversation sessions.
/// </summary>
public interface ISessionStore
{
    /// <summary>
    /// Gets or creates a session ID for the given channel and user.
    /// </summary>
    /// <param name="channelId">The channel identifier.</param>
    /// <param name="userId">The user identifier.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The session identifier.</returns>
    Task<string> GetOrCreateSessionIdAsync(string channelId, string userId, CancellationToken ct = default);

    /// <summary>
    /// Appends a new turn to the session history.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="turn">The turn to append.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task AppendTurnAsync(string sessionId, ConversationTurn turn, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the history for a given session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="maxTurns">The maximum number of turns to retrieve.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The conversation history.</returns>
    Task<IReadOnlyList<ConversationTurn>> GetHistoryAsync(string sessionId, int maxTurns = 50, CancellationToken ct = default);

    /// <summary>
    /// Verifies if a session belongs to a given user.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="userId">The user identifier.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>True if the session belongs to the user; otherwise false.</returns>
    Task<bool> SessionBelongsToUserAsync(string sessionId, string userId, CancellationToken ct = default);
}
