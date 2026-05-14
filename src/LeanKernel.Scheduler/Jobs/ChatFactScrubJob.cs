using Microsoft.Extensions.Logging;
using LeanKernel.Archivist.Wiki;
using LeanKernel.Core.Interfaces;

namespace LeanKernel.Scheduler.Jobs;

/// <summary>
/// Nightly backfill that re-scrubs newly added chat turns for facts and ingests them into the wiki.
/// Uses per-session checkpoints plus a global last-run marker for incremental processing.
/// </summary>
public sealed class ChatFactScrubJob
{
    private const string SystemSessionId = "__system_jobs";
    private const string LastRunKey = "chat-fact-scrub:last-run-utc";
    private const string SessionCheckpointKey = "chat-fact-scrub:last-processed-utc";

    private readonly ISessionStore _sessions;
    private readonly IWikiStore _wiki;
    private readonly IWikiFactExtractor _extractor;
    private readonly WikiFactMapper _mapper;
    private readonly ILogger<ChatFactScrubJob> _logger;

    /// <summary>
    /// Represents the chat fact scrub job.
    /// </summary>
    public ChatFactScrubJob(
        ISessionStore sessions,
        IWikiStore wiki,
        IWikiFactExtractor extractor,
        WikiFactMapper mapper,
        ILogger<ChatFactScrubJob> logger)
    {
        _sessions = sessions;
        _wiki = wiki;
        _extractor = extractor;
        _mapper = mapper;
        _logger = logger;
    }

    /// <summary>
    /// Executes the execute async operation.
    /// </summary>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task ExecuteAsync(CancellationToken ct)
    {
        var runStartedAt = DateTimeOffset.UtcNow;
        var sessionsScanned = 0;
        var sessionsProcessed = 0;
        var turnsProcessed = 0;
        var factsIngested = 0;

        var lastRun = await GetTimestampMetadataAsync(SystemSessionId, LastRunKey, ct);
        var sessionIds = await _sessions.ListSessionsAsync(ct);

        foreach (var sessionId in sessionIds)
        {
            sessionsScanned++;

            try
            {
                var history = await _sessions.GetHistoryAsync(sessionId, ct);
                if (history.Count == 0)
                    continue;

                var checkpoint = await GetTimestampMetadataAsync(sessionId, SessionCheckpointKey, ct)
                    ?? lastRun
                    ?? DateTimeOffset.MinValue;

                var newTurns = history
                    .Where(turn => turn.Timestamp > checkpoint)
                    .OrderBy(turn => turn.Timestamp)
                    .ToList();

                if (newTurns.Count == 0)
                    continue;

                var extractedEntries = new List<LeanKernel.Core.Models.WikiEntry>();
                for (var i = 0; i < newTurns.Count; i++)
                {
                    var userTurn = newTurns[i];
                    if (!string.Equals(userTurn.Role, "user", StringComparison.OrdinalIgnoreCase))
                        continue;

                    LeanKernel.Core.Models.ConversationTurn? assistantTurn = null;
                    for (var j = i + 1; j < newTurns.Count; j++)
                    {
                        if (string.Equals(newTurns[j].Role, "assistant", StringComparison.OrdinalIgnoreCase))
                        {
                            assistantTurn = newTurns[j];
                            break;
                        }
                    }

                    var sourceId = $"scrub:{sessionId}:{userTurn.Timestamp:O}";
                    var extractedFacts = await _extractor.ExtractAsync(
                        userTurn.Content,
                        assistantTurn?.Content ?? string.Empty,
                        sourceId,
                        ct);
                    var facts = _mapper.Map(extractedFacts, sourceId);

                    if (facts.Count > 0)
                        extractedEntries.AddRange(facts);
                }

                if (extractedEntries.Count > 0)
                {
                    await _wiki.IngestFactsAsync(extractedEntries, ct);
                    factsIngested += extractedEntries.Sum(entry => entry.Facts.Count);
                }

                turnsProcessed += newTurns.Count;
                sessionsProcessed++;

                var latestTurnTimestamp = newTurns.Max(turn => turn.Timestamp);
                await _sessions.SetMetadataAsync(
                    sessionId,
                    SessionCheckpointKey,
                    latestTurnTimestamp.ToString("O"),
                    ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Chat fact scrub failed for session {SessionId}", sessionId);
            }
        }

        await _sessions.SetMetadataAsync(
            SystemSessionId,
            LastRunKey,
            runStartedAt.ToString("O"),
            ct);

        _logger.LogInformation(
            "Chat fact scrub completed: scanned {Scanned} sessions, processed {Processed}, turns {Turns}, facts {Facts}",
            sessionsScanned,
            sessionsProcessed,
            turnsProcessed,
            factsIngested);
    }

    private async Task<DateTimeOffset?> GetTimestampMetadataAsync(string sessionId, string key, CancellationToken ct)
    {
        var raw = await _sessions.GetMetadataAsync(sessionId, key, ct);
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (DateTimeOffset.TryParse(raw, out var parsed))
            return parsed;

        return null;
    }
}
