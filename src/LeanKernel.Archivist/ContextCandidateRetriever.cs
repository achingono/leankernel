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
    private readonly LeanKernelConfig _config;
    private readonly ILogger<ContextCandidateRetriever> _logger;

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
        ILogger<ContextCandidateRetriever> logger)
    {
        _wiki = wiki;
        _knowledgeSearch = knowledgeSearch;
        _config = config.Value;
        _logger = logger;
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
        var entries = await _wiki.QueryAsync(new WikiQuery
        {
            TextQuery = query.Content,
            Dimensions = dimensions,
            MaxResults = 20,
            MinConfidence = _config.Wiki.MinConfidenceThreshold
        }, ct);

        return entries.Select(e => new RelevanceScore
        {
            EntryId = e.Id,
            Content = FormatWikiEntryCompact(e),
            EstimatedTokens = e.Facts.Sum(f => f.EstimatedTokens),
            SemanticSimilarity = ComputeLexicalSimilarity(query.Content, e),
            RecencyDecay = ComputeRecencyDecay(e.LastAccessed),
            DimensionMatch = dimensions.Contains(e.Dimension) ? 1.0 : 0.2,
            InteractionFrequency = Math.Min(e.AccessCount / 100.0, 1.0),
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
            return (await _knowledgeSearch.SearchAsync(query.Content, agentTags, limit: 10, ct)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Knowledge search failed - falling back to wiki-only context");
            return [];
        }
    }

    /// <summary>
    /// Classifies a query into active 5W1H dimensions.
    /// </summary>
    /// <param name="query">The query text to classify.</param>
    /// <returns>The active wiki dimensions.</returns>
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

        if (dims.Count == 0)
        {
            dims.Add(WikiDimension.Who);
            dims.Add(WikiDimension.What);
        }

        return dims;
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
        return Math.Max(0.0, 1.0 - (daysSince / 90.0));
    }
}
