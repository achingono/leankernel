using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Enums;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Archivist;

/// <summary>
/// Retrieves and scores raw context candidates from wiki and vector knowledge stores.
/// </summary>
public sealed class ContextCandidateRetriever
{
    private readonly IWikiStore _wiki;
    private readonly IKnowledgeSearchService _knowledgeSearch;
    private readonly IReranker? _reranker;
    private readonly LeanKernelConfig _config;
    private readonly ILogger<ContextCandidateRetriever> _logger;

    // Pre-compiled keyword map for O(1) dimension classification
    private static readonly Dictionary<string, WikiDimension> DimensionKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        { "who", WikiDimension.Who }, { "person", WikiDimension.Who }, { "contact", WikiDimension.Who }, { "name", WikiDimension.Who },
        { "what", WikiDimension.What }, { "thing", WikiDimension.What }, { "event", WikiDimension.What }, { "task", WikiDimension.What },
        { "where", WikiDimension.Where }, { "place", WikiDimension.Where }, { "location", WikiDimension.Where }, { "address", WikiDimension.Where },
        { "when", WikiDimension.When }, { "time", WikiDimension.When }, { "date", WikiDimension.When }, { "schedule", WikiDimension.When },
        { "why", WikiDimension.Why }, { "reason", WikiDimension.Why }, { "cause", WikiDimension.Why }, { "because", WikiDimension.Why },
        { "how", WikiDimension.How }, { "method", WikiDimension.How }, { "step", WikiDimension.How }, { "process", WikiDimension.How }
    };

    // Shared punctuation for tokenization to reduce allocation
    private static readonly char[] Delimiters = 
        [' ', '\t', '\r', '\n', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '"', '\'', '/', '\\', '-', '_'];

    /// <summary>
    /// Initializes a new instance of the <see cref="ContextCandidateRetriever" /> class.
    /// </summary>
    /// <param name="wiki">The wiki store used for structured knowledge retrieval.</param>
    /// <param name="knowledgeSearch">The semantic knowledge search service.</param>
    /// <param name="config">The LeanKernel configuration.</param>
    /// <param name="logger">The logger used for retrieval diagnostics.</param>
    public ContextCandidateRetriever(
        IWikiStore wiki,
        IKnowledgeSearchService knowledgeSearch,
        IOptions<LeanKernelConfig> config,
        ILogger<ContextCandidateRetriever> logger,
        IReranker? reranker = null)
    {
        _wiki = wiki ?? throw new ArgumentNullException(nameof(wiki));
        _knowledgeSearch = knowledgeSearch ?? throw new ArgumentNullException(nameof(knowledgeSearch));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _reranker = reranker;
    }

    /// <summary>
    /// Retrieves wiki candidates for the supplied message and dimensions.
    /// </summary>
    /// <param name="query">The inbound user message.</param>
    /// <param name="dimensions">The active 5W1H dimensions.</param>
    /// <param name="ct">A token used to cancel retrieval.</param>
    /// <returns>The unranked wiki candidate scores.</returns>
    public async Task<List<RelevanceScore>> RetrieveWikiLeanKernelsAsync(
        LeanKernelMessage query,
        HashSet<WikiDimension> dimensions,
        IReadOnlyList<EntityHint> entityHints,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query?.Content)) return [];

        var baseEntries = await _wiki.QueryAsync(new WikiQuery
        {
            TextQuery = query.Content,
            Dimensions = dimensions,
            MaxResults = 20,
            MinConfidence = _config.Wiki.MinConfidenceThreshold
        }, ct);

        var expandedEntryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entriesById = baseEntries.ToDictionary(entry => entry.Id, StringComparer.OrdinalIgnoreCase);
        await ExpandEntityRelationsAsync(baseEntries, entityHints, entriesById, expandedEntryIds, ct);

        // Pre-tokenize query once to avoid re-tokenizing the same query for every entry in the Select loop.
        // Keep this as HashSet to preserve O(1) membership checks in lexical overlap scoring.
        var queryTokens = Tokenize(query.Content);
        var queryTokenCount = queryTokens.Count;

        return entriesById.Values.Select(e =>
        {
            var baseSimilarity = ComputeLexicalSimilarityOptimized(queryTokens, queryTokenCount, e);
            var entityBoost = ComputeEntityBoost(e, entityHints);
            var hasEntityPersonMatch = HasPersonEntityMatch(e, entityHints);
            var hasEntityOrgMatch = HasOrganizationEntityMatch(e, entityHints) || expandedEntryIds.Contains(e.Id);
            var priority = hasEntityPersonMatch
                ? ContextPriority.High
                : hasEntityOrgMatch
                    ? ContextPriority.Low
                    : ContextPriority.Medium;

            return new RelevanceScore
            {
                EntryId = e.Id,
                Content = FormatWikiEntryCompact(e),
                EstimatedTokens = Math.Max(1, e.Facts.Sum(f => f.EstimatedTokens)),
                SemanticSimilarity = Math.Clamp(baseSimilarity + entityBoost, 0.0, 1.0),
                RecencyDecay = ComputeRecencyDecay(e.LastAccessed),
                DimensionMatch = hasEntityPersonMatch && e.Dimension == WikiDimension.Who
                    ? 1.0
                    : dimensions.Contains(e.Dimension) ? 1.0 : 0.2,
                InteractionFrequency = Math.Clamp(e.AccessCount / 100.0, 0.0, 1.0),
                Priority = priority,
                Score = 0.0
            };
        }).ToList();
    }

    /// <summary>
    /// Retrieves broader wiki candidates used as a second-pass fallback for unclear queries.
    /// </summary>
    public async Task<List<RelevanceScore>> RetrieveWikiFallbackLeanKernelsAsync(
        LeanKernelMessage query,
        IReadOnlyList<EntityHint> entityHints,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query?.Content)) return [];

        var allDimensions = Enum.GetValues<WikiDimension>().ToHashSet();
        var baseEntries = await _wiki.QueryAsync(new WikiQuery
        {
            TextQuery = query.Content,
            Dimensions = allDimensions,
            MaxResults = Math.Max(20, _config.Context.DeprioritizedRecallMaxResults),
            MinConfidence = Math.Max(0.0, _config.Wiki.MinConfidenceThreshold - 0.2)
        }, ct);

        var expandedEntryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entriesById = baseEntries.ToDictionary(entry => entry.Id, StringComparer.OrdinalIgnoreCase);
        await ExpandEntityRelationsAsync(baseEntries, entityHints, entriesById, expandedEntryIds, ct);

        var queryTokens = Tokenize(query.Content);
        var queryTokenCount = queryTokens.Count;

        return entriesById.Values.Select(e =>
        {
            var baseSimilarity = ComputeLexicalSimilarityOptimized(queryTokens, queryTokenCount, e);
            var entityBoost = ComputeEntityBoost(e, entityHints);
            var hasEntityPersonMatch = HasPersonEntityMatch(e, entityHints);
            var hasEntityOrgMatch = HasOrganizationEntityMatch(e, entityHints) || expandedEntryIds.Contains(e.Id);
            var priority = hasEntityPersonMatch
                ? ContextPriority.High
                : hasEntityOrgMatch
                    ? ContextPriority.Low
                    : ContextPriority.Medium;

            return new RelevanceScore
            {
                EntryId = e.Id,
                Content = FormatWikiEntryCompact(e),
                EstimatedTokens = Math.Max(1, e.Facts.Sum(f => f.EstimatedTokens)),
                SemanticSimilarity = Math.Clamp(baseSimilarity + entityBoost, 0.0, 1.0),
                RecencyDecay = ComputeRecencyDecay(e.LastAccessed),
                DimensionMatch = hasEntityPersonMatch && e.Dimension == WikiDimension.Who
                    ? 1.0
                    : allDimensions.Contains(e.Dimension) ? 1.0 : 0.2,
                InteractionFrequency = Math.Clamp(e.AccessCount / 100.0, 0.0, 1.0),
                Priority = priority,
                Score = 0.0
            };
        }).ToList();
    }

    /// <summary>
    /// Retrieves vector-search candidates for the supplied message and allowed tags.
    /// </summary>
    /// <param name="query">The inbound user message.</param>
    /// <param name="agentTags">The allowed knowledge tags for the active agent.</param>
    /// <param name="ct">A token used to cancel retrieval.</param>
    /// <returns>The unranked vector candidate scores.</returns>
    public async Task<List<RelevanceScore>> RetrieveVectorLeanKernelsAsync(
        LeanKernelMessage query,
        IReadOnlyList<string> agentTags,
        CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query?.Content)) return [];
            
            var results = await _knowledgeSearch.SearchAsync(query.Content, agentTags, limit: 10, ct);
            var candidates = (results ?? []).ToList();
            return await ApplyRerankPolicyAsync(query.Content, candidates, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Knowledge search failed - falling back to wiki-only context");
            return [];
        }
    }

    /// <summary>
    /// Retrieves broader vector candidates used as a second-pass fallback for unclear queries.
    /// </summary>
    public async Task<List<RelevanceScore>> RetrieveVectorFallbackLeanKernelsAsync(
        LeanKernelMessage query,
        IReadOnlyList<string> agentTags,
        CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query?.Content)) return [];

            var limit = Math.Clamp(_config.Context.DeprioritizedRecallMaxResults, 10, 100);
            var results = await _knowledgeSearch.SearchAsync(query.Content, agentTags, limit, ct);
            return (results ?? [])
                .OrderByDescending(c => c.SemanticSimilarity)
                .ThenByDescending(c => c.Score)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fallback knowledge search failed");
            return [];
        }
    }

    private async Task<List<RelevanceScore>> ApplyRerankPolicyAsync(
        string query,
        List<RelevanceScore> candidates,
        CancellationToken ct)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        var policy = _config.Context.Reranker;
        var fallback = candidates
            .OrderByDescending(c => c.Score)
            .ThenByDescending(c => c.SemanticSimilarity)
            .Take(Math.Clamp(policy.TopK, 1, 50))
            .ToList();

        if (!policy.Enabled || _reranker is null)
        {
            return fallback;
        }

        var topN = Math.Clamp(policy.TopN, 1, 50);
        var topK = Math.Clamp(policy.TopK, 1, topN);
        var timeoutMs = Math.Clamp(policy.TimeoutMs, 100, 10_000);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));

        try
        {
            var reranked = await _reranker.RerankAsync(
                query,
                candidates.Take(topN).ToList(),
                timeoutCts.Token);

            return reranked
                .Where(c => c.Score >= policy.MinAcceptanceScore)
                .Take(topK)
                .ToList();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Reranker timed out after {TimeoutMs}ms; using deterministic fallback order", timeoutMs);
            return fallback;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reranker failed; using deterministic fallback order");
            return fallback;
        }
    }

    /// <summary>
    /// Classifies a query into active 5W1H dimensions using tokenized lookup.
    /// </summary>
    /// <param name="query">The query text to classify.</param>
    /// <returns>The active wiki dimensions.</returns>
    public static HashSet<WikiDimension> ClassifyDimensions(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return GetDefaultDimensions();

        var tokens = Tokenize(query);
        var dims = new HashSet<WikiDimension>();

        foreach (var token in tokens)
        {
            if (DimensionKeywords.TryGetValue(token, out var dim))
            {
                dims.Add(dim);
            }
        }

        return dims.Count > 0 ? dims : GetDefaultDimensions();
    }

    private static HashSet<WikiDimension> GetDefaultDimensions() => 
        [WikiDimension.Who, WikiDimension.What];

    private static string FormatWikiEntryCompact(WikiEntry entry) =>
        $"[{entry.Dimension}:{entry.Subject}] {string.Join("; ", entry.Facts.Select(f => f.Claim))}";

    private static double ComputeLexicalSimilarityOptimized(
        HashSet<string> queryTokens,
        int queryTokenCount,
        WikiEntry entry)
    {
        if (queryTokenCount == 0) return 0.0;

        var entryTokens = Tokenize(BuildEntrySearchSurface(entry));
        
        if (entryTokens.Count == 0) return 0.0;

        // Iterate the smaller set to minimize total membership checks.
        var iterateSet = entryTokens.Count <= queryTokenCount ? entryTokens : queryTokens;
        var lookupSet = entryTokens.Count <= queryTokenCount ? queryTokens : entryTokens;
        int overlap = CountTokenOverlap(iterateSet, lookupSet);

        return (double)overlap / queryTokenCount;
    }

    private async Task ExpandEntityRelationsAsync(
        IReadOnlyList<WikiEntry> baseEntries,
        IReadOnlyList<EntityHint> entityHints,
        Dictionary<string, WikiEntry> entriesById,
        HashSet<string> expandedEntryIds,
        CancellationToken ct)
    {
        if (_config.Context.EntityExpansionDepth <= 0 || baseEntries.Count == 0)
        {
            return;
        }

        var frontier = baseEntries
            .Where(entry => HasEntityMatch(entry, entityHints))
            .ToList();

        for (var depth = 0; depth < _config.Context.EntityExpansionDepth && frontier.Count > 0; depth++)
        {
            var nextFrontier = new List<WikiEntry>();

            foreach (var seed in frontier)
            {
                foreach (var relationId in seed.Relations)
                {
                    if (string.IsNullOrWhiteSpace(relationId) || entriesById.ContainsKey(relationId))
                    {
                        continue;
                    }

                    var related = await _wiki.GetAsync(relationId, ct);
                    if (related is null)
                    {
                        continue;
                    }

                    entriesById[related.Id] = related;
                    expandedEntryIds.Add(related.Id);
                    nextFrontier.Add(related);
                }
            }

            frontier = nextFrontier;
        }
    }

    private static double ComputeEntityBoost(WikiEntry entry, IReadOnlyList<EntityHint> entityHints)
    {
        if (entityHints.Count == 0)
        {
            return 0.0;
        }

        if (HasPersonEntityMatch(entry, entityHints))
        {
            return 0.55;
        }

        if (HasOrganizationEntityMatch(entry, entityHints))
        {
            return 0.30;
        }

        return 0.0;
    }

    private static bool HasPersonEntityMatch(WikiEntry entry, IReadOnlyList<EntityHint> entityHints)
    {
        var entryText = BuildEntrySearchSurface(entry);
        var people = entityHints.Where(h => h.Type == EntityHintType.Person);
        return people.Any(hint =>
            entryText.Contains(hint.NormalizedName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasOrganizationEntityMatch(WikiEntry entry, IReadOnlyList<EntityHint> entityHints)
    {
        var entryText = BuildEntrySearchSurface(entry);
        var organizations = entityHints.Where(h => h.Type == EntityHintType.Organization);
        return organizations.Any(hint =>
            entryText.Contains(hint.NormalizedName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasEntityMatch(WikiEntry entry, IReadOnlyList<EntityHint> entityHints)
    {
        return HasPersonEntityMatch(entry, entityHints) || HasOrganizationEntityMatch(entry, entityHints);
    }

    private static string BuildEntrySearchSurface(WikiEntry entry)
    {
        var factsText = string.Join(' ', entry.Facts.Select(f =>
            $"{f.Claim} {f.Context?.Who} {f.Context?.What} {f.Context?.When} {f.Context?.Where} {f.Context?.Why} {f.Context?.How}"));
        return $"{entry.Subject} {entry.Summary} {string.Join(' ', entry.Aliases)} {string.Join(' ', entry.Tags)} {factsText}";
    }

    private static HashSet<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        return text.ToLowerInvariant()
            .Split(Delimiters, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 2)
            .ToHashSet();
    }

    private static int CountTokenOverlap(HashSet<string> iterateSet, HashSet<string> lookupSet)
    {
        int overlap = 0;
        foreach (var token in iterateSet)
        {
            if (lookupSet.Contains(token)) overlap++;
        }

        return overlap;
    }

    private static double ComputeRecencyDecay(DateTimeOffset lastAccessed)
    {
        var daysSince = (DateTimeOffset.UtcNow - lastAccessed).TotalDays;
        // Linear decay over 90 days, clamped at 0
        return Math.Max(0.0, 1.0 - (daysSince / 90.0));
    }
}
