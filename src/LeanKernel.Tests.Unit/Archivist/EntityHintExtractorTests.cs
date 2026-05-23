using LeanKernel.Archivist;
using LeanKernel.Core.Models;

namespace LeanKernel.Tests.Unit.Archivist;

public class EntityHintExtractorTests
{
    [Fact]
    public void Extract_RelationshipTerms_EmitsRelationshipHint()
    {
        var extractor = new EntityHintExtractor();

        var hints = extractor.Extract("I'm thinking of my mother today", []);

        Assert.Contains(hints, h => h.Type == EntityHintType.Relationship && h.NormalizedName == "mother");
    }

    [Fact]
    public void Extract_Acronym_EmitsOrganizationHint()
    {
        var extractor = new EntityHintExtractor();

        var hints = extractor.Extract("I need context from MSFT before I decide", []);

        Assert.Contains(hints, h => h.Type == EntityHintType.Organization && h.NormalizedName == "msft");
    }

    [Fact]
    public void Extract_PronounCarriesRecentPerson()
    {
        var extractor = new EntityHintExtractor();
        var history = new List<ConversationTurn>
        {
            new() { Role = "user", Content = "I might schedule with John Smith", Timestamp = DateTimeOffset.UtcNow }
        };

        var hints = extractor.Extract("Should I meet him?", history);

        Assert.Contains(hints, h => h.Type == EntityHintType.Pronoun && h.NormalizedName.Contains("john", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Extract_PronounCarriesMultipleRelationshipCandidates()
    {
        var extractor = new EntityHintExtractor();
        var history = new List<ConversationTurn>
        {
            new() { Role = "user", Content = "I'm thinking of my mother today", Timestamp = DateTimeOffset.UtcNow.AddMinutes(-2) },
            new() { Role = "user", Content = "And my brother too!", Timestamp = DateTimeOffset.UtcNow.AddMinutes(-1) }
        };

        var hints = extractor.Extract("What do you know about her?", history);

        Assert.Contains(hints, h => h.Type == EntityHintType.Pronoun && h.NormalizedName == "mother");
        Assert.Contains(hints, h => h.Type == EntityHintType.Pronoun && h.NormalizedName == "brother");
    }
}
