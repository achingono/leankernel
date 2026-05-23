using FluentAssertions;
using LeanKernel.Abstractions.Models;
using LeanKernel.Tools;

namespace LeanKernel.Tests.Unit.Tools;

public class ToolGovernancePolicyTests
{
    private readonly ToolGovernancePolicy _policy = new();

    [Fact]
    public void IsVisible_returns_true_when_no_filters_are_provided()
    {
        var tool = CreateTool(name: "wiki_search", category: "knowledge");

        var isVisible = _policy.IsVisible(tool, new ToolVisibilityContext());

        isVisible.Should().BeTrue();
    }

    [Fact]
    public void IsVisible_prefers_allowed_tool_names_over_categories()
    {
        var tool = CreateTool(name: "wiki_search", category: "knowledge");
        var context = new ToolVisibilityContext
        {
            AllowedToolNames = new[] { "WIKI_SEARCH" },
            AllowedCategories = new[] { "admin" }
        };

        var isVisible = _policy.IsVisible(tool, context);

        isVisible.Should().BeTrue();
    }

    [Fact]
    public void IsVisible_returns_false_when_name_filter_does_not_match()
    {
        var tool = CreateTool(name: "wiki_search", category: "knowledge");
        var context = new ToolVisibilityContext
        {
            AllowedToolNames = new[] { "wiki_read" }
        };

        var isVisible = _policy.IsVisible(tool, context);

        isVisible.Should().BeFalse();
    }

    [Fact]
    public void IsVisible_allows_matching_categories_when_tool_names_are_not_specified()
    {
        var tool = CreateTool(name: "wiki_search", category: "knowledge");
        var context = new ToolVisibilityContext
        {
            AllowedCategories = new[] { "KNOWLEDGE" }
        };

        var isVisible = _policy.IsVisible(tool, context);

        isVisible.Should().BeTrue();
    }

    [Fact]
    public void IsVisible_rejects_tools_without_a_matching_category()
    {
        var tool = CreateTool(name: "wiki_search", category: null);
        var context = new ToolVisibilityContext
        {
            AllowedCategories = new[] { "knowledge" }
        };

        var isVisible = _policy.IsVisible(tool, context);

        isVisible.Should().BeFalse();
    }

    private static ToolDefinition CreateTool(string name, string? category) => new()
    {
        Name = name,
        Description = $"{name} description",
        Category = category
    };
}
