using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Context.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Context;

/// <summary>
/// The Context Gatekeeper — LeanKernel's core differentiator.
/// Deny-by-default: starts from an empty context window and only admits
/// candidates that earn their place through relevance scoring and budget availability.
/// </summary>
public sealed class ContextGatekeeper : IContextGatekeeper
{
    private const string DefaultSystemPrompt =
        "You are LeanKernel, a personal AI assistant. " +
        "Be helpful, concise, and accurate. " +
        "If you don't know something, say so rather than guessing.";

    private readonly ContextCandidateRetriever _candidateRetriever;
    private readonly ConversationHistoryAssembler _historyAssembler;
    private readonly PromptAssembler _promptAssembler;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly IIdentityProvider _identityProvider;
    private readonly IOnboardingDetector _onboardingDetector;
    private readonly OnboardingDirectiveBuilder _onboardingDirectiveBuilder;
    private readonly ContextConfig _config;
    private readonly IdentityConfig _identityConfig;
    private readonly ILogger<ContextGatekeeper> _logger;

    public ContextGatekeeper(
        ContextCandidateRetriever candidateRetriever,
        ConversationHistoryAssembler historyAssembler,
        PromptAssembler promptAssembler,
        ITokenEstimator tokenEstimator,
        IOptions<ContextConfig> config,
        ILogger<ContextGatekeeper> logger,
        IIdentityProvider? identityProvider = null,
        IOnboardingDetector? onboardingDetector = null,
        OnboardingDirectiveBuilder? onboardingDirectiveBuilder = null,
        IOptions<IdentityConfig>? identityConfig = null)
    {
        _candidateRetriever = candidateRetriever ?? throw new ArgumentNullException(nameof(candidateRetriever));
        _historyAssembler = historyAssembler ?? throw new ArgumentNullException(nameof(historyAssembler));
        _promptAssembler = promptAssembler ?? throw new ArgumentNullException(nameof(promptAssembler));
        _tokenEstimator = tokenEstimator ?? throw new ArgumentNullException(nameof(tokenEstimator));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _identityConfig = identityConfig?.Value ?? new IdentityConfig();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _identityProvider = identityProvider ?? NoOpIdentityProvider.Instance;
        _onboardingDetector = onboardingDetector ?? NoOpOnboardingDetector.Instance;
        _onboardingDirectiveBuilder = onboardingDirectiveBuilder ?? new OnboardingDirectiveBuilder(Options.Create(_identityConfig));
    }

    public async Task<ConversationContext> GateContextAsync(
        LeanKernelMessage message,
        ContextBudget budget,
        string sessionId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        _logger.LogDebug(
            "Gating context for session {SessionId}, budget {TotalTokens} tokens, response headroom ratio {HeadroomRatio}",
            sessionId,
            budget.TotalTokens,
            _config.ResponseHeadroomRatio);

        var identity = await _identityProvider.LoadIdentityAsync(message.SenderId, ct).ConfigureAwait(false);
        var onboarding = await _onboardingDetector.DetectGapsAsync(identity, ct).ConfigureAwait(false);
        if (onboarding.HasGaps && identity.OverallConfidence < _identityConfig.OnboardingConfidenceThreshold)
        {
            onboarding = onboarding with
            {
                OnboardingDirective = _onboardingDirectiveBuilder.BuildDirective(onboarding),
            };
        }

        var candidates = await _candidateRetriever.RetrieveAsync(message, sessionId, ct).ConfigureAwait(false);
        var historyResult = await _historyAssembler.AssembleAsync(sessionId, candidates.History, budget.ConversationBudget, ct).ConfigureAwait(false);
        var history = historyResult.History;
        var (wikiFacts, retrievedKnowledge, admissionLog) = AdmitKnowledge(
            candidates.KnowledgeCandidates,
            budget.WikiFactsBudget + budget.RetrievalBudget);

        var systemPromptTokens = EstimateSystemPromptTokens(identity, onboarding);
        var wikiTokens = wikiFacts.Sum(f => f.TokenCount);
        var retrievalTokens = retrievedKnowledge.Sum(r => r.TokenCount);
        var historyTokens = historyResult.Diagnostics.TotalTokensAfter;

        var budgetUsage = new ContextBudgetUsage
        {
            SystemPromptUsed = systemPromptTokens,
            WikiFactsUsed = wikiTokens,
            RetrievalUsed = retrievalTokens,
            ConversationUsed = historyTokens,
            ToolsUsed = 0,
        };

        var context = new ConversationContext
        {
            SystemPrompt = DefaultSystemPrompt,
            SessionId = sessionId,
            History = history,
            WikiFacts = wikiFacts,
            RetrievedKnowledge = retrievedKnowledge,
            Identity = identity,
            Onboarding = onboarding,
            ActiveToolNames = [],
            BudgetUsage = budgetUsage,
            AdmissionLog = admissionLog,
            HistoryDiagnostics = historyResult.Diagnostics,
            RetrievalDiagnostics = candidates.RetrievalDiagnostics,
        };

        var manifest = _promptAssembler.AssembleSystemMessage(context);

        _logger.LogInformation(
            "Context gated: {WikiFacts} wiki facts, {Retrieved} retrieved, {History} history turns, {IdentitySegments} identity segments, {TotalTokens} total tokens, manifest length {ManifestLength}",
            wikiFacts.Count,
            retrievedKnowledge.Count,
            history.Count,
            identity.PromptSegments.Count,
            budgetUsage.TotalUsed,
            manifest.Length);

        return context;
    }

    private int EstimateSystemPromptTokens(IdentityContext identity, OnboardingResult onboarding)
    {
        var total = _tokenEstimator.EstimateTokens(DefaultSystemPrompt);
        total += identity.PromptSegments.Sum(_tokenEstimator.EstimateTokens);

        if (!string.IsNullOrWhiteSpace(onboarding.OnboardingDirective))
        {
            total += _tokenEstimator.EstimateTokens(onboarding.OnboardingDirective);
        }

        return total;
    }

    private (List<RetrievalCandidate> Wiki, List<RetrievalCandidate> Retrieved, List<ContextAdmissionRecord> Log) AdmitKnowledge(
        IReadOnlyList<RetrievalCandidate> candidates,
        int totalBudget)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var wiki = new List<RetrievalCandidate>();
        var retrieved = new List<RetrievalCandidate>();
        var log = new List<ContextAdmissionRecord>();
        var usedTokens = 0;

        var sorted = candidates
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Source, StringComparer.Ordinal)
            .ThenBy(c => c.Key, StringComparer.Ordinal)
            .ToList();

        foreach (var candidate in sorted)
        {
            var tokenCost = candidate.TokenCount > 0
                ? candidate.TokenCount
                : _tokenEstimator.EstimateTokens(candidate.Content);

            if (usedTokens + tokenCost > totalBudget)
            {
                log.Add(new ContextAdmissionRecord
                {
                    Key = candidate.Key,
                    Source = candidate.Source,
                    Score = candidate.Score,
                    TokenCount = tokenCost,
                    Admitted = false,
                    ExclusionReason = "BudgetExhausted",
                });
                continue;
            }

            if (candidate.Score < 0.1)
            {
                log.Add(new ContextAdmissionRecord
                {
                    Key = candidate.Key,
                    Source = candidate.Source,
                    Score = candidate.Score,
                    TokenCount = tokenCost,
                    Admitted = false,
                    ExclusionReason = "LowRelevanceScore",
                });
                continue;
            }

            usedTokens += tokenCost;

            if (string.Equals(candidate.Source, "wiki", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.Source, "gbrain_wiki", StringComparison.OrdinalIgnoreCase))
            {
                wiki.Add(candidate with { TokenCount = tokenCost });
            }
            else
            {
                retrieved.Add(candidate with { TokenCount = tokenCost });
            }

            log.Add(new ContextAdmissionRecord
            {
                Key = candidate.Key,
                Source = candidate.Source,
                Score = candidate.Score,
                TokenCount = tokenCost,
                Admitted = true,
            });
        }

        _logger.LogDebug(
            "Knowledge admission: {Admitted}/{Total} candidates admitted, {Tokens}/{Budget} tokens used",
            log.Count(entry => entry.Admitted),
            sorted.Count,
            usedTokens,
            totalBudget);

        return (wiki, retrieved, log);
    }

    private sealed class NoOpIdentityProvider : IIdentityProvider
    {
        public static NoOpIdentityProvider Instance { get; } = new();

        public Task<IdentityContext> LoadIdentityAsync(string userId, CancellationToken ct = default)
            => Task.FromResult(new IdentityContext
            {
                UserId = userId,
            });
    }

    private sealed class NoOpOnboardingDetector : IOnboardingDetector
    {
        public static NoOpOnboardingDetector Instance { get; } = new();

        public Task<OnboardingResult> DetectGapsAsync(IdentityContext identity, CancellationToken ct = default)
            => Task.FromResult(new OnboardingResult());
    }
}
