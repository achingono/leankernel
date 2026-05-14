using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Plugins.BuiltIn;
using NSubstitute;
using Xunit;

namespace LeanKernel.Tests.Unit.Plugins;

public class WikiQueryToolTests
{
    [Fact]
    public void Name_IsWikiQuery()
    {
        var wiki = Substitute.For<IWikiStore>();
        var tool = new WikiQueryTool(wiki);
        Assert.Equal("search_wiki", tool.Name);
    }

    [Fact]
    public void ParametersSchema_IsValidJson()
    {
        var wiki = Substitute.For<IWikiStore>();
        var tool = new WikiQueryTool(wiki);
        Assert.NotEmpty(tool.ParametersSchema);
        Assert.Contains("query", tool.ParametersSchema);
    }

    [Fact]
    public async Task ExecuteAsync_WithResults_ReturnsFormatted()
    {
        var wiki = Substitute.For<IWikiStore>();
        var entry = new WikiEntry
        {
            Id = "who-alice", Dimension = Core.Enums.WikiDimension.Who,
            Subject = "Alice",
            Facts = [new WikiFact { Claim = "Alice is a dev", Confidence = 0.9 }]
        };
        wiki.QueryAsync(Arg.Any<WikiQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WikiEntry>>([entry]));

        var tool = new WikiQueryTool(wiki);
        var result = await tool.ExecuteAsync("""{"query":"alice"}""", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("Alice", result.Output);
        Assert.Contains("Alice is a dev", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_NoResults_ReturnsMessage()
    {
        var wiki = Substitute.For<IWikiStore>();
        wiki.QueryAsync(Arg.Any<WikiQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WikiEntry>>([]));

        var tool = new WikiQueryTool(wiki);
        var result = await tool.ExecuteAsync("""{"query":"nothing"}""", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("No matching", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_WikiThrows_ReturnsError()
    {
        var wiki = Substitute.For<IWikiStore>();
        wiki.QueryAsync(Arg.Any<WikiQuery>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<WikiEntry>>(x => throw new InvalidOperationException("fail"));

        var tool = new WikiQueryTool(wiki);
        var result = await tool.ExecuteAsync("""{"query":"test"}""", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("fail", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidJson_ReturnsError()
    {
        var wiki = Substitute.For<IWikiStore>();
        var tool = new WikiQueryTool(wiki);
        var result = await tool.ExecuteAsync("not json", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Invalid parameters JSON", result.Error);
    }
}
