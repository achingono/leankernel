using LeanKernel.Archivist;
using LeanKernel.Core.Models;

namespace LeanKernel.Tests.Unit.Archivist;

public class EntityHintExtractorTests
{
    [Fact]
    public void Extract_RelationshipTerms_EmitsPersonHint()
    {
        var extractor = new EntityHintExtractor();

        var hints = extractor.Extract("I'm thinking of my mother today", []);

        Assert.Contains(hints, h => h.Type == EntityHintType.Person && h.NormalizedName == "mother");
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

    }
}
