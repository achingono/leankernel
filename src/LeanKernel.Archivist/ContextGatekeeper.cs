using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Enums;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Archivist;

/// <summary>
/// The Context Gatekeeper — LeanKernel's core differentiator.
/// Deny-by-default: starts from nothing and only adds LeanKernels
/// that earn their place in the context window.
/// </summary>
public sealed class ContextGatekeeper : IContextGatekeeper
{
    private readonly ContextCandidateRetriever _candidateRetriever;
    private readonly ConversationHistoryAssembler _historyAssembler;
    private readonly ILeanKernelSelectionStrategy _selectionStrategy;
    private readonly SystemPromptBuilder _systemPromptBuilder;
    private readonly OnboardingGapDetector _onboardingGapDetector;
    private readonly EntityHintExtractor _entityHintExtractor;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly LeanKernelConfig _config;
    private readonly ILogger<ContextGatekeeper> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContextGatekeeper" /> class.
    /// </summary>
    /// <param name="wiki">The wiki store used for structured knowledge retrieval.</param>
    /// <param name="sessions">The session store used for conversation history.</param>
    /// <param name="knowledgeSearch">The semantic knowledge search service.</param>
    /// <param name="config">The LeanKernel configuration.</param>
    /// <param name="logger">The logger used for context-gating diagnostics.</param>
    /// <param name="capabilityGapStore">The optional capability-gap store used to enrich prompts.</param>
    /// <param name="systemPromptBuilder">The optional system prompt builder collaborator.</param>
    /// <param name="onboardingGapDetector">The optional onboarding gap detector collaborator.</param>
    /// <param name="selectionStrategy">The optional LeanKernel selection strategy collaborator.</param>
    /// <param name="tokenEstimator">The optional token estimator collaborator.</param>
    /// <param name="candidateRetriever">The optional context candidate retriever collaborator.</param>
    /// <param name="historyAssembler">The optional conversation history assembler collaborator.</param>
    public ContextGatekeeper(
        IWikiStore wiki,
        ISessionStore sessions,
        IKnowledgeSearchService knowledgeSearch,
        IOptions<LeanKernelConfig> config,
        ILogger<ContextGatekeeper> logger,
        ICapabilityGapStore? capabilityGapStore = null,
        SystemPromptBuilder? systemPromptBuilder = null,
        OnboardingGapDetector? onboardingGapDetector = null,
        ILeanKernelSelectionStrategy? selectionStrategy = null,
        ITokenEstimator? tokenEstimator = null,
        ContextCandidateRetriever? candidateRetriever = null,
        ConversationHistoryAssembler? historyAssembler = null)
    {
        _candidateRetriever = candidateRetriever ?? new ContextCandidateRetriever(
            wiki,
            knowledgeSearch,
            config,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ContextCandidateRetriever>.Instance);
        _historyAssembler = historyAssembler ?? new ConversationHistoryAssembler(sessions, config);
        _selectionStrategy = selectionStrategy ?? new LeanKernelSelectionStrategy(config);
        _config = config.Value;
        _logger = logger;
        _systemPromptBuilder = systemPromptBuilder ?? new SystemPromptBuilder(config, capabilityGapStore);
        _onboardingGapDetector = onboardingGapDetector ?? new OnboardingGapDetector(config);
        _entityHintExtractor = new EntityHintExtractor();
        _tokenEstimator = tokenEstimator ?? new DefaultTokenEstimator();
    }

    /// <inheritdoc />
    public async Task<ConversationContext> GateContextAsync(
        LeanKernelMessage query,
        ContextBudget budget,
        string sessionId,
        CancellationToken ct)
    {
        // Existing callers use unrestricted knowledge access unless an agent scope is supplied.
        return await GateContextAsync(query, budget, sessionId, ["*"], ct);
    }

    /// <summary>
    /// Builds a deny-by-default context window constrained to the supplied agent knowledge tags.
    /// </summary>
    /// <param name="query">The inbound user message.</param>
    /// <param name="budget">The context budget for selected knowledge and history.</param>
    /// <param name="sessionId">The conversation session identifier.</param>
    /// <param name="agentKnowledgeTags">The allowed knowledge tags for the active agent.</param>
    /// <param name="ct">A token used to cancel context assembly.</param>
    /// <returns>The assembled conversation context.</returns>
    public async Task<ConversationContext> GateContextAsync(
        LeanKernelMessage query,
        ContextBudget budget,
        string sessionId,
        IReadOnlyList<string> agentKnowledgeTags,
        CancellationToken ct)
    {
        var exclusionLog = new List<string>();

        // Assemble recent history early so entity extraction can resolve pronouns against recent turns.
        var history = await _historyAssembler.AssembleAsync(sessionId, ct);
        var entityHints = _entityHintExtractor.Extract(query.Content, history);

        // Classify intent to determine the active 5W1H dimensions.
        var activeDimensions = ClassifyDimensions(query.Content);
        if (entityHints.Any(h => h.Type == EntityHintType.Person))
        {
            activeDimensions.Add(WikiDimension.Who);
        }
        if (entityHints.Any(h => h.Type == EntityHintType.Organization))
        {
            activeDimensions.Add(WikiDimension.Where);
        }

        _logger.LogDebug("Active dimensions for query: {Dimensions}", string.Join(", ", activeDimensions));
        if (entityHints.Count > 0)
        {
            _logger.LogDebug(
                "Entity hints extracted: {Hints}",
                string.Join(", ", entityHints.Select(h => $"{h.Type}:{h.NormalizedName}({h.Confidence:F2})")));
        }

        // Retrieve candidates from both structured wiki memory and vector search.
        var wikiCandidates = await _candidateRetriever.RetrieveWikiLeanKernelsAsync(query, activeDimensions, entityHints, ct);
        var vectorCandidates = await _candidateRetriever.RetrieveVectorLeanKernelsAsync(query, agentKnowledgeTags, ct);

        // Rank each source independently against its budget slice.
        var rankedWiki = _selectionStrategy.Select(wikiCandidates, budget.WikiFactsBudget, exclusionLog).ToList();
        var rankedRetrieval = _selectionStrategy.Select(vectorCandidates, budget.RetrievalBudget, exclusionLog).ToList();

        if (ShouldRunDeprioritizedRecall(query.Content, rankedWiki, rankedRetrieval))
        {
            exclusionLog.Add("INFO: Running deprioritized fallback recall across wiki and document sources due low-confidence/unclear query.");
            var fallbackWiki = await _candidateRetriever.RetrieveWikiFallbackLeanKernelsAsync(query, entityHints, ct);
            var fallbackVector = await _candidateRetriever.RetrieveVectorFallbackLeanKernelsAsync(query, agentKnowledgeTags, ct);

            wikiCandidates = MergeCandidates(wikiCandidates, fallbackWiki);
            vectorCandidates = MergeCandidates(vectorCandidates, fallbackVector);

            rankedWiki = _selectionStrategy.Select(wikiCandidates, budget.WikiFactsBudget, exclusionLog).ToList();
            rankedRetrieval = _selectionStrategy.Select(vectorCandidates, budget.RetrievalBudget, exclusionLog).ToList();
        }

        var systemPrompt = await _systemPromptBuilder.BuildAsync(ct);
        var disambiguationHints = BuildDisambiguationHints(entityHints, rankedWiki, rankedRetrieval);

        // Prompt for identity setup only at the beginning of a session.
        var onboardingInstruction = history.Count == 1
            ? await _onboardingGapDetector.BuildInstructionAsync(ct)
            : null;

        var totalTokens = _tokenEstimator.EstimateTokens(systemPrompt)
            + rankedWiki.Sum(s => s.EstimatedTokens)
            + rankedRetrieval.Sum(s => s.EstimatedTokens)
            + history.Sum(t => _tokenEstimator.EstimateTokens(t.Content))
            + disambiguationHints.Sum(_tokenEstimator.EstimateTokens);

        _logger.LogInformation(
            "Context gated: {WikiLeanKernels} wiki, {VectorLeanKernels} vector, {Turns} turns, ~{Tokens} tokens, {Excluded} excluded",
            rankedWiki.Count, rankedRetrieval.Count, history.Count, totalTokens, exclusionLog.Count);

        return new ConversationContext
        {
            SystemPrompt = systemPrompt,
            History = history,
            WikiLeanKernels = rankedWiki,
            RetrievedLeanKernels = rankedRetrieval,
            ActiveToolNames = [],
            EstimatedTotalTokens = totalTokens,
            ExclusionLog = exclusionLog,
            OnboardingInstruction = onboardingInstruction,
            DisambiguationHints = disambiguationHints
        };
    }

    /// <summary>
    /// Classifies a query into 5W1H dimensions using the current keyword-based classifier.
    /// </summary>
    public static HashSet<WikiDimension> ClassifyDimensions(string query) => ContextCandidateRetriever.ClassifyDimensions(query);

    private List<string> BuildDisambiguationHints(
        IReadOnlyList<EntityHint> entityHints,
        IReadOnlyList<RelevanceScore> rankedWiki,
        IReadOnlyList<RelevanceScore> rankedRetrieval)
    {
        var hints = new List<string>();
        var people = entityHints.Where(h => h.Type == EntityHintType.Person).ToList();
        if (people.Count == 0 || (rankedWiki.Count == 0 && rankedRetrieval.Count == 0))
        {
            return hints;
        }

        var candidates = rankedWiki.Concat(rankedRetrieval).ToList();
        foreach (var person in people)
        {
            var matches = candidates
                .Where(w => w.Content.Contains(person.NormalizedName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(w => w.Score)
                .ToList();

            if (matches.Count <= 1)
            {
                continue;
            }

            var topScore = matches[0].Score;
            var secondScore = matches[1].Score;
            var confidenceGap = topScore - secondScore;
            if (topScore >= _config.Context.AmbiguityLowConfidenceThreshold
                && confidenceGap >= _config.Context.AmbiguityConfidenceGapThreshold)
            {
                continue;
            }

            var top = matches[0].EntryId;
            hints.Add(
                $"I found {matches.Count} plausible references for '{person.NormalizedName}' and confidence is not high enough. Ground on '{top}' as best guess and ask the user to confirm.");
        }

        return hints;
    }

    private bool ShouldRunDeprioritizedRecall(
        string queryText,
        IReadOnlyList<RelevanceScore> rankedWiki,
        IReadOnlyList<RelevanceScore> rankedRetrieval)
    {
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return false;
        }

        if (rankedWiki.Count + rankedRetrieval.Count == 0)
        {
            return true;
        }

        var topScore = Math.Max(
            rankedWiki.FirstOrDefault()?.Score ?? 0.0,
            rankedRetrieval.FirstOrDefault()?.Score ?? 0.0);

        var tokenCount = queryText
            .Split([' ', '\t', '\r', '\n', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '"', '\'', '/', '\\', '-', '_'],
                StringSplitOptions.RemoveEmptyEntries)
            .Length;

        if (topScore < _config.Context.LowConfidenceFallbackThreshold)
        {
            return true;
        }

        return tokenCount <= 6 && topScore < (_config.Context.LowConfidenceFallbackThreshold + 0.1);
    }

    private static List<RelevanceScore> MergeCandidates(
        IReadOnlyList<RelevanceScore> primary,
        IReadOnlyList<RelevanceScore> fallback)
    {
        var merged = primary.ToDictionary(
            candidate => $"{candidate.SourceType}:{candidate.EntryId}",
            candidate => candidate,
            StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in fallback)
        {
            var key = $"{candidate.SourceType}:{candidate.EntryId}";
            if (!merged.TryGetValue(key, out var existing))
            {
                merged[key] = candidate;
                continue;
            }

            var keepFallback =
                candidate.SemanticSimilarity > existing.SemanticSimilarity
                || candidate.Score > existing.Score;
            if (keepFallback)
            {
                merged[key] = candidate;
            }
        }

        return merged.Values.ToList();
    }
}
