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
        Assert.Equal(1536, config.EmbeddingDimension);
        Assert.Equal("/app/data/documents", config.DocumentsPath);
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
    public void WildcardScope_Representation()
    {
        var scope = new AgentScopeConfig { Tags = ["*"] };
        Assert.Single(scope.Tags);
        Assert.Equal("*", scope.Tags[0]);
    }
}
