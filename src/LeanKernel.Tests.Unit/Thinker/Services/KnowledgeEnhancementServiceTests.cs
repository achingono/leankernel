using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Thinker.Services;

namespace LeanKernel.Tests.Unit.Thinker.Services;

public sealed class KnowledgeEnhancementServiceTests
{
    [Fact]
    public async Task EnhanceResponseAsync_DoesNotAppendLowRelevanceInsights()
    {
        var knowledge = Substitute.For<IKnowledgeSearchService>();
        knowledge.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                new RelevanceScore
                {
                    EntryId = "unrelated.md",
                    Content = "unrelated",
                    Score = 0.12
                }
            ]);
        var service = CreateService(knowledge, minRelevanceThreshold: 0.65);

        var response = await service.EnhanceResponseAsync(
            "How do I find a document?",
            "Here is a sufficiently detailed response that should otherwise be eligible for enhancement.",
            CreateContext(),
            CancellationToken.None);

        Assert.DoesNotContain("Related insights from your knowledge base", response);
        Assert.DoesNotContain("unrelated", response);
    }

    [Fact]
    public async Task EnhanceResponseAsync_AppendsOnlyRelevantInsights()
    {
        var knowledge = Substitute.For<IKnowledgeSearchService>();
        knowledge.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                new RelevanceScore
                {
                    EntryId = "low.md",
                    Content = "low relevance",
                    Score = 0.20
                },
                new RelevanceScore
                {
                    EntryId = "high.md",
                    Content = "high relevance",
                    Score = 0.82
                }
            ]);
        var service = CreateService(knowledge, minRelevanceThreshold: 0.65);

        var response = await service.EnhanceResponseAsync(
            "How do I find a document?",
            "Here is a sufficiently detailed response that should otherwise be eligible for enhancement.",
            CreateContext(),
            CancellationToken.None);

        Assert.Contains("Related insights from your knowledge base", response);
        Assert.Contains("high", response);
        Assert.DoesNotContain("low", response);
    }

    private static KnowledgeEnhancementService CreateService(
        IKnowledgeSearchService knowledge,
        double minRelevanceThreshold)
    {
        var config = Options.Create(new LeanKernelConfig
        {
            Context = new ContextConfig
            {
                MinRelevanceThreshold = minRelevanceThreshold
            }
        });

        return new KnowledgeEnhancementService(
            knowledge,
            config,
            NullLogger<KnowledgeEnhancementService>.Instance);
    }

    private static ConversationContext CreateContext()
    {
        return new ConversationContext
        {
            SystemPrompt = "system",
            History = [],
            WikiLeanKernels = [],
            RetrievedLeanKernels = [],
            ActiveToolNames = []
        };
    }
}
