using Microsoft.Extensions.Options;
using NSubstitute;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Plugins.BuiltIn;
using Xunit;

namespace LeanKernel.Tests.Unit.Plugins;

public class KnowledgeSearchToolTests
{
    [Fact]
    public void Metadata_IsExpected()
    {
        var knowledge = Substitute.For<IKnowledgeSearchService>();
        var tool = new KnowledgeSearchTool(knowledge, Options.Create(new LeanKernelConfig()));

        Assert.Equal("search_knowledge", tool.Name);
        Assert.Contains("search your memory", tool.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("search the wiki", tool.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("query", tool.ParametersSchema);
        Assert.Contains("limit", tool.ParametersSchema);
        Assert.Contains("tags", tool.ParametersSchema);
    }

    [Fact]
    public async Task ExecuteAsync_JsonInput_UsesQueryClampAndTags()
    {
        var knowledge = Substitute.For<IKnowledgeSearchService>();
        knowledge.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<RelevanceScore>
            {
                new()
                {
                    EntryId = "doc-1",
                    Content = "Result content",
                    EstimatedTokens = 8,
                    Score = 0.92
                }
            }));

        var tool = new KnowledgeSearchTool(knowledge, Options.Create(new LeanKernelConfig()));

        var result = await tool.ExecuteAsync("""{"query":"docs","limit":999,"tags":["technical","books"]}""", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("Result content", result.Output);

        await knowledge.Received(1).SearchAsync(
            "docs",
            Arg.Is<IReadOnlyList<string>>(t => t.SequenceEqual(new[] { "technical", "books" })),
            50,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_PlainTextInput_UsesDefaults()
    {
        var knowledge = Substitute.For<IKnowledgeSearchService>();
        knowledge.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<RelevanceScore>()));

        var config = new LeanKernelConfig
        {
            Knowledge = new KnowledgeConfig
            {
                DefaultDocumentTags = ["general", "reference"]
            }
        };
        var tool = new KnowledgeSearchTool(knowledge, Options.Create(config));

        var result = await tool.ExecuteAsync("search this", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("No matching knowledge found", result.Output);

        await knowledge.Received(1).SearchAsync(
            "search this",
            Arg.Is<IReadOnlyList<string>>(t => t.SequenceEqual(new[] { "general", "reference", "wiki" })),
            5,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ServiceThrows_ReturnsError()
    {
        var knowledge = Substitute.For<IKnowledgeSearchService>();
        knowledge.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<Task<List<RelevanceScore>>>(_ => throw new InvalidOperationException("boom"));

        var tool = new KnowledgeSearchTool(knowledge, Options.Create(new LeanKernelConfig()));
        var result = await tool.ExecuteAsync("""{"query":"q"}""", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("boom", result.Error);
    }
}
