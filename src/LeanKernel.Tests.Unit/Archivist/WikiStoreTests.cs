using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LeanKernel.Archivist.Wiki;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Enums;
using LeanKernel.Core.Models;
using Xunit;

namespace LeanKernel.Tests.Unit.Archivist;

public class WikiStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly WikiStore _store;

    public WikiStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"LEANKERNEL_wiki_{Guid.NewGuid():N}");
        var config = Options.Create(new LeanKernelConfig { Wiki = new WikiConfig { BasePath = _tempDir } });
        _store = new WikiStore(config, NullLogger<WikiStore>.Instance);
    }

    [Fact]
    public async Task UpsertAndGet_RoundTrips()
    {
        var entry = MakeEntry("who-alice", WikiDimension.Who, "Alice", "Alice is a developer");
        await _store.UpsertAsync(entry, CancellationToken.None);

        var result = await _store.GetAsync("who-alice", CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal("Alice", result.Subject);
        Assert.Single(result.Facts);
        Assert.Equal("Alice is a developer", result.Facts[0].Claim);
    }

    [Fact]
    public async Task GetAsync_NonExistent_ReturnsNull()
    {
        var result = await _store.GetAsync("who-nobody", CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_RemovesEntry()
    {
        var entry = MakeEntry("who-bob", WikiDimension.Who, "Bob", "Bob is a PM");
        await _store.UpsertAsync(entry, CancellationToken.None);
        await _store.DeleteAsync("who-bob", CancellationToken.None);

        var result = await _store.GetAsync("who-bob", CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_NonExistent_NoOp()
    {
        await _store.DeleteAsync("who-ghost", CancellationToken.None);

        var result = await _store.GetAsync("who-ghost", CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task ListByDimension_ReturnsCorrectEntries()
    {
        await _store.UpsertAsync(MakeEntry("who-a", WikiDimension.Who, "A", "Fact A"), CancellationToken.None);
        await _store.UpsertAsync(MakeEntry("who-b", WikiDimension.Who, "B", "Fact B"), CancellationToken.None);

        var results = await _store.ListByDimensionAsync(WikiDimension.Who, CancellationToken.None);
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task ListByDimension_EmptyDimension_ReturnsEmpty()
    {
        var results = await _store.ListByDimensionAsync(WikiDimension.How, CancellationToken.None);
        Assert.Empty(results);
    }

    [Fact]
    public async Task QueryAsync_TextFilter_Matches()
    {
        await _store.UpsertAsync(MakeEntry("who-alice", WikiDimension.Who, "Alice", "Alice is a developer"), CancellationToken.None);
        await _store.UpsertAsync(MakeEntry("who-bob", WikiDimension.Who, "Bob", "Bob is a PM"), CancellationToken.None);

        var query = new WikiQuery { TextQuery = "alice", MaxResults = 10 };
        var results = await _store.QueryAsync(query, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("Alice", results[0].Subject);
    }

    [Fact]
    public async Task QueryAsync_NaturalLanguageQuery_MatchesByTokens()
    {
        await _store.UpsertAsync(
            MakeEntry("who-user-profile", WikiDimension.Who, "User", "User name is Ada Lovelace"),
            CancellationToken.None);

        var query = new WikiQuery { TextQuery = "what is my name", MaxResults = 10 };
        var results = await _store.QueryAsync(query, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("who-user-profile", results[0].Id);
    }

    [Fact]
    public async Task QueryAsync_DimensionFilter_LimitsScope()
    {
        await _store.UpsertAsync(MakeEntry("who-alice", WikiDimension.Who, "Alice", "Fact"), CancellationToken.None);
        await _store.UpsertAsync(MakeEntry("what-event", WikiDimension.What, "Event", "Fact"), CancellationToken.None);

        var query = new WikiQuery { Dimensions = [WikiDimension.Who] };
        var results = await _store.QueryAsync(query, CancellationToken.None);

        Assert.Single(results);
    }

    [Fact]
    public async Task QueryAsync_MaxResults_Honored()
    {
        for (int i = 0; i < 5; i++)
            await _store.UpsertAsync(MakeEntry($"who-p{i}", WikiDimension.Who, $"P{i}", "Fact"), CancellationToken.None);

        var query = new WikiQuery { MaxResults = 2 };
        var results = await _store.QueryAsync(query, CancellationToken.None);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task QueryAsync_MinConfidence_Filters()
    {
        var entry = MakeEntry("who-low", WikiDimension.Who, "Low", "Low conf");
        entry.Facts[0].Confidence = 0.1;
        await _store.UpsertAsync(entry, CancellationToken.None);

        var query = new WikiQuery { MinConfidence = 0.5 };
        var results = await _store.QueryAsync(query, CancellationToken.None);
        Assert.Empty(results);
    }

    [Fact]
    public async Task IngestFactsAsync_NewEntry_Creates()
    {
        var entries = new[] { MakeEntry("who-new", WikiDimension.Who, "New", "New fact") };
        await _store.IngestFactsAsync(entries, CancellationToken.None);

        var result = await _store.GetAsync("who-new", CancellationToken.None);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task IngestFactsAsync_ExistingEntry_MergesFacts()
    {
        await _store.UpsertAsync(MakeEntry("who-merge", WikiDimension.Who, "Merge", "Fact 1"), CancellationToken.None);

        var incoming = MakeEntry("who-merge", WikiDimension.Who, "Merge", "Fact 2");
        await _store.IngestFactsAsync([incoming], CancellationToken.None);

        var result = await _store.GetAsync("who-merge", CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal(2, result.Facts.Count);
    }

    [Fact]
    public async Task IngestFactsAsync_DuplicateClaim_UpdatesConfidence()
    {
        var entry = MakeEntry("who-dup", WikiDimension.Who, "Dup", "Same claim");
        entry.Facts[0].Confidence = 0.5;
        await _store.UpsertAsync(entry, CancellationToken.None);

        var incoming = MakeEntry("who-dup", WikiDimension.Who, "Dup", "Same claim");
        incoming.Facts[0].Confidence = 0.9;
        await _store.IngestFactsAsync([incoming], CancellationToken.None);

        var result = await _store.GetAsync("who-dup", CancellationToken.None);
        Assert.NotNull(result);
        Assert.Single(result.Facts);
        Assert.Equal(0.9, result.Facts[0].Confidence);
    }

    [Fact]
    public async Task UpsertAsync_WritesIndexFile()
    {
        var entry = MakeEntry("who-alice", WikiDimension.Who, "Alice", "Alice is a developer");
        await _store.UpsertAsync(entry, CancellationToken.None);

        var metaFolder = new WikiConfig().MetaFolder;
        var indexPath = Path.Combine(_tempDir, metaFolder, "index.json");
        Assert.True(File.Exists(indexPath));
    }

    [Fact]
    public async Task UpsertAsync_RejectsDimensionIdConflict()
    {
        var entry = MakeEntry("what-alice", WikiDimension.Who, "Alice", "Alice is a developer");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.UpsertAsync(entry, CancellationToken.None));
    }

    [Fact]
    public async Task ListByDimensionAsync_UsesFactPointersForCrossDimensionLookup()
    {
        var entry = new WikiEntry
        {
            Id = "who-ada",
            Dimension = WikiDimension.Who,
            Subject = "Ada",
            Facts =
            [
                new WikiFact
                {
                    Claim = "Ada prefers concise responses because it reduces noise.",
                    Confidence = 0.9,
                    Context = new WikiFactContext { Who = "Ada", Why = "Reduce response noise" },
                    EstimatedTokens = 8
                }
            ]
        };
        await _store.UpsertAsync(entry, CancellationToken.None);

        var whyEntries = await _store.ListByDimensionAsync(WikiDimension.Why, CancellationToken.None);

        Assert.Contains(whyEntries, e => e.Id == "who-ada");
    }

    [Fact]
    public async Task UpsertAsync_DoesNotMisresolveSubjectWithDimensionTokenPrefix()
    {
        var entry = MakeEntry("who-what-if-analysis", WikiDimension.Who, "what if analysis", "A strategy note");
        await _store.UpsertAsync(entry, CancellationToken.None);

        var expectedPath = Path.Combine(_tempDir, "who", "what-if-analysis.md");
        var wrongPath = Path.Combine(_tempDir, "what", "if-analysis.md");
        Assert.True(File.Exists(expectedPath));
        Assert.False(File.Exists(wrongPath));
    }

    private static WikiEntry MakeEntry(string id, WikiDimension dim, string subject, string claim) =>
        new()
        {
            Id = id,
            Dimension = dim,
            Subject = subject,
            Facts = [new WikiFact { Claim = claim, Confidence = 0.8, EstimatedTokens = 5 }]
        };

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
