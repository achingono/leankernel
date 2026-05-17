using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using LeanKernel.Archivist;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Enums;
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

    [Fact]
    public async Task RetrieveWikiLeanKernelsAsync_RelationshipHintMatchesStructuredFactContext()
    {
        var wiki = Substitute.For<IWikiStore>();
        var knowledge = Substitute.For<IKnowledgeSearchService>();

        var family = new WikiEntry
        {
            Id = "who-family",
            Dimension = WikiDimension.Who,
            Subject = "Family",
            Facts =
            [
                new WikiFact
                {
                    Claim = "Family remembrance guidance.",
                    Confidence = 0.95,
                    EstimatedTokens = 10,
                    Context = new WikiFactContext
                    {
                        Who = "mother and brother remembrance",
                        Why = "active grief support"
                    }
                }
            ],
            LastAccessed = DateTimeOffset.UtcNow
        };

        wiki.QueryAsync(Arg.Any<WikiQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WikiEntry>>([family]));
        knowledge.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<RelevanceScore>()));

        var retriever = new ContextCandidateRetriever(
            wiki,
            knowledge,
            Options.Create(new LeanKernelConfig()),
            NullLogger<ContextCandidateRetriever>.Instance);

        var query = new LeanKernelMessage
        {
            Id = "1",
            ChannelId = "c",
            SenderId = "u",
            Content = "I'm thinking of my mother today"
        };
        var dimensions = new HashSet<WikiDimension> { WikiDimension.When };
        var hints = new List<EntityHint>
        {
            new() { NormalizedName = "mother", Type = EntityHintType.Relationship, Confidence = 0.85 }
        };

        var results = await retriever.RetrieveWikiLeanKernelsAsync(query, dimensions, hints, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(ContextPriority.High, results[0].Priority);
    }

    [Fact]
    public async Task RetrieveWikiLeanKernelsAsync_CompactContentIncludesContextDetails()
    {
        var wiki = Substitute.For<IWikiStore>();
        var knowledge = Substitute.For<IKnowledgeSearchService>();

        var family = new WikiEntry
        {
            Id = "who-family",
            Dimension = WikiDimension.Who,
            Subject = "Family",
            Facts =
            [
                new WikiFact
                {
                    Claim = "Family remembrance guidance.",
                    Confidence = 0.95,
                    EstimatedTokens = 10,
                    Context = new WikiFactContext
                    {
                        What = "Parents include Winnie Chingono (mother) and Jacob Chingono (father)."
                    }
                }
            ],
            LastAccessed = DateTimeOffset.UtcNow
        };

        wiki.QueryAsync(Arg.Any<WikiQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WikiEntry>>([family]));
        knowledge.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<RelevanceScore>()));

        var retriever = new ContextCandidateRetriever(
            wiki,
            knowledge,
            Options.Create(new LeanKernelConfig()),
            NullLogger<ContextCandidateRetriever>.Instance);

        var query = new LeanKernelMessage
        {
            Id = "1",
            ChannelId = "c",
            SenderId = "u",
            Content = "What do you know about my mother?"
        };
        var dimensions = new HashSet<WikiDimension> { WikiDimension.Who };
        var hints = new List<EntityHint>
        {
            new() { NormalizedName = "mother", Type = EntityHintType.Relationship, Confidence = 0.85 }
        };

        var results = await retriever.RetrieveWikiLeanKernelsAsync(query, dimensions, hints, CancellationToken.None);

        Assert.Single(results);
        Assert.Contains("Winnie", results[0].Content, StringComparison.OrdinalIgnoreCase);
        Assert.True(results[0].EstimatedTokens > 10);
    }

    [Fact]
    public async Task RetrieveVectorFallbackLeanKernelsAsync_ReturnsBroaderSemanticOrder()
    {
        var wiki = Substitute.For<IWikiStore>();
        var knowledge = Substitute.For<IKnowledgeSearchService>();

        var candidates = new List<RelevanceScore>
        {
            new() { EntryId = "doc-2", Content = "b", SemanticSimilarity = 0.4, Score = 0.2, SourceType = RelevanceSourceType.Vector },
            new() { EntryId = "doc-1", Content = "a", SemanticSimilarity = 0.9, Score = 0.1, SourceType = RelevanceSourceType.Vector },
            new() { EntryId = "doc-3", Content = "c", SemanticSimilarity = 0.7, Score = 0.5, SourceType = RelevanceSourceType.Vector }
        };
        knowledge.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(candidates));

        var retriever = new ContextCandidateRetriever(
            wiki,
            knowledge,
            Options.Create(new LeanKernelConfig { Context = new ContextConfig { DeprioritizedRecallMaxResults = 50 } }),
            NullLogger<ContextCandidateRetriever>.Instance);

        var query = new LeanKernelMessage { Id = "1", ChannelId = "c", SenderId = "u", Content = "unclear reference" };
        var results = await retriever.RetrieveVectorFallbackLeanKernelsAsync(query, ["*"], CancellationToken.None);

        Assert.Equal(3, results.Count);
        Assert.Equal("doc-1", results[0].EntryId);
        Assert.Equal("doc-3", results[1].EntryId);
        Assert.Equal("doc-2", results[2].EntryId);
    }
}
