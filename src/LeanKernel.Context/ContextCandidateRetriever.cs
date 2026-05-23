using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Context.Retrieval;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Context;

/// <summary>
/// Retrieves context candidates from GBrain knowledge and conversation history.
/// Does NOT make admission decisions — just fetches raw candidates.
/// </summary>
public sealed class ContextCandidateRetriever
{
    private readonly IKnowledgeService _knowledge;
    private readonly IScopedKnowledgeService _scopedKnowledge;
    private readonly RetrievalScopePolicy _scopePolicy;
    private readonly ISessionStore _sessions;
    private readonly RetrievalConfig _retrievalConfig;
    private readonly ILogger<ContextCandidateRetriever> _logger;

    public ContextCandidateRetriever(
        IKnowledgeService knowledge,
        IScopedKnowledgeService scopedKnowledge,
        RetrievalScopePolicy scopePolicy,
        ISessionStore sessions,
        IOptions<RetrievalConfig> retrievalConfig,
        ILogger<ContextCandidateRetriever> logger)
    {
        _knowledge = knowledge ?? throw new ArgumentNullException(nameof(knowledge));
        _scopedKnowledge = scopedKnowledge ?? throw new ArgumentNullException(nameof(scopedKnowledge));
        _scopePolicy = scopePolicy ?? throw new ArgumentNullException(nameof(scopePolicy));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _retrievalConfig = retrievalConfig?.Value ?? throw new ArgumentNullException(nameof(retrievalConfig));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ContextCandidates> RetrieveAsync(
        LeanKernelMessage message,
        string sessionId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (string.IsNullOrWhiteSpace(message.Content))
        {
            var emptyHistory = await _sessions.GetHistoryAsync(sessionId, maxTurns: 50, ct).ConfigureAwait(false);
            return new ContextCandidates
            {
                KnowledgeCandidates = [],
                History = emptyHistory,
            };
        }

        IReadOnlyList<RetrievalCandidate> knowledgeCandidates;
        RetrievalDiagnostics? diagnostics = null;

        if (_retrievalConfig.ScopingEnabled)
        {
            var scope = _scopePolicy.ResolveScope(message);
            var scopedResult = await _scopedKnowledge.RetrieveWithScopeAsync(message.Content, scope, maxResults: 20, ct).ConfigureAwait(false);
            knowledgeCandidates = scopedResult.Candidates;
            diagnostics = scopedResult.Diagnostics with
            {
                SessionId = sessionId,
                TurnId = ResolveTurnId(message),
            };

            _logger.LogDebug(
                "Retrieved {KnowledgeCount} scoped knowledge candidates for scope {Scope}",
                knowledgeCandidates.Count,
                diagnostics.EffectiveScope);
        }
        else
        {
            knowledgeCandidates = await _knowledge.SearchAsync(message.Content, maxResults: 20, ct).ConfigureAwait(false);
        }

        var history = await _sessions.GetHistoryAsync(sessionId, maxTurns: 50, ct).ConfigureAwait(false);

        _logger.LogDebug(
            "Retrieved {KnowledgeCount} knowledge candidates and {HistoryCount} history turns",
            knowledgeCandidates.Count,
            history.Count);

        return new ContextCandidates
        {
            KnowledgeCandidates = knowledgeCandidates,
            History = history,
            RetrievalDiagnostics = diagnostics,
        };
    }

    private static string ResolveTurnId(LeanKernelMessage message)
    {
        if (message.Metadata is not null)
        {
            if (message.Metadata.TryGetValue("turn_id", out var turnId) && !string.IsNullOrWhiteSpace(turnId))
            {
                return turnId.Trim();
            }

            if (message.Metadata.TryGetValue("turnId", out turnId) && !string.IsNullOrWhiteSpace(turnId))
            {
                return turnId.Trim();
            }
        }

        return "unknown";
    }
}

/// <summary>
/// Raw candidates before admission decisions.
/// </summary>
public sealed class ContextCandidates
{
    public IReadOnlyList<RetrievalCandidate> KnowledgeCandidates { get; init; } = [];

    public IReadOnlyList<ConversationTurn> History { get; init; } = [];

    public RetrievalDiagnostics? RetrievalDiagnostics { get; init; }
}
