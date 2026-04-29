using Microsoft.Extensions.Logging.Abstractions;
using LeanKernel.Archivist.Embedding;

namespace LeanKernel.Tests.Unit.Archivist;

public class EmbeddingCacheTests
{
    [Fact]
    public void TryGet_ReturnsFalse_WhenNotCached()
    {
        var cache = new EmbeddingCache(100, NullLogger<EmbeddingCache>.Instance);
        var found = cache.TryGet("hello", out var embedding);
        Assert.False(found);
        Assert.Empty(embedding);
    }

    [Fact]
    public void Set_ThenGet_ReturnsEmbedding()
    {
        var cache = new EmbeddingCache(100, NullLogger<EmbeddingCache>.Instance);
        var vector = new float[] { 0.1f, 0.2f, 0.3f };

        cache.Set("hello", vector);
        var found = cache.TryGet("hello", out var result);

        Assert.True(found);
        Assert.Equal(vector, result);
    }

    [Fact]
    public void Evicts_WhenOverCapacity()
    {
        var cache = new EmbeddingCache(5, NullLogger<EmbeddingCache>.Instance);

        for (var i = 0; i < 10; i++)
        {
            cache.Set($"text-{i}", [i * 0.1f]);
        }

        Assert.True(cache.Count <= 5);
    }

    [Fact]
    public void SameText_ReturnsSameEntry()
    {
        var cache = new EmbeddingCache(100, NullLogger<EmbeddingCache>.Instance);
        cache.Set("test", [1.0f]);
        cache.Set("test", [2.0f]);

        var found = cache.TryGet("test", out var result);
        Assert.True(found);
        Assert.Equal([2.0f], result);
        Assert.Equal(1, cache.Count);
    }
}
