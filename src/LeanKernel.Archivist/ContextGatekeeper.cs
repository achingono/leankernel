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

        // Classify intent to determine the active 5W1H dimensions.
        var activeDimensions = ClassifyDimensions(query.Content);
        _logger.LogDebug("Active dimensions for query: {Dimensions}", string.Join(", ", activeDimensions));

        // Retrieve candidates from both structured wiki memory and vector search.
        var wikiCandidates = await _candidateRetriever.RetrieveWikiLeanKernelsAsync(query, activeDimensions, ct);
        var vectorCandidates = await _candidateRetriever.RetrieveVectorLeanKernelsAsync(query, agentKnowledgeTags, ct);

        // Rank each source independently against its budget slice.
        var rankedWiki = _selectionStrategy.Select(wikiCandidates, budget.WikiFactsBudget, exclusionLog);
        var rankedRetrieval = _selectionStrategy.Select(vectorCandidates, budget.RetrievalBudget, exclusionLog);

        // Assemble recent history with compaction for older turns.
        var history = await _historyAssembler.AssembleAsync(sessionId, ct);

        var systemPrompt = await _systemPromptBuilder.BuildAsync(ct);

        // Prompt for identity setup only at the beginning of a session.
        var onboardingInstruction = history.Count == 1
            ? await _onboardingGapDetector.BuildInstructionAsync(ct)
            : null;

        var totalTokens = _tokenEstimator.EstimateTokens(systemPrompt)
            + rankedWiki.Sum(s => s.EstimatedTokens)
            + rankedRetrieval.Sum(s => s.EstimatedTokens)
            + history.Sum(t => _tokenEstimator.EstimateTokens(t.Content));

        _logger.LogInformation(
            "Context gated: {WikiLeanKernels} wiki, {VectorLeanKernels} vector, {Turns} turns, ~{Tokens} tokens, {Excluded} excluded",
            rankedWiki.Count, rankedRetrieval.Count, history.Count, totalTokens, exclusionLog.Count);

        return new ConversationContext
        {
            SystemPrompt = systemPrompt,
            History = history,
            WikiLeanKernels = rankedWiki.ToList(),
            RetrievedLeanKernels = rankedRetrieval.ToList(),
            ActiveToolNames = [],
            EstimatedTotalTokens = totalTokens,
            ExclusionLog = exclusionLog,
            OnboardingInstruction = onboardingInstruction
        };
    }

    /// <summary>
    /// Classifies a query into 5W1H dimensions using the current keyword-based classifier.
    /// </summary>
    public static HashSet<WikiDimension> ClassifyDimensions(string query) => ContextCandidateRetriever.ClassifyDimensions(query);
}
