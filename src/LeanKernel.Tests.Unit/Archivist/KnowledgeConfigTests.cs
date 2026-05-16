using LeanKernel.Core.Configuration;
using Xunit;

namespace LeanKernel.Tests.Unit.Archivist;

public class KnowledgeConfigTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var config = new KnowledgeConfig();

        Assert.True(config.Enabled);
        Assert.Equal("LEANKERNEL_knowledge", config.CollectionName);
        Assert.Equal("LEANKERNEL_knowledge", config.WikiCollectionName);
        Assert.Equal("documents", config.DocumentsCollectionName);
        Assert.Equal(1536, config.EmbeddingDimension);
        Assert.Equal("/app/data/agents/main/documents", config.DocumentsPath);
        Assert.Equal(["general"], config.DefaultDocumentTags);
        Assert.Empty(config.AgentScopes);
        Assert.Empty(config.TagRules);
    }

    [Fact]
    public void AgentScopeConfig_Binds()
    {
        var scope = new AgentScopeConfig
        {
            Tags = ["wiki", "technical"],
            Description = "Code agent"
        };

        Assert.Equal(2, scope.Tags.Length);
        Assert.Contains("wiki", scope.Tags);
        Assert.Contains("technical", scope.Tags);
    }

    [Fact]
    public void TagRule_Binds()
    {
        var rule = new TagRule
        {
            PathPattern = "documents/technical/**",
            Tags = ["technical", "api-docs"]
        };

        Assert.Equal("documents/technical/**", rule.PathPattern);
        Assert.Equal(2, rule.Tags.Length);
    }

    [Fact]
    public void LeanKernelConfig_IncludesKnowledge()
    {
        var config = new LeanKernelConfig();
        Assert.NotNull(config.Knowledge);
        Assert.True(config.Knowledge.Enabled);
    }

    [Fact]
    public void LeanKernelConfig_ContextDefaultsIncludeEntityTuning()
    {
        var config = new LeanKernelConfig();

        Assert.Equal(0.45, config.Context.EntitySubjectBoost);
        Assert.Equal(0.35, config.Context.SupportingEntityThreshold);
        Assert.Equal(1, config.Context.EntityExpansionDepth);
        Assert.Equal(0.72, config.Context.LowConfidenceFallbackThreshold);
        Assert.Equal(40, config.Context.DeprioritizedRecallMaxResults);
        Assert.Equal(0.78, config.Context.AmbiguityLowConfidenceThreshold);
        Assert.Equal(0.10, config.Context.AmbiguityConfidenceGapThreshold);
    }

    [Fact]
    public void WildcardScope_Representation()
    {
        var scope = new AgentScopeConfig { Tags = ["*"] };
        Assert.Single(scope.Tags);
        Assert.Equal("*", scope.Tags[0]);
    }
}
