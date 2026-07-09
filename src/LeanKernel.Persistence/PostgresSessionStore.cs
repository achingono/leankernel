using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Persistence.Entities;
using System.Text.Json;
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
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private const int MaxConcurrencyRetries = 3;

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
            await TouchSessionWithRetryAsync(db, session, ct).ConfigureAwait(false);
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

        await TouchSessionWithRetryAsync(retryDb, racedSession, ct).ConfigureAwait(false);
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

        session.UpdatedAt = MaxTimestamp(session.UpdatedAt, DateTimeOffset.UtcNow);

        var entity = new TurnEntity
        {
            Id = string.IsNullOrWhiteSpace(turn.TurnId) ? Guid.NewGuid().ToString() : turn.TurnId,
            SessionId = sessionId,
            Role = turn.Role,
            Content = turn.Content,
            Timestamp = turn.Timestamp,
            IsCompacted = turn.IsCompacted,
            CompactionSourceId = turn.CompactionSourceId,
            Metadata = turn.Metadata is null
                ? null
                : JsonSerializer.Serialize(turn.Metadata, JsonOptions),
        };

        db.Turns.Add(entity);
        await SaveChangesWithConcurrencyRetryAsync(db, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> SessionBelongsToUserAsync(string sessionId, string userId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        return await db.Sessions
            .AsNoTracking()
            .AnyAsync(s => s.Id == sessionId && s.UserId == userId, ct)
            .ConfigureAwait(false);
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
                Metadata = ParseMetadata(t.Metadata, _logger),
            })
            .ToList();
    }

    private static IReadOnlyDictionary<string, string>? ParseMetadata(string? metadata, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(metadata, JsonOptions);
            return parsed is null
                ? null
                : new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to deserialize turn metadata.");
            return null;
        }
        catch (NotSupportedException ex)
        {
            logger.LogWarning(ex, "Unsupported turn metadata payload encountered.");
            return null;
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        return new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    private async Task TouchSessionWithRetryAsync(LeanKernelDbContext db, SessionEntity session, CancellationToken ct)
    {
        session.UpdatedAt = MaxTimestamp(session.UpdatedAt, DateTimeOffset.UtcNow);
        await SaveChangesWithConcurrencyRetryAsync(db, ct).ConfigureAwait(false);
    }

    private async Task SaveChangesWithConcurrencyRetryAsync(LeanKernelDbContext db, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxConcurrencyRetries; attempt++)
        {
            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                return;
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < MaxConcurrencyRetries)
            {
                _logger.LogWarning(
                    ex,
                    "Session write encountered a concurrency conflict (attempt {Attempt}/{MaxAttempts}); retrying",
                    attempt,
                    MaxConcurrencyRetries);

                foreach (var entry in ex.Entries)
                {
                    await entry.ReloadAsync(ct).ConfigureAwait(false);
                    if (entry.Entity is SessionEntity sessionEntity)
                    {
                        sessionEntity.UpdatedAt = MaxTimestamp(sessionEntity.UpdatedAt, DateTimeOffset.UtcNow);
                    }
                }
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static DateTimeOffset MaxTimestamp(DateTimeOffset left, DateTimeOffset right)
        => left >= right ? left : right;
}
