using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Persistence;

/// <summary>
/// Provides PostgreSQL-backed session persistence.
/// </summary>
public sealed class PostgresSessionStore(
    IDbContextFactory<LeanKernelDbContext> dbFactory,
    ILogger<PostgresSessionStore> logger) : ISessionStore
{
    private readonly IDbContextFactory<LeanKernelDbContext> _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly ILogger<PostgresSessionStore> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Gets an existing session identifier for the supplied channel and user pair or creates a new one.
    /// </summary>
    /// <param name="channelId">The channel identifier.</param>
    /// <param name="userId">The user identifier.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The existing or newly created session identifier.</returns>
    public async Task<string> GetOrCreateSessionIdAsync(string channelId, string userId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var session = await db.Sessions
            .Where(s => s.ChannelId == channelId && s.UserId == userId)
            .OrderByDescending(s => s.UpdatedAt)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (session is not null)
        {
            session.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return session.Id;
        }

        var newSession = new SessionEntity
        {
            ChannelId = channelId,
            UserId = userId,
        };

        db.Sessions.Add(newSession);

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Created new session {SessionId} for {ChannelId}/{UserId}",
                newSession.Id,
                channelId,
                userId);

            return newSession.Id;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(
                ex,
                "Session creation raced for {ChannelId}/{UserId}; retrying lookup",
                channelId,
                userId);
        }

        await using var retryDb = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var racedSession = await retryDb.Sessions
            .Where(s => s.ChannelId == channelId && s.UserId == userId)
            .OrderByDescending(s => s.UpdatedAt)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Failed to create or retrieve a session for channel '{channelId}' and user '{userId}'.");

        racedSession.UpdatedAt = DateTimeOffset.UtcNow;
        await retryDb.SaveChangesAsync(ct).ConfigureAwait(false);
        return racedSession.Id;
    }

    /// <summary>
    /// Appends a conversation turn to an existing session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="turn">The turn to persist.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes when the turn is stored.</returns>
    public async Task AppendTurnAsync(string sessionId, ConversationTurn turn, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(turn);

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var session = await db.Sessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Session '{sessionId}' was not found.");

        session.UpdatedAt = DateTimeOffset.UtcNow;

        var entity = new TurnEntity
        {
            Id = string.IsNullOrWhiteSpace(turn.TurnId) ? Guid.NewGuid().ToString() : turn.TurnId,
            SessionId = sessionId,
            Role = turn.Role,
            Content = turn.Content,
            Timestamp = turn.Timestamp,
            IsCompacted = turn.IsCompacted,
            CompactionSourceId = turn.CompactionSourceId,
        };

        db.Turns.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the most recent conversation history for a session in chronological order.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="maxTurns">The maximum number of turns to return.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The ordered conversation history for the session.</returns>
    public async Task<IReadOnlyList<ConversationTurn>> GetHistoryAsync(string sessionId, int maxTurns = 50, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (maxTurns <= 0)
        {
            return [];
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var turns = await db.Turns
            .AsNoTracking()
            .Where(t => t.SessionId == sessionId)
            .OrderByDescending(t => t.Timestamp)
            .ThenByDescending(t => t.Id)
            .Take(maxTurns)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return turns
            .OrderBy(t => t.Timestamp)
            .ThenBy(t => t.Id)
            .Select(t => new ConversationTurn
            {
                Role = t.Role,
                Content = t.Content,
                Timestamp = t.Timestamp,
                TurnId = t.Id,
                IsCompacted = t.IsCompacted,
                CompactionSourceId = t.CompactionSourceId,
            })
            .ToList();
    }
}
