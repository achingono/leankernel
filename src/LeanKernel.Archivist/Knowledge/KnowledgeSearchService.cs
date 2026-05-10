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
        CancellationToken ct)
    {
        if (!_config.Enabled) return [];

        limit = Math.Clamp(limit, 1, 50);

        try
        {
            var exists = await _qdrant.CollectionExistsAsync(_config.CollectionName, ct);
            if (!exists) return [];

            var embedding = await _embeddings.EmbedAsync(query, ct);
            var filter = BuildScopeFilter(agentTags);

            var results = await _qdrant.SearchAsync(
                _config.CollectionName,
                embedding,
                filter: filter,
                limit: (ulong)limit,
                cancellationToken: ct);

            return results.Select(r => new RelevanceScore
            {
                EntryId = GetPayloadString(r, "entry_id") ?? GetPayloadString(r, "source_file") ?? "",
                Content = GetPayloadString(r, "text") ?? "",
                EstimatedTokens = (int)Math.Ceiling((GetPayloadString(r, "text") ?? "").Length / 4.0),
                SemanticSimilarity = r.Score,
                Score = r.Score,
                SourceType = RelevanceSourceType.Vector
            }).ToList();
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

    private static Filter? BuildScopeFilter(IReadOnlyList<string> agentTags)
    {
        // Wildcard means no filter — agent sees everything
        if (agentTags.Count == 0 || agentTags.Contains("*"))
            return null;

        // Filter: at least one of the agent's tags must match the document's tags
        return new Filter
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
    }

    private static string? GetPayloadString(ScoredPoint point, string key)
    {
        return point.Payload.TryGetValue(key, out var value) ? value.StringValue : null;
    }
}
