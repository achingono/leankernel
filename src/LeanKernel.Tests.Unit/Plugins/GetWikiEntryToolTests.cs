using LeanKernel.Core.Enums;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Plugins.BuiltIn;
using NSubstitute;

namespace LeanKernel.Tests.Unit.Plugins;

public class GetWikiEntryToolTests
{
    [Fact]
    public void Metadata_IsExpected()
    {
        var wiki = Substitute.For<IWikiStore>();
        var tool = new GetWikiEntryTool(wiki);
        Assert.Equal("get_wiki_entry", tool.Name);
        Assert.Contains("entryId", tool.ParametersSchema);
        Assert.Contains("dimension", tool.ParametersSchema);
        Assert.Contains("subject", tool.ParametersSchema);
    }

    [Fact]
    public async Task ExecuteAsync_ByEntryId_ReturnsFormattedEntry()
    {
        var wiki = Substitute.For<IWikiStore>();
        wiki.GetAsync("who-jane-doe", Arg.Any<CancellationToken>())
            .Returns(new WikiEntry
            {
                Id = "who-jane-doe",
                Dimension = WikiDimension.Who,
                Subject = "Jane Doe",
                Summary = "Senior manager",
                Aliases = ["J. Doe"],
                Tags = ["management"],
                Facts =
                [
                    new WikiFact
                    {
                        Claim = "Leads platform strategy",
                        Confidence = 0.9,
                        Source = "org-chart.md"
                    }
                ]
            });

        var tool = new GetWikiEntryTool(wiki);
        var result = await tool.ExecuteAsync("""{"entryId":"who-jane-doe"}""", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("# Jane Doe", result.Output);
        Assert.Contains("Leads platform strategy", result.Output);
        Assert.Contains("J. Doe", result.Output);
        await wiki.Received(1).GetAsync("who-jane-doe", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ByDimensionAndSubject_BuildsSlugLookup()
    {
        var wiki = Substitute.For<IWikiStore>();
        wiki.GetAsync("who-jane-doe", Arg.Any<CancellationToken>())
            .Returns(new WikiEntry
            {
                Id = "who-jane-doe",
                Dimension = WikiDimension.Who,
                Subject = "Jane Doe"
            });

        var tool = new GetWikiEntryTool(wiki);
        var result = await tool.ExecuteAsync("""{"dimension":"who","subject":"Jane Doe"}""", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("who-jane-doe", result.Output);
        await wiki.Received(1).GetAsync("who-jane-doe", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_FallsBackToDimensionList_WhenCanonicalIdMisses()
    {
        var wiki = Substitute.For<IWikiStore>();
        wiki.GetAsync("who-jd", Arg.Any<CancellationToken>()).Returns((WikiEntry?)null);
        wiki.ListByDimensionAsync(WikiDimension.Who, Arg.Any<CancellationToken>())
            .Returns(new List<WikiEntry>
            {
                new()
                {
                    Id = "who-jane-doe",
                    Dimension = WikiDimension.Who,
                    Subject = "Jane Doe",
                    Aliases = ["JD"]
                }
            });

        var tool = new GetWikiEntryTool(wiki);
        var result = await tool.ExecuteAsync("""{"dimension":"who","subject":"JD"}""", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("Jane Doe", result.Output);
        await wiki.Received(1).ListByDimensionAsync(WikiDimension.Who, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_NotFound_ReturnsFriendlyMessage()
    {
        var wiki = Substitute.For<IWikiStore>();
        wiki.GetAsync("who-missing-person", Arg.Any<CancellationToken>()).Returns((WikiEntry?)null);

        var tool = new GetWikiEntryTool(wiki);
        var result = await tool.ExecuteAsync("""{"entryId":"who-missing-person"}""", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("No wiki entry found for the requested key.", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_EntityWithoutDedicatedPage_ReturnsNotFoundForExactLookup()
    {
        var wiki = Substitute.For<IWikiStore>();
        wiki.GetAsync("who-huy", Arg.Any<CancellationToken>()).Returns((WikiEntry?)null);
        wiki.ListByDimensionAsync(WikiDimension.Who, Arg.Any<CancellationToken>())
            .Returns(new List<WikiEntry>());

        var tool = new GetWikiEntryTool(wiki);
        var result = await tool.ExecuteAsync("""{"dimension":"who","subject":"Huy"}""", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("No wiki entry found for the requested key.", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_MissingIdentifiers_ReturnsError()
    {
        var wiki = Substitute.For<IWikiStore>();
        var tool = new GetWikiEntryTool(wiki);

        var result = await tool.ExecuteAsync("""{"dimension":"who"}""", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("subject", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidDimension_ReturnsError()
    {
        var wiki = Substitute.For<IWikiStore>();
        var tool = new GetWikiEntryTool(wiki);

        var result = await tool.ExecuteAsync("""{"dimension":"person","subject":"Jane"}""", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("dimension", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidJson_ReturnsError()
    {
        var wiki = Substitute.For<IWikiStore>();
        var tool = new GetWikiEntryTool(wiki);

        var result = await tool.ExecuteAsync("not json", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Invalid parameters JSON", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_WikiThrows_ReturnsError()
    {
        var wiki = Substitute.For<IWikiStore>();
        wiki.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<WikiEntry?>(_ => throw new InvalidOperationException("boom"));

        var tool = new GetWikiEntryTool(wiki);
        var result = await tool.ExecuteAsync("""{"entryId":"who-jane"}""", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("boom", result.Error);
    }
}
