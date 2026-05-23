using LeanKernel.Archivist.Wiki;
using LeanKernel.Core.Models;

namespace LeanKernel.Tests.Unit.Archivist;

public sealed class WikiFactMapperTests
{
    private readonly WikiFactMapper _mapper = new();

    [Fact]
    public void Map_GeneratesCanonicalIdsWithoutLlmPrefixOrDate()
    {
        var extracted = new[]
        {
            new ExtractedWikiFact
            {
                Subject = "Alfero Chingono",
                PrimaryDimension = "who",
                Claim = "Alfero prefers concise responses.",
                Who = "Alfero Chingono",
                SourceQuote = "I prefer concise responses."
            }
        };

        var entries = _mapper.Map(extracted, "conversation:test");

        var entry = Assert.Single(entries);
        Assert.Equal("who-alfero-chingono", entry.Id);
        Assert.DoesNotContain("llm-", entry.Id, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(DateTime.UtcNow.ToString("yyyyMMdd"), entry.Id, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Map_DeduplicatesByNormalizedKey()
    {
        var extracted = new[]
        {
            new ExtractedWikiFact
            {
                Subject = "Ada",
                PrimaryDimension = "who",
                Claim = "Ada likes direct responses.",
                Who = "Ada",
                SourceQuote = "I like direct responses."
            },
            new ExtractedWikiFact
            {
                Subject = "Ada",
                PrimaryDimension = "who",
                Claim = "Ada likes direct responses!",
                Who = "Ada",
                SourceQuote = "I like direct responses."
            }
        };

        var entries = _mapper.Map(extracted, "conversation:test");

        var entry = Assert.Single(entries);
        Assert.Single(entry.Facts);
        Assert.NotNull(entry.Facts[0].NormalizedKey);
    }

    [Fact]
    public void Map_AddsAliasOnSlugCollision()
    {
        var extracted = new[]
        {
            new ExtractedWikiFact
            {
                Subject = "Alfero Chingono",
                PrimaryDimension = "who",
                Claim = "Alfero prefers concise answers.",
                Who = "Alfero Chingono",
                SourceQuote = "I prefer concise answers."
            },
            new ExtractedWikiFact
            {
                Subject = "Alfero-Chingono",
                PrimaryDimension = "who",
                Claim = "Alfero uses direct style.",
                Who = "Alfero-Chingono",
                SourceQuote = "Use direct style."
            }
        };

        var entries = _mapper.Map(extracted, "conversation:test");

        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.Id == "who-alfero-chingono");
        Assert.Contains(entries, e => e.Id == "who-alfero-chingono-2");
    }

    [Fact]
    public void Map_TreatsSubjectCaseVariantsAsSameCanonicalEntry()
    {
        var extracted = new[]
        {
            new ExtractedWikiFact
            {
                Subject = "Ada",
                PrimaryDimension = "who",
                Claim = "Ada prefers concise responses.",
                Who = "Ada"
            },
            new ExtractedWikiFact
            {
                Subject = "ada",
                PrimaryDimension = "who",
                Claim = "Ada uses direct language.",
                Who = "Ada"
            }
        };

        var entries = _mapper.Map(extracted, "conversation:test");

        var entry = Assert.Single(entries);
        Assert.Equal("who-ada", entry.Id);
        Assert.Equal(2, entry.Facts.Count);
    }

    [Fact]
    public void Map_SkipsLowSignalPlaceholderClaims()
    {
        var extracted = new[]
        {
            new ExtractedWikiFact
            {
                Subject = "John Smith",
                PrimaryDimension = "who",
                Claim = "not specified",
                Who = "John Smith"
            },
            new ExtractedWikiFact
            {
                Subject = "John Smith",
                PrimaryDimension = "who",
                Claim = "John Smith is CTO at Teachers",
                Who = "John Smith"
            }
        };

        var entries = _mapper.Map(extracted, "conversation:test");

        var entry = Assert.Single(entries);
        var fact = Assert.Single(entry.Facts);
        Assert.Equal("John Smith is CTO at Teachers", fact.Claim);
    }
}
