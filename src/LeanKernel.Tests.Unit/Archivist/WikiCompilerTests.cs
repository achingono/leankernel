using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LeanKernel.Archivist.Wiki;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Enums;
using LeanKernel.Core.Models;

namespace LeanKernel.Tests.Unit.Archivist;

public class WikiCompilerTests
{
    private static WikiCompiler CreateCompiler(IOptions<LeanKernelConfig>? config = null)
    {
        var cfg = config ?? Options.Create(new LeanKernelConfig());
        var wiki = new MockWikiStore();
        return new WikiCompiler(wiki, cfg, NullLogger<WikiCompiler>.Instance);
    }

    [Fact]
    public void ProcessEntry_PrunesStaleLowConfidenceFacts()
    {
        var compiler = CreateCompiler();
        var entry = new WikiEntry
        {
            Id = "who-test",
            Dimension = WikiDimension.Who,
            Subject = "Test",
            Facts =
            [
                new WikiFact
                {
                    Claim = "Fresh high confidence",
                    Confidence = 0.9,
                    LastConfirmed = DateTimeOffset.UtcNow
                },
                new WikiFact
                {
                    Claim = "Stale low confidence",
                    Confidence = 0.3,
                    LastConfirmed = DateTimeOffset.UtcNow.AddDays(-60)
                }
            ]
        };

        var (pruned, deduped, updated) = compiler.ProcessEntry(entry);

        Assert.Equal(1, pruned);
        Assert.NotNull(updated);
        Assert.Single(updated.Facts);
        Assert.Equal("Fresh high confidence", updated.Facts[0].Claim);
    }

    [Fact]
    public void ProcessEntry_DeduplicatesIdenticalClaims()
    {
        var compiler = CreateCompiler();
        var entry = new WikiEntry
        {
            Id = "who-test",
            Dimension = WikiDimension.Who,
            Subject = "Test",
            Facts =
            [
                new WikiFact { Claim = "Alice is a developer", Confidence = 0.9 },
                new WikiFact { Claim = "alice is a developer", Confidence = 0.8 },
                new WikiFact { Claim = "Bob is a tester", Confidence = 0.9 }
            ]
        };

        var (pruned, deduped, updated) = compiler.ProcessEntry(entry);

        Assert.Equal(1, deduped);
        Assert.NotNull(updated);
        Assert.Equal(2, updated.Facts.Count);
    }

    [Fact]
    public void ProcessEntry_ReturnsNull_WhenNoChanges()
    {
        var compiler = CreateCompiler();
        var entry = new WikiEntry
        {
            Id = "who-test",
            Dimension = WikiDimension.Who,
            Subject = "Test",
            Facts =
            [
                new WikiFact { Claim = "Unique fresh fact", Confidence = 0.9, LastConfirmed = DateTimeOffset.UtcNow }
            ]
        };

        var (pruned, deduped, updated) = compiler.ProcessEntry(entry);

        Assert.Equal(0, pruned);
        Assert.Equal(0, deduped);
        Assert.Null(updated);
    }

    [Fact]
    public void ProcessEntry_CapsFactsAtMax()
    {
        var config = Options.Create(new LeanKernelConfig
        {
            Wiki = new WikiConfig { MaxFactsPerEntry = 3 }
        });
        var compiler = CreateCompiler(config);

        var facts = Enumerable.Range(1, 10).Select(i => new WikiFact
        {
            Claim = $"Fact {i}",
            Confidence = 0.5 + (i * 0.04),
            LastConfirmed = DateTimeOffset.UtcNow
        }).ToList();

        var entry = new WikiEntry
        {
            Id = "who-test",
            Dimension = WikiDimension.Who,
            Subject = "Test",
            Facts = facts
        };

        var (pruned, deduped, updated) = compiler.ProcessEntry(entry);

        Assert.NotNull(updated);
        Assert.Equal(3, updated.Facts.Count);
        // Should keep highest confidence
        Assert.True(updated.Facts[0].Confidence >= updated.Facts[1].Confidence);
    }

    // Simple mock since we only call ProcessEntry (doesn't need store)
    private sealed class MockWikiStore : Core.Interfaces.IWikiStore
    {
        public Task<WikiEntry?> GetAsync(string entryId, CancellationToken ct) => Task.FromResult<WikiEntry?>(null);
        public Task UpsertAsync(WikiEntry entry, CancellationToken ct) => Task.CompletedTask;
        public Task DeleteAsync(string entryId, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<WikiEntry>> QueryAsync(WikiQuery query, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WikiEntry>>([]);
        public Task<IReadOnlyList<WikiEntry>> ListByDimensionAsync(Core.Enums.WikiDimension dimension, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WikiEntry>>([]);
        public Task IngestFactsAsync(IEnumerable<WikiEntry> entries, CancellationToken ct) => Task.CompletedTask;
    }
}
