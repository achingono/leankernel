using FluentAssertions;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Tools;
using Xunit;

namespace LeanKernel.Tests.Unit.Tools;

public class ToolGovernancePolicyTests
{
    private static ToolDefinition MakeTool(string name, string category) => new()
    {
        Name = name,
        Category = category,
        Description = "Test",
        Handler = (_, _) => Task.FromResult(new ToolResult { ToolName = name, Success = true })
    };

    [Fact]
    public void IsAllowed_EmptyAllowlists_AllowsAll()
    {
        var settings = new ToolSettings
        {
            AllowedToolNames = [],
            AllowedCategories = []
        };
        var policy = new ToolGovernancePolicy(settings);

        policy.IsAllowed(MakeTool("web_search", "internet")).Should().BeTrue();
        policy.IsAllowed(MakeTool("calculate", "calculation")).Should().BeTrue();
    }

    [Fact]
    public void IsAllowed_NameAllowlist_OnlyAllowsNamedTools()
    {
        var settings = new ToolSettings
        {
            AllowedToolNames = ["web_search", "calculate"],
            AllowedCategories = ["internet"]
        };
        var policy = new ToolGovernancePolicy(settings);

        policy.IsAllowed(MakeTool("web_search", "internet")).Should().BeTrue();
        policy.IsAllowed(MakeTool("calculate", "calculation")).Should().BeTrue();
        policy.IsAllowed(MakeTool("file_search", "filesystem")).Should().BeFalse();
        // Name list takes precedence over category
        policy.IsAllowed(MakeTool("unknown", "internet")).Should().BeFalse();
    }

    [Fact]
    public void IsAllowed_CategoryAllowlist_OnlyAllowsNamedCategories()
    {
        var settings = new ToolSettings
        {
            AllowedToolNames = [],
            AllowedCategories = ["internet", "knowledge"]
        };
        var policy = new ToolGovernancePolicy(settings);

        policy.IsAllowed(MakeTool("web_search", "internet")).Should().BeTrue();
        policy.IsAllowed(MakeTool("wiki_search", "knowledge")).Should().BeTrue();
        policy.IsAllowed(MakeTool("calculate", "calculation")).Should().BeFalse();
    }

    [Fact]
    public void Filter_AppliesPolicy()
    {
        var settings = new ToolSettings
        {
            AllowedToolNames = ["web_search"]
        };
        var policy = new ToolGovernancePolicy(settings);

        var tools = new[]
        {
            MakeTool("web_search", "internet"),
            MakeTool("file_search", "filesystem"),
            MakeTool("calculate", "calculation")
        };

        var allowed = policy.Filter(tools).ToList();

        allowed.Should().HaveCount(1);
        allowed[0].Name.Should().Be("web_search");
    }

    [Fact]
    public void IsAllowed_NullTool_Throws()
    {
        var policy = new ToolGovernancePolicy(new ToolSettings());
        var act = () => policy.IsAllowed(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Filter_NullList_Throws()
    {
        var policy = new ToolGovernancePolicy(new ToolSettings());
        var act = () => policy.Filter(null!).ToList();
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullSettings_Throws()
    {
        var act = () => new ToolGovernancePolicy(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IsAllowed_NameAllowlist_CaseInsensitive()
    {
        var settings = new ToolSettings { AllowedToolNames = ["WEB_SEARCH"] };
        var policy = new ToolGovernancePolicy(settings);

        policy.IsAllowed(MakeTool("web_search", "internet")).Should().BeTrue();
    }
}
