using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Archivist.Knowledge;

/// <summary>
/// Read-only search against the unified LEANKERNEL_knowledge Qdrant collection.
/// Supports agent-scoped tag filtering. All writes are handled by the external sidecar indexer.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class KnowledgeSearchService : IKnowledgeSearchService
{
    private readonly QdrantClient _qdrant;
    private readonly IEmbeddingService _embeddings;
    private readonly KnowledgeConfig _config;
    private readonly ILogger<KnowledgeSearchService> _logger;

    /// <summary>
    /// Represents the knowledge search service.
    /// </summary>
    public KnowledgeSearchService(
        IEmbeddingService embeddings,
        IOptions<LeanKernelConfig> config,
        ILogger<KnowledgeSearchService> logger)
    {
        _config = config.Value.Knowledge;
        _qdrant = new QdrantClient(config.Value.Qdrant.Host, config.Value.Qdrant.Port);
        _embeddings = embeddings;
        _logger = logger;
    }

    /// <summary>
    /// Represents the search async.
    /// </summary>
    public async Task<List<RelevanceScore>> SearchAsync(
        string query,
        IReadOnlyList<string> agentTags,
        int limit,
        CancellationToken ct,
        string? sourceType = null)
    {
        if (!_config.Enabled) return [];

        limit = Math.Clamp(limit, 1, 50);

        try
        {
            var embedding = await _embeddings.EmbedAsync(query, ct);
            var collections = ResolveCollections(sourceType);
            var filter = BuildScopeFilter(agentTags, sourceType);
            var aggregate = new List<RelevanceScore>();

            foreach (var collection in collections)
            {
                var exists = await _qdrant.CollectionExistsAsync(collection, ct);
                if (!exists)
                {
                    continue;
                }

                var results = await _qdrant.SearchAsync(
                    collection,
                    embedding,
                    filter: filter,
                    limit: (ulong)limit,
                    cancellationToken: ct);

                aggregate.AddRange(results.Select(r => ToRelevanceScore(r, sourceType)));
            }

            return aggregate
                .OrderByDescending(r => r.Score)
                .ThenByDescending(r => r.SemanticSimilarity)
                .Take(limit)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Knowledge search failed");
            return [];
        }
    }

    /// <summary>
    /// Executes the is available async operation.
    /// </summary>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
    public async Task<bool> IsAvailableAsync(CancellationToken ct)
    {
        if (!_config.Enabled) return false;

        try
        {
            return await _qdrant.CollectionExistsAsync(_config.CollectionName, ct);
        }
        catch
        {
            return false;
        }
    }

    private List<string> ResolveCollections(string? sourceType)
    {
        if (string.Equals(sourceType, "wiki", StringComparison.OrdinalIgnoreCase))
        {
            return [_config.WikiCollectionName];
        }

        if (string.Equals(sourceType, "document", StringComparison.OrdinalIgnoreCase))
        {
            return [_config.DocumentsCollectionName];
        }

        return new List<string>
        {
            _config.WikiCollectionName,
            _config.DocumentsCollectionName
        }
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
    }

    private static Filter? BuildScopeFilter(IReadOnlyList<string> agentTags, string? sourceType)
    {
        var hasWildcard = agentTags.Count == 0 || agentTags.Contains("*");
        var sourceCondition = BuildSourceCondition(sourceType);

        // Wildcard means no filter — agent sees everything
        if (hasWildcard && sourceCondition is null)
            return null;

        if (hasWildcard)
        {
            return new Filter { Must = { sourceCondition! } };
        }

        // Filter: at least one of the agent's tags must match the document's tags
        var filter = new Filter
        {
            Should =
            {
                agentTags.Select(tag => new Condition
                {
                    Field = new FieldCondition
                    {
                        Key = "tags",
                        Match = new Match { Keyword = tag }
                    }
                })
            }
        };

        if (sourceCondition is not null)
        {
            filter.Must.Add(sourceCondition);
        }

        return filter;
    }

    private static Condition? BuildSourceCondition(string? sourceType)
    {
        if (string.IsNullOrWhiteSpace(sourceType))
        {
            return null;
        }

        var normalized = sourceType.Equals("document", StringComparison.OrdinalIgnoreCase)
            ? "document"
            : sourceType.Equals("wiki", StringComparison.OrdinalIgnoreCase)
                ? "wiki"
                : null;

        return normalized is null
            ? null
            : new Condition
            {
                Field = new FieldCondition
                {
                    Key = "source_type",
                    Match = new Match { Keyword = normalized }
                }
            };
    }

    private static string? GetPayloadString(ScoredPoint point, string key)
    {
        return point.Payload.TryGetValue(key, out var value) ? value.StringValue : null;
    }

    private static RelevanceScore ToRelevanceScore(ScoredPoint point, string? requestedSourceType)
    {
        var payloadSource = GetPayloadString(point, "source_type");
        var effectiveSource = requestedSourceType ?? payloadSource;
        return new RelevanceScore
        {
            EntryId = GetPayloadString(point, "entry_id")
                ?? GetPayloadString(point, "entryId")
                ?? GetPayloadString(point, "source_file")
                ?? "",
            Content = GetPayloadString(point, "text") ?? "",
            EstimatedTokens = (int)Math.Ceiling((GetPayloadString(point, "text") ?? "").Length / 4.0),
            SemanticSimilarity = point.Score,
            Score = point.Score,
            SourceType = RelevanceSourceType.Vector,
            KnowledgeSource = effectiveSource?.Equals("wiki", StringComparison.OrdinalIgnoreCase) == true
                ? KnowledgeSourceType.Wiki
                : effectiveSource?.Equals("document", StringComparison.OrdinalIgnoreCase) == true
                    ? KnowledgeSourceType.Document
                    : KnowledgeSourceType.Unknown
        };
    }
}
