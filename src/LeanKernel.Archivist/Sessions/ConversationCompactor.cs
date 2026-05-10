using Microsoft.Extensions.Logging;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Archivist.Sessions;

/// <summary>
/// Compacts old conversation turns by summarizing them, then promotes
/// extracted 5W1H facts to the wiki before dropping raw messages.
/// Implements the tiered aging strategy from the plan.
/// </summary>
public sealed class ConversationCompactor
{
    private readonly ISessionStore _sessions;
    private readonly IWikiStore _wiki;
    private readonly ILogger<ConversationCompactor> _logger;

    /// <summary>
    /// Represents the conversation compactor.
    /// </summary>
    public ConversationCompactor(
        ISessionStore sessions,
        IWikiStore wiki,
        ILogger<ConversationCompactor> logger)
    {
        _sessions = sessions;
        _wiki = wiki;
        _logger = logger;
    }

    /// <summary>
    /// Compact a session: extract 5W1H facts from archived turns (16+)
    /// and persist them to the wiki, then drop the raw messages.
    /// </summary>
    public async Task CompactSessionAsync(string sessionId, CancellationToken ct)
    {
        var history = await _sessions.GetHistoryAsync(sessionId, ct);
        if (history.Count <= 16) return; // No archiving needed yet

        var archivedTurns = history.Take(history.Count - 16).ToList();
        var archivedCount = 0;

        // Process archived turns in pairs (user + assistant)
        for (var i = 0; i < archivedTurns.Count - 1; i += 2)
        {
            var userTurn = archivedTurns[i];
            var assistantTurn = i + 1 < archivedTurns.Count ? archivedTurns[i + 1] : null;

            if (userTurn.Role != "user" || assistantTurn?.Role != "assistant")
                continue;

            var sourceId = $"session:{sessionId}:{userTurn.Timestamp:yyyy-MM-ddTHH:mm:ss}";
            var entries = Wiki.WikiExtractor.ExtractFacts(
                userTurn.Content,
                assistantTurn.Content,
                sourceId);

            foreach (var entry in entries)
            {
                await _wiki.UpsertAsync(entry, ct);
            }

            archivedCount++;
        }

        // Compact the session store (remove archived turns)
        await _sessions.CompactAsync(sessionId, ct);

        _logger.LogInformation(
            "Compacted session {SessionId}: archived {Count} turn pairs → wiki",
            sessionId, archivedCount);
    }
}
