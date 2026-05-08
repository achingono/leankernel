using LeanKernel.Archivist.Wiki;
using LeanKernel.Core.Enums;

namespace LeanKernel.Tests.Unit.Archivist;

public class WikiExtractorTests
{
    [Fact]
    public void ExtractFacts_DetectsWhoPattern_IsA()
    {
        var entries = WikiExtractor.ExtractFacts(
            "Alice Smith is a project manager at Acme Corp",
            "I've noted that.",
            "test-source");

        Assert.Contains(entries, e => e.Dimension == WikiDimension.Who && e.Subject == "Alice Smith");
    }

    [Fact]
    public void ExtractFacts_DetectsWhoPattern_WorksAt()
    {
        var entries = WikiExtractor.ExtractFacts(
            "Bob Johnson works at Contoso Ltd",
            "Got it.",
            "test-source");

        Assert.Contains(entries, e => e.Dimension == WikiDimension.Who && e.Subject == "Bob Johnson");
    }

    [Fact]
    public void ExtractFacts_SkipsCommonWords()
    {
        var entries = WikiExtractor.ExtractFacts(
            "This is a test. There is a thing.",
            "Understood.",
            "test-source");

        Assert.DoesNotContain(entries, e => e.Subject == "This");
        Assert.DoesNotContain(entries, e => e.Subject == "There");
    }

    [Fact]
    public void ExtractFacts_DetectsWhatPattern()
    {
        var entries = WikiExtractor.ExtractFacts(
            "We discussed the Alpha project and its timeline",
            "The Alpha project is progressing well.",
            "test-source");

        Assert.Contains(entries, e => e.Dimension == WikiDimension.What);
    }

    [Fact]
    public void ExtractFacts_DetectsWhenPattern_IsoDate()
    {
        var entries = WikiExtractor.ExtractFacts(
            "The deadline is 2026-05-15",
            "I've noted the deadline.",
            "test-source");

        Assert.Contains(entries, e => e.Dimension == WikiDimension.When);
    }

    [Fact]
    public void ExtractFacts_MergesDuplicateWhoEntries()
    {
        var entries = WikiExtractor.ExtractFacts(
            "Alice Smith is a manager. Alice Smith works at Acme",
            "Noted.",
            "test-source");

        var aliceEntries = entries.Where(e => e.Subject == "Alice Smith").ToList();
        Assert.Single(aliceEntries);
        Assert.True(aliceEntries[0].Facts.Count >= 2);
    }

    [Fact]
    public void ExtractFacts_AssignsModerateConfidence()
    {
        var entries = WikiExtractor.ExtractFacts(
            "Alice Smith is a developer",
            "OK.",
            "test-source");

        var fact = entries.SelectMany(e => e.Facts).FirstOrDefault();
        Assert.NotNull(fact);
        Assert.Equal(0.7, fact.Confidence);
    }

    [Fact]
    public void ExtractFacts_GeneratesCorrectSlugId()
    {
        var entries = WikiExtractor.ExtractFacts(
            "John Doe is a developer",
            "OK.",
            "test-source");

        var entry = entries.FirstOrDefault(e => e.Subject == "John Doe");
        Assert.NotNull(entry);
        Assert.Equal("who-john-doe", entry.Id);
    }

    [Fact]
    public void ExtractFacts_ReturnsEmptyForGenericText()
    {
        var entries = WikiExtractor.ExtractFacts(
            "hello, how are you?",
            "I'm fine, thanks!",
            "test-source");

        // Should not create entries for generic conversation
        Assert.DoesNotContain(entries, e => e.Dimension == WikiDimension.Who);
    }

    [Fact]
    public void ExtractFacts_ExtractsUserNameFromFirstPersonStatement()
    {
        var entries = WikiExtractor.ExtractFacts(
            "my name is Ada Lovelace",
            "Thanks for sharing.",
            "test-source");

        var profile = entries.FirstOrDefault(e => e.Id == "who-user-profile");
        Assert.NotNull(profile);
        Assert.Contains(profile.Facts, f => f.Claim.Contains("User name is Ada Lovelace", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExtractFacts_ExtractsUserPreference()
    {
        var entries = WikiExtractor.ExtractFacts(
            "I prefer concise responses.",
            "Understood.",
            "test-source");

        var prefs = entries.FirstOrDefault(e => e.Id == "what-user-preferences");
        Assert.NotNull(prefs);
        Assert.Contains(prefs.Facts, f => f.Claim.Contains("User prefers concise responses", StringComparison.OrdinalIgnoreCase));
    }
}
