using Microsoft.Extensions.Options;
using NSubstitute;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Plugins.BuiltIn;

namespace LeanKernel.Tests.Unit.Plugins;

public class DocumentSearchToolTests
{
    [Fact]
    public void Metadata_IsExpected()
    {
        var knowledge = Substitute.For<IKnowledgeSearchService>();
        var tool = new DocumentSearchTool(knowledge, Options.Create(new LeanKernelConfig()));
        Assert.Equal("search_documents", tool.Name);
        Assert.Contains("documents-only", tool.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("query", tool.ParametersSchema);
    }

    [Fact]
    public async Task ExecuteAsync_UsesDocumentScope()
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
                new() { EntryId = "doc-1", Content = "Document chunk", Score = 0.9, SemanticSimilarity = 0.9, SourceType = RelevanceSourceType.Vector, KnowledgeSource = KnowledgeSourceType.Document }
            });

        var tool = new DocumentSearchTool(knowledge, Options.Create(new LeanKernelConfig()));
        var result = await tool.ExecuteAsync("""{"query":"chunk","maxResults":3}""", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("Document chunk", result.Output);
        await knowledge.Received(1).SearchAsync(
            "chunk",
            Arg.Any<IReadOnlyList<string>>(),
            3,
            Arg.Any<CancellationToken>(),
            "document");
    }
}
