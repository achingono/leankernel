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

    [Fact]
    public void IsVisible_throws_when_tool_is_null()
    {
        var act = () => _policy.IsVisible(null!, new ToolVisibilityContext());

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IsVisible_throws_when_context_is_null()
    {
        var tool = CreateTool(name: "wiki_search", category: "knowledge");

        var act = () => _policy.IsVisible(tool, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IsVisible_allows_matching_tool_name()
    {
        var tool = CreateTool(name: "wiki_search", category: "knowledge");
        var context = new ToolVisibilityContext
        {
            AllowedToolNames = new[] { "wiki_search" }
        };

        var isVisible = _policy.IsVisible(tool, context);

        isVisible.Should().BeTrue();
    }

    [Fact]
    public void IsVisible_default_allows_when_both_lists_empty()
    {
        var tool = CreateTool(name: "any_tool", category: "any_category");
        var context = new ToolVisibilityContext
        {
            AllowedToolNames = Array.Empty<string>(),
            AllowedCategories = Array.Empty<string>()
        };

        var isVisible = _policy.IsVisible(tool, context);

        isVisible.Should().BeTrue();
    }

    private static ToolDefinition CreateTool(string name, string? category) => new()
    {
        Name = name,
        Description = $"{name} description",
        Category = category
    };
}
