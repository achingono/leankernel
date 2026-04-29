using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LeanKernel.Archivist.Wiki;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Enums;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using NSubstitute;
using Xunit;

namespace LeanKernel.Tests.Unit.Archivist;

public class WikiCompilerFullTests
{
    private static LeanKernelConfig DefaultConfig => new()
    {
        Wiki = new WikiConfig
        {
            MaxFactsPerEntry = 5,
            StaleFactDays = 30,
            MinConfidenceThreshold = 0.5
        }
    };

    [Fact]
    public void ProcessEntry_NoChanges_ReturnsZeros()
    {
        var compiler = CreateCompiler();
        var entry = MakeEntry("e1", [MakeFact("Fact", 0.8)]);

        var (pruned, deduped, updated) = compiler.ProcessEntry(entry);

        Assert.Equal(0, pruned);
        Assert.Equal(0, deduped);
        Assert.Null(updated);
    }

    [Fact]
    public void ProcessEntry_DuplicateFacts_Deduplicates()
    {
        var compiler = CreateCompiler();
        var entry = MakeEntry("e1", [MakeFact("Same claim", 0.8), MakeFact("same claim", 0.9)]);

        var (_, deduped, updated) = compiler.ProcessEntry(entry);

        Assert.Equal(1, deduped);
        Assert.NotNull(updated);
        Assert.Single(updated.Facts);
    }

    [Fact]
    public void ProcessEntry_StaleFacts_Pruned()
    {
        var compiler = CreateCompiler();
        var staleFact = MakeFact("Stale", 0.1);
        staleFact.LastConfirmed = DateTimeOffset.UtcNow.AddDays(-60);
        var entry = MakeEntry("e1", [staleFact, MakeFact("Fresh", 0.9)]);

        var (pruned, _, updated) = compiler.ProcessEntry(entry);

        Assert.Equal(1, pruned);
        Assert.NotNull(updated);
        Assert.Single(updated.Facts);
        Assert.Equal("Fresh", updated.Facts[0].Claim);
    }

    [Fact]
    public void ProcessEntry_ExceedsMaxFacts_Caps()
    {
        var compiler = CreateCompiler();
        var facts = Enumerable.Range(0, 10).Select(i => MakeFact($"Fact {i}", 0.5 + i * 0.05)).ToList();
        // Add a duplicate to trigger dedup
        facts.Add(MakeFact("Fact 0", 0.5));
        var entry = MakeEntry("e1", facts);

        var (_, _, updated) = compiler.ProcessEntry(entry);

        Assert.NotNull(updated);
        Assert.True(updated.Facts.Count <= 5);
    }

    [Fact]
    public async Task CompileAsync_ProcessesAllDimensions()
    {
        var wiki = Substitute.For<IWikiStore>();
        foreach (var dim in Enum.GetValues<WikiDimension>())
            wiki.ListByDimensionAsync(dim, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<WikiEntry>>([]));

        var compiler = new WikiCompiler(wiki, Options.Create(DefaultConfig), NullLogger<WikiCompiler>.Instance);
        await compiler.CompileAsync(CancellationToken.None);

        // Should have called ListByDimensionAsync for each dimension
        foreach (var dim in Enum.GetValues<WikiDimension>())
            await wiki.Received(1).ListByDimensionAsync(dim, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompileAsync_UpdatesModifiedEntries()
    {
        var wiki = Substitute.For<IWikiStore>();
        var entry = MakeEntry("who-dup", [MakeFact("Dup A", 0.8), MakeFact("dup a", 0.7)]);
        entry = entry with { Dimension = WikiDimension.Who };

        wiki.ListByDimensionAsync(WikiDimension.Who, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WikiEntry>>([entry]));
        foreach (var dim in Enum.GetValues<WikiDimension>().Where(d => d != WikiDimension.Who))
            wiki.ListByDimensionAsync(dim, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<WikiEntry>>([]));

        var compiler = new WikiCompiler(wiki, Options.Create(DefaultConfig), NullLogger<WikiCompiler>.Instance);
        await compiler.CompileAsync(CancellationToken.None);

        await wiki.Received(1).UpsertAsync(Arg.Any<WikiEntry>(), Arg.Any<CancellationToken>());
    }

    private WikiCompiler CreateCompiler()
    {
        var wiki = Substitute.For<IWikiStore>();
        return new WikiCompiler(wiki, Options.Create(DefaultConfig), NullLogger<WikiCompiler>.Instance);
    }

    private static WikiEntry MakeEntry(string id, List<WikiFact> facts) => new()
    {
        Id = id, Dimension = WikiDimension.Who, Subject = "Test",
        Facts = facts, LastAccessed = DateTimeOffset.UtcNow
    };

    private static WikiFact MakeFact(string claim, double confidence) => new()
    {
        Claim = claim, Confidence = confidence, EstimatedTokens = 3,
        LastConfirmed = DateTimeOffset.UtcNow
    };
}
