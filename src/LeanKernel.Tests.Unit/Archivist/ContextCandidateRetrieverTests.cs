using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using LeanKernel.Archivist;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Tests.Unit.Archivist;

public class ContextCandidateRetrieverTests
{
    [Fact]
    public async Task RetrieveVectorLeanKernelsAsync_ReranksAndTrims()
    {
        var wiki = Substitute.For<IWikiStore>();
        var knowledge = Substitute.For<IKnowledgeSearchService>();
        var reranker = Substitute.For<IReranker>();

        var query = new LeanKernelMessage { Id = "1", ChannelId = "c", SenderId = "u", Content = "alpha" };
        var baseResults = new List<RelevanceScore>
        {
            new() { EntryId = "doc-1", Content = "alpha", Score = 0.2, SemanticSimilarity = 0.2, SourceType = RelevanceSourceType.Vector },
            new() { EntryId = "doc-2", Content = "beta", Score = 0.1, SemanticSimilarity = 0.1, SourceType = RelevanceSourceType.Vector },
            new() { EntryId = "doc-3", Content = "gamma", Score = 0.05, SemanticSimilarity = 0.05, SourceType = RelevanceSourceType.Vector },
        };
        knowledge.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(baseResults);

        reranker.RerankAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<RelevanceScore>>(), Arg.Any<CancellationToken>())
            .Returns(new List<RelevanceScore>
            {
                baseResults[2] with { Score = 0.95 },
                baseResults[0] with { Score = 0.85 },
                baseResults[1] with { Score = 0.60 },
            });

        var config = Options.Create(new LeanKernelConfig
        {
            Context = new ContextConfig
            {
                Reranker = new RerankerConfig { Enabled = true, TopN = 3, TopK = 2, TimeoutMs = 5000, MinAcceptanceScore = 0.7 }
            }
        });

        var retriever = new ContextCandidateRetriever(wiki, knowledge, config, NullLogger<ContextCandidateRetriever>.Instance, reranker);
        var results = await retriever.RetrieveVectorLeanKernelsAsync(query, ["wiki"], CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Equal("doc-3", results[0].EntryId);
        Assert.Equal("doc-1", results[1].EntryId);
    }

    [Fact]
    public async Task RetrieveVectorLeanKernelsAsync_RerankerFailureFallsBackToDeterministicOrder()
    {
        var wiki = Substitute.For<IWikiStore>();
        var knowledge = Substitute.For<IKnowledgeSearchService>();
        var reranker = Substitute.For<IReranker>();

        var query = new LeanKernelMessage { Id = "1", ChannelId = "c", SenderId = "u", Content = "alpha" };
        var baseResults = new List<RelevanceScore>
        {
            new() { EntryId = "doc-2", Content = "beta", Score = 0.4, SemanticSimilarity = 0.4, SourceType = RelevanceSourceType.Vector },
            new() { EntryId = "doc-1", Content = "alpha", Score = 0.9, SemanticSimilarity = 0.9, SourceType = RelevanceSourceType.Vector },
        };
        knowledge.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(baseResults);
        reranker.RerankAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<RelevanceScore>>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<RelevanceScore>>>(_ => throw new TimeoutException("boom"));

        var config = Options.Create(new LeanKernelConfig
        {
            Context = new ContextConfig
            {
                Reranker = new RerankerConfig { Enabled = true, TopN = 4, TopK = 2, TimeoutMs = 50 }
            }
        });

        var retriever = new ContextCandidateRetriever(wiki, knowledge, config, NullLogger<ContextCandidateRetriever>.Instance, reranker);
        var results = await retriever.RetrieveVectorLeanKernelsAsync(query, ["wiki"], CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Equal("doc-1", results[0].EntryId);
        Assert.Equal("doc-2", results[1].EntryId);
    }
}

