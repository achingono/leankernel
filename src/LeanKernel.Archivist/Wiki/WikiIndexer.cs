using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Archivist.Wiki;

/// <summary>
/// Indexes wiki entries into Qdrant for semantic vector search.
/// Each wiki entry becomes a point with its embedding and metadata.
/// </summary>
public sealed class WikiIndexer
{
    private readonly QdrantClient _qdrant;
    private readonly IEmbeddingService _embeddings;
    private readonly LeanKernelConfig _config;
    private readonly ILogger<WikiIndexer> _logger;
    private bool _collectionReady;

    public WikiIndexer(
        IEmbeddingService embeddings,
        IOptions<LeanKernelConfig> config,
        ILogger<WikiIndexer> logger)
    {
        _config = config.Value;
        _qdrant = new QdrantClient(_config.Qdrant.Host, _config.Qdrant.Port);
        _embeddings = embeddings;
        _logger = logger;
    }

    public async Task EnsureCollectionAsync(CancellationToken ct)
    {
        if (_collectionReady) return;

        try
        {
            var exists = await _qdrant.CollectionExistsAsync(_config.Qdrant.CollectionName, ct);
            if (!exists)
            {
                await _qdrant.CreateCollectionAsync(
                    _config.Qdrant.CollectionName,
                    new VectorParams
                    {
                        Size = (ulong)_config.Qdrant.EmbeddingDimension,
                        Distance = Distance.Cosine
                    },
                    cancellationToken: ct);
                _logger.LogInformation("Created Qdrant collection: {Collection}", _config.Qdrant.CollectionName);
            }
            _collectionReady = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not connect to Qdrant — vector search disabled");
        }
    }

    public async Task IndexEntryAsync(WikiEntry entry, CancellationToken ct)
    {
        await EnsureCollectionAsync(ct);
        if (!_collectionReady) return;

        var text = FormatForEmbedding(entry);
        var embedding = await _embeddings.EmbedAsync(text, ct);

        var pointId = GeneratePointId(entry.Id);
        var point = new PointStruct
        {
            Id = pointId,
            Vectors = embedding,
            Payload =
            {
                ["entry_id"] = entry.Id,
                ["dimension"] = entry.Dimension.ToString().ToLowerInvariant(),
                ["subject"] = entry.Subject,
                ["text"] = text,
                ["fact_count"] = entry.Facts.Count,
                ["last_accessed"] = entry.LastAccessed.ToUnixTimeSeconds()
            }
        };

        await _qdrant.UpsertAsync(
            _config.Qdrant.CollectionName,
            [point],
            cancellationToken: ct);

        _logger.LogDebug("Indexed wiki entry {EntryId} ({Dimension}:{Subject})",
            entry.Id, entry.Dimension, entry.Subject);
    }

    public async Task<List<RelevanceScore>> SearchAsync(string query, int limit, CancellationToken ct)
    {
        await EnsureCollectionAsync(ct);
        if (!_collectionReady) return [];

        var embedding = await _embeddings.EmbedAsync(query, ct);

        var results = await _qdrant.SearchAsync(
            _config.Qdrant.CollectionName,
            embedding,
            limit: (ulong)limit,
            cancellationToken: ct);

        return results.Select(r => new RelevanceScore
        {
            EntryId = r.Payload["entry_id"].StringValue,
            Content = r.Payload["text"].StringValue,
            EstimatedTokens = (int)Math.Ceiling(r.Payload["text"].StringValue.Length / 4.0),
            SemanticSimilarity = r.Score,
            Score = r.Score
        }).ToList();
    }

    public async Task DeleteEntryAsync(string entryId, CancellationToken ct)
    {
        await EnsureCollectionAsync(ct);
        if (!_collectionReady) return;

        var pointId = GeneratePointId(entryId);
        await _qdrant.DeleteAsync(
            _config.Qdrant.CollectionName,
            [pointId],
            cancellationToken: ct);
    }

    private static string FormatForEmbedding(WikiEntry entry) =>
        $"{entry.Dimension}:{entry.Subject} — " +
        string.Join("; ", entry.Facts.Select(f => f.Claim));

    private static PointId GeneratePointId(string entryId)
    {
        var hash = (ulong)Math.Abs((long)entryId.GetHashCode(StringComparison.Ordinal));
        return new PointId { Num = hash };
    }
}
