using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Gateway.Services;

public sealed class ChatService(
    IAgentRuntime agentRuntime,
    ISessionStore sessionStore,
    ILogger<ChatService> logger,
    IDbContextFactory<LeanKernelDbContext>? dbContextFactory = null)
{
    public const string ChannelPrefix = "blazor";
    private const int MaxHistoryTurns = 100;

    private readonly IAgentRuntime _agentRuntime = agentRuntime ?? throw new ArgumentNullException(nameof(agentRuntime));
    private readonly ISessionStore _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
    private readonly ILogger<ChatService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IDbContextFactory<LeanKernelDbContext>? _dbContextFactory = dbContextFactory;

    public string? OwnerId { get; private set; }
    public string? CurrentSessionId { get; private set; }
    public string? CurrentChannelId { get; private set; }
    public bool IsInitialized { get; private set; }
    public bool IsLoading { get; private set; }
    public string ComposerText { get; set; } = string.Empty;
    public string? ErrorMessage { get; private set; }
    public List<ChatSessionSummary> Sessions { get; } = [];
    public List<ChatMessageViewModel> Messages { get; } = [];

    public async Task InitializeAsync(
        string ownerId,
        IReadOnlyList<ChatSessionSummary>? cachedSessions,
        string? requestedSessionId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        OwnerId = ownerId;
        IsInitialized = true;
        ErrorMessage = null;

        ReplaceSessions(cachedSessions ?? []);
        await RefreshSessionsAsync(ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(requestedSessionId))
        {
            await OpenSessionAsync(requestedSessionId, ct).ConfigureAwait(false);
            return;
        }

        if (!string.IsNullOrWhiteSpace(CurrentSessionId))
        {
            await OpenSessionAsync(CurrentSessionId, ct).ConfigureAwait(false);
            return;
        }

        if (Sessions.Count > 0)
        {
            await OpenSessionAsync(Sessions[0].SessionId, ct).ConfigureAwait(false);
            return;
        }

        ClearConversation();
    }

    public async Task<string> CreateNewSessionAsync(CancellationToken ct = default)
    {
        EnsureReady();

        var conversationKey = Guid.NewGuid().ToString("N");
        var channelId = BuildChannelId(conversationKey);
        var sessionId = await _sessionStore.GetOrCreateSessionIdAsync(channelId, OwnerId!, ct).ConfigureAwait(false);

        CurrentSessionId = sessionId;
        CurrentChannelId = channelId;
        ErrorMessage = null;
        Messages.Clear();

        UpsertSession(new ChatSessionSummary
        {
            SessionId = sessionId,
            ChannelId = channelId,
            Title = "New session",
            Preview = "Start a conversation",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            HasMessages = false,
        });

        await RefreshSessionsAsync(ct).ConfigureAwait(false);
        return sessionId;
    }

    public async Task OpenSessionAsync(string sessionId, CancellationToken ct = default)
    {
        EnsureReady();
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var cached = Sessions.FirstOrDefault(session => string.Equals(session.SessionId, sessionId, StringComparison.Ordinal));
        var channelId = cached?.ChannelId ?? await ResolveChannelIdAsync(sessionId, ct).ConfigureAwait(false);

        if (_dbContextFactory is not null && cached is null && string.IsNullOrWhiteSpace(channelId))
        {
            ClearConversation();
            ErrorMessage = "The requested session could not be found.";
            return;
        }

        CurrentSessionId = sessionId;
        CurrentChannelId = channelId ?? BuildChannelId(sessionId);
        ErrorMessage = null;

        await LoadHistoryAsync(sessionId, ct).ConfigureAwait(false);
    }

    public async Task<bool> SendAsync(CancellationToken ct = default)
    {
        EnsureReady();

        var content = ComposerText.Trim();
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        ErrorMessage = null;
        IsLoading = true;

        var sessionId = CurrentSessionId;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            sessionId = await CreateNewSessionAsync(ct).ConfigureAwait(false);
        }

        CurrentSessionId = sessionId;
        CurrentChannelId ??= BuildChannelId(sessionId!);

        var pendingMessage = CreatePendingUserMessage(content);
        Messages.Add(pendingMessage);
        TouchSession(sessionId!, CurrentChannelId, content, DateTimeOffset.UtcNow);

        try
        {
            await _agentRuntime.RunTurnAsync(new LeanKernelMessage
            {
                Content = content,
                SenderId = OwnerId!,
                ChannelId = CurrentChannelId!,
                SessionId = sessionId,
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ui_surface"] = "blazor-chat"
                }
            }, ct).ConfigureAwait(false);

            ComposerText = string.Empty;
            await LoadHistoryAsync(sessionId!, ct).ConfigureAwait(false);
            await RefreshSessionsAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Messages.Remove(pendingMessage);
            throw;
        }
        catch (Exception ex)
        {
            Messages.Remove(pendingMessage);
            ErrorMessage = "The turn could not be completed. Please try again.";
            _logger.LogError(ex, "Sending a Blazor chat turn failed for session {SessionId}", sessionId);

            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                try
                {
                    await LoadHistoryAsync(sessionId, ct).ConfigureAwait(false);
                }
                catch (Exception loadEx) when (loadEx is not OperationCanceledException)
                {
                    _logger.LogDebug(loadEx, "Refreshing chat history after a failed turn was not possible for session {SessionId}", sessionId);
                }
            }

            return false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadHistoryAsync(string sessionId, CancellationToken ct)
    {
        var turns = await _sessionStore.GetHistoryAsync(sessionId, MaxHistoryTurns, ct).ConfigureAwait(false);
        var compactionKinds = await LoadCompactionKindsAsync(sessionId, ct).ConfigureAwait(false);

        Messages.Clear();
        Messages.AddRange(turns.Select(turn => MapTurn(turn, compactionKinds)));

        var channelId = CurrentChannelId ?? await ResolveChannelIdAsync(sessionId, ct).ConfigureAwait(false);
        TouchSession(sessionId, channelId, turns.LastOrDefault()?.Content, turns.LastOrDefault()?.Timestamp ?? DateTimeOffset.UtcNow, turns);
    }

    private async Task RefreshSessionsAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(OwnerId) || _dbContextFactory is null)
        {
            return;
        }

        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

            var persistedSessions = await db.Sessions
                .AsNoTracking()
                .Where(session => session.UserId == OwnerId && session.ChannelId.StartsWith(ChannelPrefix + ":"))
                .OrderByDescending(session => session.UpdatedAt)
                .Take(24)
                .Select(session => new SessionSnapshot(
                    session.Id,
                    session.ChannelId,
                    session.CreatedAt,
                    session.UpdatedAt))
                .ToListAsync(ct)
                .ConfigureAwait(false);

            if (persistedSessions.Count == 0)
            {
                return;
            }

            var sessionIds = persistedSessions.Select(session => session.SessionId).ToArray();
            var turnSnapshots = await db.Turns
                .AsNoTracking()
                .Where(turn => sessionIds.Contains(turn.SessionId))
                .OrderByDescending(turn => turn.Timestamp)
                .ThenByDescending(turn => turn.Id)
                .Select(turn => new TurnSnapshot(
                    turn.SessionId,
                    turn.Role,
                    turn.Content,
                    turn.Timestamp,
                    turn.Id))
                .ToListAsync(ct)
                .ConfigureAwait(false);

            var latestTurnLookup = turnSnapshots
                .GroupBy(turn => turn.SessionId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            var mergedSessions = persistedSessions.Select(session =>
            {
                latestTurnLookup.TryGetValue(session.SessionId, out var latestTurn);
                return BuildSessionSummary(session, latestTurn);
            });

            ReplaceSessions(mergedSessions);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Loading Blazor chat sessions from persistence failed; using cached summaries only");
        }
    }

    private async Task<string?> ResolveChannelIdAsync(string sessionId, CancellationToken ct)
    {
        var cached = Sessions.FirstOrDefault(session => string.Equals(session.SessionId, sessionId, StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(cached?.ChannelId))
        {
            return cached.ChannelId;
        }

        if (_dbContextFactory is null)
        {
            return null;
        }

        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            return await db.Sessions
                .AsNoTracking()
                .Where(session => session.Id == sessionId
                    && session.UserId == OwnerId
                    && session.ChannelId.StartsWith(ChannelPrefix + ":"))
                .Select(session => session.ChannelId)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Resolving the channel id for session {SessionId} failed", sessionId);
            return null;
        }
    }

    private async Task<IReadOnlyDictionary<string, ChatCompactionKind>> LoadCompactionKindsAsync(string sessionId, CancellationToken ct)
    {
        if (_dbContextFactory is null)
        {
            return new Dictionary<string, ChatCompactionKind>(StringComparer.Ordinal);
        }

        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var markers = await db.CompactionMarkers
                .AsNoTracking()
                .Where(marker => marker.SessionId == sessionId && marker.CompactedContent != null)
                .OrderByDescending(marker => marker.CompactedAt)
                .Select(marker => new { marker.CompactedContent, marker.MarkerType })
                .ToListAsync(ct)
                .ConfigureAwait(false);

            return markers
                .Where(marker => !string.IsNullOrWhiteSpace(marker.CompactedContent))
                .GroupBy(marker => marker.CompactedContent!, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => ResolveCompactionKind(group.First().MarkerType),
                    StringComparer.Ordinal);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Loading compaction markers for session {SessionId} failed", sessionId);
            return new Dictionary<string, ChatCompactionKind>(StringComparer.Ordinal);
        }
    }

    private void TouchSession(
        string sessionId,
        string? channelId,
        string? preview,
        DateTimeOffset updatedAt,
        IReadOnlyList<ConversationTurn>? turns = null)
    {
        var title = BuildTitle(turns, preview);
        var summary = new ChatSessionSummary
        {
            SessionId = sessionId,
            ChannelId = channelId,
            Title = title,
            Preview = BuildPreview(preview),
            CreatedAt = Sessions.FirstOrDefault(session => string.Equals(session.SessionId, sessionId, StringComparison.Ordinal))?.CreatedAt ?? updatedAt,
            UpdatedAt = updatedAt,
            HasMessages = turns?.Count > 0 || !string.IsNullOrWhiteSpace(preview),
        };

        UpsertSession(summary);
    }

    private void ReplaceSessions(IEnumerable<ChatSessionSummary> sessions)
    {
        Sessions.Clear();
        Sessions.AddRange(sessions
            .GroupBy(session => session.SessionId, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(item => item.UpdatedAt).First())
            .OrderByDescending(session => session.UpdatedAt));
    }

    private void UpsertSession(ChatSessionSummary summary)
    {
        var existingIndex = Sessions.FindIndex(session => string.Equals(session.SessionId, summary.SessionId, StringComparison.Ordinal));
        if (existingIndex >= 0)
        {
            Sessions.RemoveAt(existingIndex);
        }

        Sessions.Add(summary);
        Sessions.Sort((left, right) => right.UpdatedAt.CompareTo(left.UpdatedAt));
    }

    private static ChatMessageViewModel CreatePendingUserMessage(string content)
        => new()
        {
            Id = $"pending-{Guid.NewGuid():N}",
            Role = "user",
            Content = content,
            Timestamp = DateTimeOffset.UtcNow,
            Status = ChatMessageStatus.Pending,
        };

    private static ChatMessageViewModel MapTurn(
        ConversationTurn turn,
        IReadOnlyDictionary<string, ChatCompactionKind> compactionKinds)
    {
        compactionKinds.TryGetValue(turn.Content, out var markerKind);
        var compactionKind = turn.IsCompacted
            ? markerKind == ChatCompactionKind.None ? ChatCompactionKind.Compacted : markerKind
            : ChatCompactionKind.None;

        return new ChatMessageViewModel
        {
            Id = turn.TurnId ?? $"{turn.Role}-{turn.Timestamp.ToUnixTimeMilliseconds()}",
            Role = turn.Role,
            Content = turn.Content,
            Timestamp = turn.Timestamp,
            IsCompacted = turn.IsCompacted,
            CompactionKind = compactionKind,
            CompactionLabel = compactionKind switch
            {
                ChatCompactionKind.Summarized => "Summarized",
                ChatCompactionKind.Compacted => "Compacted",
                _ => null,
            },
            Status = ChatMessageStatus.Persisted,
        };
    }

    private static ChatSessionSummary BuildSessionSummary(SessionSnapshot session, TurnSnapshot? latestTurn)
        => new()
        {
            SessionId = session.SessionId,
            ChannelId = session.ChannelId,
            Title = BuildTitle(null, latestTurn?.Content),
            Preview = BuildPreview(latestTurn?.Content),
            CreatedAt = session.CreatedAt,
            UpdatedAt = latestTurn?.Timestamp ?? session.UpdatedAt,
            HasMessages = latestTurn is not null,
        };

    private static ChatCompactionKind ResolveCompactionKind(string? markerType)
        => markerType?.Trim().ToLowerInvariant() switch
        {
            "summarized" => ChatCompactionKind.Summarized,
            "compacted" => ChatCompactionKind.Compacted,
            _ => ChatCompactionKind.None,
        };

    private static string BuildChannelId(string conversationKey) => $"{ChannelPrefix}:{conversationKey}";

    private static string BuildPreview(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "Start a conversation";
        }

        var singleLine = content.ReplaceLineEndings(" ").Trim();
        return singleLine.Length <= 72 ? singleLine : singleLine[..69] + "...";
    }

    private static string BuildTitle(IReadOnlyList<ConversationTurn>? turns, string? fallbackContent)
    {
        var userTurn = turns?
            .FirstOrDefault(turn => string.Equals(turn.Role, "user", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(turn.Content))
            ?.Content;

        var source = string.IsNullOrWhiteSpace(userTurn) ? fallbackContent : userTurn;
        if (string.IsNullOrWhiteSpace(source))
        {
            return "New session";
        }

        var singleLine = source.ReplaceLineEndings(" ").Trim();
        return singleLine.Length <= 36 ? singleLine : singleLine[..33] + "...";
    }

    private void ClearConversation()
    {
        CurrentSessionId = null;
        CurrentChannelId = null;
        Messages.Clear();
        ErrorMessage = null;
    }

    private void EnsureReady()
    {
        if (!IsInitialized || string.IsNullOrWhiteSpace(OwnerId))
        {
            throw new InvalidOperationException("ChatService must be initialized before use.");
        }
    }

    private sealed record SessionSnapshot(
        string SessionId,
        string ChannelId,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    private sealed record TurnSnapshot(
        string SessionId,
        string Role,
        string Content,
        DateTimeOffset Timestamp,
        string TurnId);
}

public sealed record ChatSessionSummary
{
    public required string SessionId { get; init; }
    public string? ChannelId { get; init; }
    public required string Title { get; init; }
    public required string Preview { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public bool HasMessages { get; init; }
}

public sealed record ChatMessageViewModel
{
    public required string Id { get; init; }
    public required string Role { get; init; }
    public required string Content { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public bool IsCompacted { get; init; }
    public ChatCompactionKind CompactionKind { get; init; }
    public string? CompactionLabel { get; init; }
    public ChatMessageStatus Status { get; init; }
}

public enum ChatCompactionKind
{
    None = 0,
    Compacted = 1,
    Summarized = 2,
}

public enum ChatMessageStatus
{
    Persisted = 0,
    Pending = 1,
}
