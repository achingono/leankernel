using LeanKernel.Archivist.Wiki;
using LeanKernel.Core.Enums;
using LeanKernel.Core.Models;
using Xunit;

namespace LeanKernel.Tests.Unit.Archivist;

public class WikiMarkdownFormatTests
{
    [Fact]
    public void SerializeToMarkdown_ProducesFrontmatter()
    {
        var entry = MakeEntry("who-alice", WikiDimension.Who, "Alice", "Alice is a developer");
        var md = WikiStore.SerializeToMarkdown(entry);

        Assert.Contains("---", md);
        Assert.Contains("id: who-alice", md);
        Assert.Contains("dimension: who", md);
        Assert.Contains("subject: Alice", md);
        Assert.Contains("# Alice", md);
    }

    [Fact]
    public void SerializeToMarkdown_FactsAsListItems()
    {
        var entry = MakeEntry("who-bob", WikiDimension.Who, "Bob", "Bob is a PM");
        entry.Facts[0].Confidence = 0.85;
        entry.Facts[0] = entry.Facts[0] with { Source = "session-123" };

        var md = WikiStore.SerializeToMarkdown(entry);

        Assert.Contains("- Bob is a PM", md);
        Assert.Contains("confidence: 0.85", md);
        Assert.Contains("source: session-123", md);
    }

    [Fact]
    public void SerializeToMarkdown_RelationsAsLinks()
    {
        var entry = MakeEntry("who-alice", WikiDimension.Who, "Alice", "Fact");
        entry = entry with { Relations = ["what-project-atlas", "who-bob"] };

        var md = WikiStore.SerializeToMarkdown(entry);

        Assert.Contains("## Related", md);
        Assert.Contains("[Project Atlas]", md);
        Assert.Contains("[Bob]", md);
    }

    [Fact]
    public void ParseMarkdown_RoundTrips()
    {
        var entry = new WikiEntry
        {
            Id = "who-alice",
            Dimension = WikiDimension.Who,
            Subject = "Alice",
            Facts = [
                new WikiFact { Claim = "Alice is a developer", Confidence = 0.9, Source = "s1", EstimatedTokens = 5 },
                new WikiFact { Claim = "Alice likes coffee", Confidence = 0.7, Source = "s2", EstimatedTokens = 4 }
            ],
            Relations = ["what-project-atlas"],
            LastAccessed = DateTimeOffset.Parse("2024-06-15T10:00:00Z"),
            AccessCount = 5
        };

        var md = WikiStore.SerializeToMarkdown(entry);
        var parsed = WikiStore.ParseMarkdown(md, "fallback-id");

        Assert.NotNull(parsed);
        Assert.Equal("who-alice", parsed.Id);
        Assert.Equal(WikiDimension.Who, parsed.Dimension);
        Assert.Equal("Alice", parsed.Subject);
        Assert.Equal(2, parsed.Facts.Count);
        Assert.Equal("Alice is a developer", parsed.Facts[0].Claim);
        Assert.Equal(0.9, parsed.Facts[0].Confidence);
        Assert.Equal("s1", parsed.Facts[0].Source);
        Assert.Equal("Alice likes coffee", parsed.Facts[1].Claim);
        Assert.Single(parsed.Relations);
        Assert.Equal(5, parsed.AccessCount);
    }

    [Fact]
    public void ParseMarkdown_NoFrontmatter_ReturnsNull()
    {
        var result = WikiStore.ParseMarkdown("Just plain text", "fallback");
        Assert.Null(result);
    }

    [Fact]
    public void ParseMarkdown_EmptyContent_ReturnsNull()
    {
        var result = WikiStore.ParseMarkdown("", "fallback");
        Assert.Null(result);
    }

    [Fact]
    public void ParseMarkdown_InvalidFrontmatter_ReturnsNull()
    {
        var content = "---\nno closing delimiter";
        var result = WikiStore.ParseMarkdown(content, "fallback");
        Assert.Null(result);
    }

    [Fact]
    public void ParseMarkdown_FactWithoutMeta_DefaultConfidence()
    {
        var content = """
            ---
            id: who-test
            dimension: who
            subject: Test
            lastAccessed: 2024-01-01T00:00:00Z
            accessCount: 0
            ---

            # Test

            - A simple fact without metadata
            """;
        // Remove leading whitespace from each line (heredoc indentation)
        content = string.Join("\n", content.Split('\n').Select(l => l.TrimStart()));

        var parsed = WikiStore.ParseMarkdown(content, "fallback");

        Assert.NotNull(parsed);
        Assert.Single(parsed.Facts);
        Assert.Equal("A simple fact without metadata", parsed.Facts[0].Claim);
        Assert.Equal(0.5, parsed.Facts[0].Confidence);
    }

    [Fact]
    public void ParseMarkdown_MultipleDimensions()
    {
        foreach (var dim in Enum.GetValues<WikiDimension>())
        {
            var entry = new WikiEntry
            {
                Id = $"{dim.ToString().ToLowerInvariant()}-test",
                Dimension = dim,
                Subject = "Test",
                Facts = [new WikiFact { Claim = "Test fact", Confidence = 0.8, EstimatedTokens = 3 }]
            };
            var md = WikiStore.SerializeToMarkdown(entry);
            var parsed = WikiStore.ParseMarkdown(md, "fallback");

            Assert.NotNull(parsed);
            Assert.Equal(dim, parsed.Dimension);
        }
    }

    [Fact]
    public void ParseMarkdown_RelatedLinks_ExtractsIds()
    {
        var content = """
            ---
            id: who-alice
            dimension: who
            subject: Alice
            lastAccessed: 2024-01-01T00:00:00Z
            accessCount: 0
            ---

            # Alice

            - Alice is a developer <!--{confidence: 0.9, source: s1, confirmed: 2024-01-01, tokens: 5}-->

            ## Related

            - [Project Atlas](../what/project-atlas.md)
            - [Bob](./bob.md)
            """;
        content = string.Join("\n", content.Split('\n').Select(l => l.TrimStart()));

        var parsed = WikiStore.ParseMarkdown(content, "fallback");

        Assert.NotNull(parsed);
        Assert.Equal(2, parsed.Relations.Count);
        Assert.Contains("what-project-atlas", parsed.Relations);
        Assert.Contains("who-bob", parsed.Relations);
    }

    [Fact]
    public void ParseMarkdown_SameDimensionRelation_RoundTrips()
    {
        // Same-dimension links (./name.md) should round-trip correctly
        var entry = new WikiEntry
        {
            Id = "who-alice",
            Dimension = WikiDimension.Who,
            Subject = "Alice",
            Facts = [new WikiFact { Claim = "Alice works with Bob", Confidence = 0.9, EstimatedTokens = 5 }],
            Relations = ["who-bob", "what-project-atlas"],
            LastAccessed = DateTimeOffset.Parse("2024-06-15T10:00:00Z"),
            AccessCount = 3
        };

        var md = WikiStore.SerializeToMarkdown(entry);
        var parsed = WikiStore.ParseMarkdown(md, "fallback");

        Assert.NotNull(parsed);
        Assert.Equal(2, parsed.Relations.Count);
        Assert.Contains("who-bob", parsed.Relations);
        Assert.Contains("what-project-atlas", parsed.Relations);
    }

    private static WikiEntry MakeEntry(string id, WikiDimension dim, string subject, string claim) =>
        new()
        {
            Id = id,
            Dimension = dim,
            Subject = subject,
            Facts = [new WikiFact { Claim = claim, Confidence = 0.8, EstimatedTokens = 5 }]
        };
}
