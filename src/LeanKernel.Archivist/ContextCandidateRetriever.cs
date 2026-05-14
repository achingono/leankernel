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
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query?.Content)) return [];

        var entries = await _wiki.QueryAsync(new WikiQuery
        {
            TextQuery = query.Content,
            Dimensions = dimensions,
            MaxResults = 20,
            MinConfidence = _config.Wiki.MinConfidenceThreshold
        }, ct);

        // Pre-tokenize query once to avoid re-tokenizing the same query for every entry in the Select loop.
        // Keep this as HashSet to preserve O(1) membership checks in lexical overlap scoring.
        var queryTokens = Tokenize(query.Content);
        var queryTokenCount = queryTokens.Count;

        return entries.Select(e => new RelevanceScore
        {
            EntryId = e.Id,
            Content = FormatWikiEntryCompact(e),
            EstimatedTokens = e.Facts.Sum(f => f.EstimatedTokens),
            SemanticSimilarity = ComputeLexicalSimilarityOptimized(queryTokens, queryTokenCount, e),
            RecencyDecay = ComputeRecencyDecay(e.LastAccessed),
            DimensionMatch = dimensions.Contains(e.Dimension) ? 1.0 : 0.2,
            InteractionFrequency = Math.Clamp(e.AccessCount / 100.0, 0.0, 1.0),
            Score = 0.0
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

        // Combine subject and facts for context
        var entryText = $"{entry.Subject} {string.Join(' ', entry.Facts.Select(f => f.Claim))}";
        var entryTokens = Tokenize(entryText);
        
        if (entryTokens.Count == 0) return 0.0;

        // Iterate the smaller set to minimize total membership checks.
        var iterateSet = entryTokens.Count <= queryTokenCount ? entryTokens : queryTokens;
        var lookupSet = entryTokens.Count <= queryTokenCount ? queryTokens : entryTokens;
        int overlap = CountTokenOverlap(iterateSet, lookupSet);

        return (double)overlap / queryTokenCount;
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
