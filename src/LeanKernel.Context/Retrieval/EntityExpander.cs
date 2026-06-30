using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Context.Retrieval;

/// <summary>
/// Discovers related entities for a query and returns bounded expansion results.
/// </summary>
public sealed class EntityExpander(
    IKnowledgeService knowledge,
    IOptions<RetrievalConfig> retrievalConfig,
    IOptions<ContextConfig> contextConfig,
    ILogger<EntityExpander> logger)
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "about", "after", "before", "could", "from", "have", "into", "need", "please", "show", "status",
        "tell", "that", "their", "there", "these", "this", "update", "what", "when", "where", "which", "with"
    };

    private readonly IKnowledgeService _knowledge = knowledge ?? throw new ArgumentNullException(nameof(knowledge));
    private readonly RetrievalConfig _retrievalConfig = (retrievalConfig ?? throw new ArgumentNullException(nameof(retrievalConfig))).Value;
    private readonly ContextConfig _contextConfig = (contextConfig ?? throw new ArgumentNullException(nameof(contextConfig))).Value;
    private readonly ILogger<EntityExpander> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Expands deterministic entity references from the supplied query and seed candidates.
    /// </summary>
    /// <param name="query">The user query.</param>
    /// <param name="seedCandidates">The initial retrieval candidates.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The bounded entity expansion result.</returns>
    public async Task<EntityExpansionResult> ExpandAsync(
        string query,
        IReadOnlyList<RetrievalCandidate> seedCandidates,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(seedCandidates);

        if (_retrievalConfig.MaxEntityExpansionResults <= 0)
        {
            return new EntityExpansionResult();
        }

        var expandedCandidates = new Dictionary<string, RetrievalCandidate>(StringComparer.OrdinalIgnoreCase);
        var boostedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var expandedEntities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var searchedTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visitedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string Key, int Depth)>();

        foreach (var entity in ExtractEntityTerms(query, seedCandidates))
        {
            if (expandedEntities.Add(entity))
            {
                await CollectSearchResultsAsync(entity, expandedCandidates, boostedKeys, expandedEntities, searchedTerms, ct).ConfigureAwait(false);
            }
        }

        foreach (var seed in SortCandidates(seedCandidates).Take(_retrievalConfig.MaxEntityExpansionResults))
        {
            if (visitedKeys.Add(seed.Key))
            {
                boostedKeys.Add(seed.Key);
                queue.Enqueue((seed.Key, 0));
            }
        }

        while (queue.Count > 0 && expandedCandidates.Count < _retrievalConfig.MaxEntityExpansionResults)
        {
            var (key, depth) = queue.Dequeue();
            if (depth >= _contextConfig.EntityExpansionDepth)
            {
                continue;
            }

            var page = await _knowledge.GetPageAsync(key, ct).ConfigureAwait(false);
            if (page?.LinkedPages is null || page.LinkedPages.Count == 0)
            {
                continue;
            }

            await ProcessLinkedPagesAsync(page, depth, queue, expandedCandidates, boostedKeys, expandedEntities, visitedKeys, searchedTerms, ct);
        }

        _logger.LogDebug(
            "Expanded {EntityCount} entities and discovered {CandidateCount} related candidates",
            expandedEntities.Count,
            expandedCandidates.Count);

        return new EntityExpansionResult
        {
            ExpandedCandidates = SortCandidates(expandedCandidates.Values),
            ExpandedEntities = expandedEntities.OrderBy(entity => entity, StringComparer.OrdinalIgnoreCase).ToList(),
            BoostedCandidateKeys = boostedKeys
        };
    }

    private async Task CollectSearchResultsAsync(
        string searchTerm,
        IDictionary<string, RetrievalCandidate> expandedCandidates,
        ISet<string> boostedKeys,
        ISet<string> expandedEntities,
        ISet<string> searchedTerms,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(searchTerm) || !searchedTerms.Add(searchTerm))
        {
            return;
        }

        var remaining = _retrievalConfig.MaxEntityExpansionResults - expandedCandidates.Count;
        if (remaining <= 0)
        {
            return;
        }

        var searchResults = await _knowledge.SearchAsync(searchTerm, remaining, ct).ConfigureAwait(false);
        foreach (var candidate in SortCandidates(searchResults))
        {
            if (expandedCandidates.Count >= _retrievalConfig.MaxEntityExpansionResults)
            {
                break;
            }

            expandedCandidates[candidate.Key] = candidate;
            boostedKeys.Add(candidate.Key);

            if (TryGetMetadataValue(candidate.Metadata, "subject", out var subject))
            {
                expandedEntities.Add(subject);
            }

            var keyEntity = ExtractKeyEntity(candidate.Key);
            if (!string.IsNullOrWhiteSpace(keyEntity))
            {
                expandedEntities.Add(keyEntity);
            }
        }
    }

    private async Task ProcessLinkedPagesAsync(
        KnowledgePage page,
        int depth,
        Queue<(string Key, int Depth)> queue,
        IDictionary<string, RetrievalCandidate> expandedCandidates,
        ISet<string> boostedKeys,
        ISet<string> expandedEntities,
        HashSet<string> visitedKeys,
        HashSet<string> searchedTerms,
        CancellationToken ct)
    {
        var linkedPages = page.LinkedPages;
        if (linkedPages is null || linkedPages.Count == 0)
        {
            return;
        }

        foreach (var linkedKey in linkedPages
            .Where(link => !string.IsNullOrWhiteSpace(link))
            .OrderBy(link => link, StringComparer.Ordinal))
        {
            boostedKeys.Add(linkedKey);

            var entityName = ExtractKeyEntity(linkedKey);
            if (!string.IsNullOrWhiteSpace(entityName))
            {
                expandedEntities.Add(entityName);
                await CollectSearchResultsAsync(entityName, expandedCandidates, boostedKeys, expandedEntities, searchedTerms, ct).ConfigureAwait(false);
            }

            if (visitedKeys.Add(linkedKey))
            {
                queue.Enqueue((linkedKey, depth + 1));
            }

            if (expandedCandidates.Count >= _retrievalConfig.MaxEntityExpansionResults)
            {
                break;
            }
        }
    }

    private IEnumerable<string> ExtractEntityTerms(string query, IReadOnlyList<RetrievalCandidate> seedCandidates)
    {
        var entities = new List<string>();

        foreach (var token in query.Split([' ', '\t', '\r', '\n', ',', '.', ';', ':', '!', '?', '"', '\'', '(', ')', '[', ']'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.Length < 4 || StopWords.Contains(token))
            {
                continue;
            }

            entities.Add(token);
        }

        foreach (var candidate in SortCandidates(seedCandidates).Take(_retrievalConfig.MaxEntityExpansionResults))
        {
            if (TryGetMetadataValue(candidate.Metadata, "subject", out var subject))
            {
                entities.Add(subject);
            }

            var keyEntity = ExtractKeyEntity(candidate.Key);
            if (!string.IsNullOrWhiteSpace(keyEntity))
            {
                entities.Add(keyEntity);
            }
        }

        return entities
            .Where(entity => !string.IsNullOrWhiteSpace(entity))
            .Select(entity => entity.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(_retrievalConfig.MaxEntityExpansionResults)
            .ToList();
    }

    private static IReadOnlyList<RetrievalCandidate> SortCandidates(IEnumerable<RetrievalCandidate> candidates)
        => candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Source, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Key, StringComparer.Ordinal)
            .ToList();

    private static bool TryGetMetadataValue(
        IReadOnlyDictionary<string, string>? metadata,
        string key,
        out string value)
    {
        value = string.Empty;

        if (metadata is null || !metadata.TryGetValue(key, out var rawValue) || string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        value = rawValue.Trim();
        return true;
    }

    private static string? ExtractKeyEntity(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var segments = key.Split(['/', ':'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length == 0 ? null : segments[^1];
    }
}

/// <summary>
/// Represents the related entities and candidates discovered during expansion.
/// </summary>
public sealed record EntityExpansionResult
{
    /// <summary>
    /// Gets the additional candidates discovered during expansion.
    /// </summary>
    public IReadOnlyList<RetrievalCandidate> ExpandedCandidates { get; init; } = [];

    /// <summary>
    /// Gets the entity names discovered during expansion.
    /// </summary>
    public IReadOnlyList<string> ExpandedEntities { get; init; } = [];

    /// <summary>
    /// Gets the candidate keys that should receive an entity-aware score boost.
    /// </summary>
    public IReadOnlySet<string> BoostedCandidateKeys { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
