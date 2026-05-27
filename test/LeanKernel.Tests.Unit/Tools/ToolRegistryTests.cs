using FluentAssertions;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace LeanKernel.Tests.Unit.Tools;

public class ToolRegistryTests
{
    [Fact]
    public void GetTool_returns_case_insensitive_match()
    {
        var registry = CreateRegistry(
            CreateTool(name: "wiki_search", category: "knowledge"),
            CreateTool(name: "wiki_read", category: "knowledge"));

        var tool = registry.GetTool("WIKI_SEARCH");

        tool.Should().NotBeNull();
        tool!.Name.Should().Be("wiki_search");
    }

    [Fact]
    public void GetTool_returns_the_last_registered_definition_for_duplicate_names()
    {
        var registry = CreateRegistry(
            CreateTool(name: "wiki_search", category: "knowledge", description: "first"),
            CreateTool(name: "WIKI_SEARCH", category: "admin", description: "second"));

        var tool = registry.GetTool("wiki_search");

        tool.Should().NotBeNull();
        tool!.Description.Should().Be("second");
        tool.Category.Should().Be("admin");
    }

    [Fact]
    public void GetVisibleTools_filters_tools_using_the_governance_policy()
    {
        var registry = CreateRegistry(
            CreateTool(name: "wiki_search", category: "knowledge"),
            CreateTool(name: "admin_reset", category: "admin"));
        var context = new ToolVisibilityContext
        {
            AllowedCategories = new[] { "knowledge" }
        };

        var visibleTools = registry.GetVisibleTools(context);

        visibleTools.Should().ContainSingle();
        visibleTools[0].Name.Should().Be("wiki_search");
    }

    [Fact]
    public void AddLeanKernelTools_registers_the_built_in_registry_and_executor()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Mock.Of<IKnowledgeService>());

        services.AddLeanKernelTools();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IToolRegistry>();

        registry.GetTool("wiki_search").Should().NotBeNull();
        registry.GetTool("http_request").Should().NotBeNull();
        registry.GetTool("json_transform").Should().NotBeNull();
        registry.GetTool("csv_xlsx_read_write").Should().NotBeNull();
        registry.GetTool("database_query").Should().NotBeNull();
        provider.GetRequiredService<IToolExecutor>().Should().BeOfType<ToolExecutor>();
    }

    private static ToolRegistry CreateRegistry(params ToolDefinition[] tools)
    {
        return new ToolRegistry(new ToolGovernancePolicy(), tools, Mock.Of<ILogger<ToolRegistry>>());
    }

    private static ToolDefinition CreateTool(string name, string? category, string? description = null) => new()
    {
        Name = name,
        Description = description ?? $"{name} description",
        Category = category
    };
}
