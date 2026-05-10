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
    private readonly IWikiStore _wiki;
    private readonly ISessionStore _sessions;
    private readonly IKnowledgeSearchService _knowledgeSearch;
    private readonly ICapabilityGapStore? _capabilityGapStore;
    private readonly LeanKernelConfig _config;
    private readonly ILogger<ContextGatekeeper> _logger;

    private const string DefaultSystemPrompt = """
        You are an AI assistant and a user's personal agent.
        
        Your goal is to understand their needs and preferences by asking clarifying questions:
        - What would you like my name to be?
        - What is your preferred engagement model? (e.g., proactive, reactive, advisory)
        - What are your communication preferences? (tone, formality, detail level)
        - What actions should I handle autonomously vs. asking for permission?
        - What are your availability and timezone preferences?
        
        Once you understand their preferences, help them configure SELF.md and USER.md.
        Answer concisely and accurately using only the context provided.
        If you don't have enough context, ask clarifying questions rather than guessing.
        Structure important facts as Who/What/Where/When/Why/How.
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContextGatekeeper" /> class.
    /// </summary>
    /// <param name="wiki">The wiki store used for structured knowledge retrieval.</param>
    /// <param name="sessions">The session store used for conversation history.</param>
    /// <param name="knowledgeSearch">The semantic knowledge search service.</param>
    /// <param name="config">The LeanKernel configuration.</param>
    /// <param name="logger">The logger used for context-gating diagnostics.</param>
    /// <param name="capabilityGapStore">The optional capability-gap store used to enrich prompts.</param>
    public ContextGatekeeper(
        IWikiStore wiki,
        ISessionStore sessions,
        IKnowledgeSearchService knowledgeSearch,
        IOptions<LeanKernelConfig> config,
        ILogger<ContextGatekeeper> logger,
        ICapabilityGapStore? capabilityGapStore = null)
    {
        _wiki = wiki;
        _sessions = sessions;
        _knowledgeSearch = knowledgeSearch;
        _capabilityGapStore = capabilityGapStore;
        _config = config.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ConversationContext> GateContextAsync(
        LeanKernelMessage query,
        ContextBudget budget,
        string sessionId,
        CancellationToken ct)
    {
        // Default: unrestricted access for backward compatibility
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

        // Phase 1: Classify intent → determine active 5W1H dimensions
        var activeDimensions = ClassifyDimensions(query.Content);
        _logger.LogDebug("Active dimensions for query: {Dimensions}", string.Join(", ", activeDimensions));

        // Phase 2: Retrieve candidate LeanKernels from wiki + vector store
        var wikiCandidates = await RetrieveWikiLeanKernelsAsync(query, activeDimensions, ct);
        var vectorCandidates = await RetrieveVectorLeanKernelsAsync(query, agentKnowledgeTags, ct);

        // Phase 3: Competitive ranking — all candidates compete for budget
        var rankedWiki = RankLeanKernels(wikiCandidates, budget.WikiFactsBudget, exclusionLog);
        var rankedRetrieval = RankLeanKernels(vectorCandidates, budget.RetrievalBudget, exclusionLog);

        // Phase 4: Assemble conversation history (sliding window with compaction)
        var history = await AssembleHistoryAsync(sessionId, ct);

        // Phase 5: Build final context
        var systemPrompt = await BuildSystemPromptAsync(ct);

        // Phase 5a: On the very first message of a session, check for identity gaps
        var onboardingInstruction = history.Count == 1
            ? await BuildOnboardingInstructionAsync(ct)
            : null;

        var totalTokens = EstimateTokens(systemPrompt)
            + rankedWiki.Sum(s => s.EstimatedTokens)
            + rankedRetrieval.Sum(s => s.EstimatedTokens)
            + history.Sum(t => EstimateTokens(t.Content));

        _logger.LogInformation(
            "Context gated: {WikiLeanKernels} wiki, {VectorLeanKernels} vector, {Turns} turns, ~{Tokens} tokens, {Excluded} excluded",
            rankedWiki.Count, rankedRetrieval.Count, history.Count, totalTokens, exclusionLog.Count);

        return new ConversationContext
        {
            SystemPrompt = systemPrompt,
            History = history,
            WikiLeanKernels = rankedWiki,
            RetrievedLeanKernels = rankedRetrieval,
            ActiveToolNames = [], // Populated by Thinker based on intent
            EstimatedTotalTokens = totalTokens,
            ExclusionLog = exclusionLog,
            OnboardingInstruction = onboardingInstruction
        };
    }

    /// <summary>
    /// Simple keyword-based dimension classifier.
    /// Future: replace with a lightweight local classifier model.
    /// </summary>
    public static HashSet<WikiDimension> ClassifyDimensions(string query)
    {
        var dims = new HashSet<WikiDimension>();
        var lower = query.ToLowerInvariant();

        if (lower.Contains("who") || lower.Contains("person") || lower.Contains("contact") || lower.Contains("name"))
            dims.Add(WikiDimension.Who);
        if (lower.Contains("what") || lower.Contains("thing") || lower.Contains("event") || lower.Contains("task"))
            dims.Add(WikiDimension.What);
        if (lower.Contains("where") || lower.Contains("place") || lower.Contains("location") || lower.Contains("address"))
            dims.Add(WikiDimension.Where);
        if (lower.Contains("when") || lower.Contains("time") || lower.Contains("date") || lower.Contains("schedule"))
            dims.Add(WikiDimension.When);
        if (lower.Contains("why") || lower.Contains("reason") || lower.Contains("cause") || lower.Contains("because"))
            dims.Add(WikiDimension.Why);
        if (lower.Contains("how") || lower.Contains("method") || lower.Contains("step") || lower.Contains("process"))
            dims.Add(WikiDimension.How);

        // Default: if no dimension detected, include Who + What as baseline
        if (dims.Count == 0)
        {
            dims.Add(WikiDimension.Who);
            dims.Add(WikiDimension.What);
        }

        return dims;
    }

    private async Task<List<RelevanceScore>> RetrieveWikiLeanKernelsAsync(
        LeanKernelMessage query,
        HashSet<WikiDimension> dimensions,
        CancellationToken ct)
    {
        var wikiQuery = new WikiQuery
        {
            TextQuery = query.Content,
            Dimensions = dimensions,
            MaxResults = 20,
            MinConfidence = _config.Wiki.MinConfidenceThreshold
        };

        var entries = await _wiki.QueryAsync(wikiQuery, ct);

        return entries.Select(e => new RelevanceScore
        {
            EntryId = e.Id,
            Content = FormatWikiEntryCompact(e),
            EstimatedTokens = e.Facts.Sum(f => f.EstimatedTokens),
            SemanticSimilarity = ComputeLexicalSimilarity(query.Content, e),
            RecencyDecay = ComputeRecencyDecay(e.LastAccessed),
            DimensionMatch = dimensions.Contains(e.Dimension) ? 1.0 : 0.2,
            InteractionFrequency = Math.Min(e.AccessCount / 100.0, 1.0),
            Score = 0.0 // Computed during ranking
        }).ToList();
    }

    private async Task<List<RelevanceScore>> RetrieveVectorLeanKernelsAsync(
        LeanKernelMessage query,
        IReadOnlyList<string> agentTags,
        CancellationToken ct)
    {
        try
        {
            var results = await _knowledgeSearch.SearchAsync(query.Content, agentTags, limit: 10, ct);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Knowledge search failed — falling back to wiki-only context");
            return [];
        }
    }

    private List<RelevanceScore> RankLeanKernels(
        List<RelevanceScore> candidates,
        int tokenBudget,
        List<string> exclusionLog)
    {
        var cfg = _config.Context;

        // Score each candidate using source-aware scoring
        var scored = candidates.Select(c => c with
        {
            Score = c.ComputeSourceAwareScore()
        })
        .OrderByDescending(c => c.Score)
        .ToList();

        // Greedy budget fill
        var selected = new List<RelevanceScore>();
        var remainingBudget = tokenBudget;

        foreach (var LeanKernel in scored)
        {
            if (LeanKernel.Score < cfg.MinRelevanceThreshold)
            {
                exclusionLog.Add($"EXCLUDED [{LeanKernel.EntryId}]: score {LeanKernel.Score:F2} below threshold {cfg.MinRelevanceThreshold}");
                continue;
            }

            if (LeanKernel.EstimatedTokens > remainingBudget)
            {
                exclusionLog.Add($"EXCLUDED [{LeanKernel.EntryId}]: {LeanKernel.EstimatedTokens} tokens exceeds remaining budget {remainingBudget}");
                continue;
            }

            selected.Add(LeanKernel);
            remainingBudget -= LeanKernel.EstimatedTokens;
        }

        return selected;
    }

    private async Task<List<ConversationTurn>> AssembleHistoryAsync(
        string sessionId,
        CancellationToken ct)
    {
        var allTurns = await _sessions.GetHistoryAsync(sessionId, ct);
        var maxTurns = Math.Min(allTurns.Count, _config.Context.MaxConversationTurns);
        var recentTurns = allTurns.TakeLast(maxTurns).ToList();

        // Tiered aging: compact older turns
        var result = new List<ConversationTurn>();
        for (var i = 0; i < recentTurns.Count; i++)
        {
            var age = recentTurns.Count - i; // Distance from most recent
            var turn = recentTurns[i];

            if (age <= 3)
            {
                // Current: full messages
                result.Add(turn);
            }
            else if (age <= 8)
            {
                // Recent: summarize long content
                result.Add(turn with
                {
                    Content = turn.Content.Length > 500
                        ? turn.Content[..500] + "..."
                        : turn.Content,
                    IsCompacted = turn.Content.Length > 500
                });
            }
            else
            {
                // Older: single-line summaries
                result.Add(turn with
                {
                    Content = Truncate(turn.Content, 100),
                    IsCompacted = true
                });
            }
        }

        return result;
    }

    private async Task<string> BuildSystemPromptAsync(CancellationToken ct)
    {
        var agentDir = Path.Combine(_config.Agents.BasePath, "main");
        var soulPath = Path.Combine(agentDir, "SELF.md");
        var userPath = Path.Combine(agentDir, "USER.md");

        var soulContent = File.Exists(soulPath)
            ? await File.ReadAllTextAsync(soulPath, ct)
            : null;

        var userContent = File.Exists(userPath)
            ? await File.ReadAllTextAsync(userPath, ct)
            : null;

        if (soulContent is null && userContent is null)
        {
            return DefaultSystemPrompt;
        }

        var sb = new System.Text.StringBuilder();

        if (soulContent is not null)
        {
            sb.AppendLine(soulContent);
        }
        else
        {
            sb.AppendLine(DefaultSystemPrompt);
        }

        if (userContent is not null)
        {
            sb.AppendLine();
            sb.AppendLine(userContent);
        }

        if (_capabilityGapStore is not null)
        {
            var capabilityGaps = await _capabilityGapStore.ReadPromptSectionAsync(ct);
            if (!string.IsNullOrWhiteSpace(capabilityGaps))
            {
                sb.AppendLine();
                sb.AppendLine(capabilityGaps);
            }
        }

        return sb.ToString().Trim();
    }

    private async Task<string?> BuildOnboardingInstructionAsync(CancellationToken ct)
    {
        var agentDir = Path.Combine(_config.Agents.BasePath, "main");
        var soulPath = Path.Combine(agentDir, "SELF.md");
        var userPath = Path.Combine(agentDir, "USER.md");

        var soulContent = File.Exists(soulPath) ? await File.ReadAllTextAsync(soulPath, ct) : null;
        var userContent = File.Exists(userPath) ? await File.ReadAllTextAsync(userPath, ct) : null;

        var gaps = new List<string>();

        // Check SELF.md for gaps
        if (string.IsNullOrWhiteSpace(soulContent) || soulContent.Contains("TODO") || soulContent.Length < 100)
        {
            gaps.Add("agent identity (name, role, personality, capabilities, communication style)");
        }

        // Check USER.md for gaps
        if (string.IsNullOrWhiteSpace(userContent) || userContent.Contains("TODO") || userContent.Length < 50)
        {
            gaps.Add("user preferences (name, timezone, communication preferences, goals)");
        }
        else
        {
            // Check for specific missing sections
            if (!userContent.Contains("name", StringComparison.OrdinalIgnoreCase) &&
                !userContent.Contains("who", StringComparison.OrdinalIgnoreCase))
                gaps.Add("the user's name");

            if (!userContent.Contains("timezone", StringComparison.OrdinalIgnoreCase) &&
                !userContent.Contains("location", StringComparison.OrdinalIgnoreCase))
                gaps.Add("the user's timezone or location");
        }

        if (gaps.Count == 0)
            return null;

        return $"""
            IMPORTANT: This is the first message in a new conversation session.
            Before answering the user's question, briefly introduce yourself and ask 1-2 focused questions
            to fill in missing information about: {string.Join(", ", gaps)}.
            Keep it conversational and natural — don't make it feel like a form.
            After gathering this context, answer their question.
            Once you have gathered the information, output it as a structured block at the end of your response:
            [IDENTITY_UPDATE]
            <the gathered facts as key: value pairs>
            [/IDENTITY_UPDATE]
            """;
    }

    private static string FormatWikiEntryCompact(WikiEntry entry) =>
        $"[{entry.Dimension}:{entry.Subject}] " +
        string.Join("; ", entry.Facts.Select(f => f.Claim));

    private static double ComputeLexicalSimilarity(string queryText, WikiEntry entry)
    {
        var queryTokens = Tokenize(queryText);
        if (queryTokens.Count == 0)
            return 0.0;

        var entryText = $"{entry.Subject} {string.Join(' ', entry.Facts.Select(f => f.Claim))}";
        var entryTokens = Tokenize(entryText);
        if (entryTokens.Count == 0)
            return 0.0;

        var overlap = queryTokens.Count(token => entryTokens.Contains(token));
        return overlap / (double)queryTokens.Count;
    }

    private static HashSet<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        return text
            .ToLowerInvariant()
            .Split([' ', '\t', '\r', '\n', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '"', '\'', '/', '\\', '-', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length >= 2)
            .ToHashSet();
    }

    private static double ComputeRecencyDecay(DateTimeOffset lastAccessed)
    {
        var daysSince = (DateTimeOffset.UtcNow - lastAccessed).TotalDays;
        return Math.Max(0.0, 1.0 - (daysSince / 90.0)); // Decays to 0 over 90 days
    }

    private static int EstimateTokens(string text) =>
        (int)Math.Ceiling(text.Length / 4.0); // ~4 chars per token approximation

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "...";
}
