using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
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
        var knowledge = Substitute.For<IKnowledgeSearchService>();
        var tool = CreateTool(knowledge);
        Assert.Equal("search_wiki", tool.Name);
    }

    [Fact]
    public void ParametersSchema_UsesKnowledgeSearchShape()
    {
        var knowledge = Substitute.For<IKnowledgeSearchService>();
        var tool = CreateTool(knowledge);
        Assert.NotEmpty(tool.ParametersSchema);
        Assert.Contains("query", tool.ParametersSchema);
        Assert.Contains("maxResults", tool.ParametersSchema);
        Assert.Contains("tags", tool.ParametersSchema);
        Assert.DoesNotContain("dimensions", tool.ParametersSchema);
    }

    [Fact]
    public async Task ExecuteAsync_WithResults_ReturnsFormatted()
    {
        var knowledge = Substitute.For<IKnowledgeSearchService>();
        knowledge.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<string?>())
            .Returns(new List<RelevanceScore>
            {
                new() { EntryId = "who-alice", Content = "Alice is a dev", Score = 0.91 }
            });

        var tool = CreateTool(knowledge);
        var result = await tool.ExecuteAsync("""{"query":"alice"}""", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("(score:", result.Output);
        Assert.Contains("Alice is a dev", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_NoResults_ReturnsMessage()
    {
        var knowledge = Substitute.For<IKnowledgeSearchService>();
        knowledge.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<string?>())
            .Returns(new List<RelevanceScore>());

        var tool = CreateTool(knowledge);
        var result = await tool.ExecuteAsync("""{"query":"nothing"}""", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("No matching wiki content found.", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_KnowledgeThrows_ReturnsError()
    {
        var knowledge = Substitute.For<IKnowledgeSearchService>();
        knowledge.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<string?>())
            .Returns<Task<List<RelevanceScore>>>(_ => throw new InvalidOperationException("fail"));

        var tool = CreateTool(knowledge);
        var result = await tool.ExecuteAsync("""{"query":"test"}""", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("fail", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidJson_ReturnsError()
    {
        var knowledge = Substitute.For<IKnowledgeSearchService>();
        var tool = CreateTool(knowledge);
        var result = await tool.ExecuteAsync("not json", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Invalid parameters JSON", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_AlwaysUsesWikiSourceType_AndAddsWikiTag()
    {
        var knowledge = Substitute.For<IKnowledgeSearchService>();
        knowledge.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<string?>())
            .Returns(new List<RelevanceScore>());

        var tool = CreateTool(knowledge);
        await tool.ExecuteAsync("""{"query":"alice","maxResults":3,"tags":["people"]}""", CancellationToken.None);

        await knowledge.Received(1).SearchAsync(
            "alice",
            Arg.Is<IReadOnlyList<string>>(tags => tags.Contains("people") && tags.Contains("wiki")),
            3,
            Arg.Any<CancellationToken>(),
            "wiki");
    }

    [Fact]
    public async Task ExecuteAsync_UsesConfiguredDefaultTags_WhenTagsNotProvided()
    {
        var knowledge = Substitute.For<IKnowledgeSearchService>();
        knowledge.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<string?>())
            .Returns(new List<RelevanceScore>());

        var config = new LeanKernelConfig
        {
            Knowledge = new KnowledgeConfig
            {
                DefaultDocumentTags = ["general", "internal"]
            }
        };
        var tool = new WikiQueryTool(knowledge, Options.Create(config));
        await tool.ExecuteAsync("""{"query":"alice"}""", CancellationToken.None);

        await knowledge.Received(1).SearchAsync(
            "alice",
            Arg.Is<IReadOnlyList<string>>(tags =>
                tags.SequenceEqual(new[] { "general", "internal", "wiki" })),
            5,
            Arg.Any<CancellationToken>(),
            "wiki");
    }

    [Fact]
    public async Task ExecuteAsync_EntityWithoutDedicatedPage_ReturnsCrossMentionChunk()
    {
        var knowledge = Substitute.For<IKnowledgeSearchService>();
        knowledge.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<string?>())
            .Returns(new List<RelevanceScore>
            {
                new()
                {
                    EntryId = "context-now",
                    Content = "Huy has invited Alfero to join him; stance remains learn and contribute selectively.",
                    Score = 0.84
                }
            });

        var tool = CreateTool(knowledge);
        var result = await tool.ExecuteAsync("""{"query":"Huy"}""", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("Huy has invited Alfero", result.Output);
    }

    private static WikiQueryTool CreateTool(IKnowledgeSearchService knowledge)
    {
        return new WikiQueryTool(knowledge, Options.Create(new LeanKernelConfig()));
    }
}
