using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Archivist.Embedding;

/// <summary>
/// In-memory cache for embedding vectors to avoid redundant API calls.
/// Uses a simple LRU eviction policy. For persistence across restarts,
/// a future version could use an SQLite backing store.
/// </summary>
public sealed class EmbeddingCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly int _maxEntries;
    private readonly ILogger<EmbeddingCache> _logger;

    public EmbeddingCache(int maxEntries, ILogger<EmbeddingCache> logger)
    {
        _maxEntries = maxEntries;
        _logger = logger;
    }

    public bool TryGet(string text, out float[] embedding)
    {
        var key = ComputeKey(text);
        if (_cache.TryGetValue(key, out var entry))
        {
            entry.LastAccessed = DateTimeOffset.UtcNow;
            embedding = entry.Embedding;
            return true;
        }

        embedding = [];
        return false;
    }

    public void Set(string text, float[] embedding)
    {
        var key = ComputeKey(text);
        _cache[key] = new CacheEntry
        {
            Embedding = embedding,
            LastAccessed = DateTimeOffset.UtcNow
        };

        if (_cache.Count > _maxEntries)
        {
            Evict();
        }
    }

    public int Count => _cache.Count;

    private void Evict()
    {
        var toRemove = _cache
            .OrderBy(kv => kv.Value.LastAccessed)
            .Take(_cache.Count - _maxEntries + (_maxEntries / 10))
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in toRemove)
        {
            _cache.TryRemove(key, out _);
        }

        _logger.LogDebug("Evicted {Count} embedding cache entries", toRemove.Count);
    }

    private static string ComputeKey(string text)
    {
        var hash = (ulong)Math.Abs((long)text.GetHashCode(StringComparison.Ordinal));
        return hash.ToString("x16");
    }

    private sealed class CacheEntry
    {
        public required float[] Embedding { get; init; }
        public DateTimeOffset LastAccessed { get; set; }
    }
}
