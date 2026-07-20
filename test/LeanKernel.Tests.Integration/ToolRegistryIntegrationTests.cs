using FluentAssertions;

using LeanKernel.Logic.Tools;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace LeanKernel.Tests.Integration;

/// <summary>
/// Integration tests for the tool registry and tool runtime startup.
/// Validates that the registry is registered and the governance/adapter path works in the real DI container.
/// </summary>
public class ToolRegistryIntegrationTests : IClassFixture<ToolsEnabledTestApplicationFactory>
{
    private readonly ToolsEnabledTestApplicationFactory _factory;

    public ToolRegistryIntegrationTests(ToolsEnabledTestApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void ToolRegistry_IsRegistered_AsSingleton()
    {
        using var scope = _factory.Services.CreateScope();
        var registry1 = scope.ServiceProvider.GetRequiredService<IToolRegistry>();
        var registry2 = _factory.Services.GetRequiredService<IToolRegistry>();

        registry1.Should().BeSameAs(registry2);
    }

    [Fact]
    public void ToolRegistry_WithToolsEnabled_HasBuiltInTools()
    {
        var registry = _factory.Services.GetRequiredService<IToolRegistry>();

        registry.Tools.Should().NotBeEmpty();
        registry.Tools.Select(t => t.Name).Should().Contain("web_search");
        registry.Tools.Select(t => t.Name).Should().Contain("file_search");
        registry.Tools.Select(t => t.Name).Should().Contain("calculate");
        registry.Tools.Select(t => t.Name).Should().Contain("count");
        registry.Tools.Select(t => t.Name).Should().Contain("sum");
        registry.Tools.Select(t => t.Name).Should().Contain("average");
        registry.Tools.Select(t => t.Name).Should().Contain("min_max");
        registry.Tools.Select(t => t.Name).Should().Contain("group_by");
    }

    [Fact]
    public void ToolRegistry_NoDuplicateNames()
    {
        var registry = _factory.Services.GetRequiredService<IToolRegistry>();
        var names = registry.Tools.Select(t => t.Name).ToList();
        names.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void ToolRegistry_AllToolsHaveHandlers()
    {
        var registry = _factory.Services.GetRequiredService<IToolRegistry>();

        foreach (var tool in registry.Tools)
        {
            tool.Handler.Should().NotBeNull($"tool '{tool.Name}' should have a handler");
        }
    }
}

/// <summary>
/// Integration tests validating tool call execution through the tool registry.
/// </summary>
public class ToolExecutionIntegrationTests : IClassFixture<ToolsEnabledTestApplicationFactory>
{
    private readonly IToolRegistry _registry;

    public ToolExecutionIntegrationTests(ToolsEnabledTestApplicationFactory factory)
    {
        _registry = factory.Services.GetRequiredService<IToolRegistry>();
    }

    [Fact]
    public async Task Calculate_ViaRegistry_ReturnsResult()
    {
        var tool = _registry.Tools.First(t => t.Name == "calculate");
        var args = new Dictionary<string, object?> { ["expression"] = "2 + 2" };

        var result = await tool.Handler(args, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Be("4");
    }

    [Fact]
    public async Task Count_ViaRegistry_ReturnsCount()
    {
        var tool = _registry.Tools.First(t => t.Name == "count");
        var args = new Dictionary<string, object?> { ["items"] = "[1,2,3]" };

        var result = await tool.Handler(args, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Be("3");
    }

    [Fact]
    public async Task Sum_ViaRegistry_ReturnsSum()
    {
        var tool = _registry.Tools.First(t => t.Name == "sum");
        var args = new Dictionary<string, object?> { ["items"] = "[10,20,30]" };

        var result = await tool.Handler(args, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Be("60");
    }

    [Fact]
    public async Task FileSearch_ViaRegistry_RejectsTraversal()
    {
        var tool = _registry.Tools.First(t => t.Name == "file_search");
        var args = new Dictionary<string, object?>
        {
            ["name"] = "test",
            ["path"] = "../../etc"
        };

        var result = await tool.Handler(args, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Access denied");
    }

    [Fact]
    public async Task ToolDefinitionAIToolAdapter_CanAdaptAllRegisteredTools()
    {
        foreach (var tool in _registry.Tools)
        {
            var aiTool = ToolDefinitionAIToolAdapter.ToAITool(tool);
            aiTool.Should().NotBeNull($"tool '{tool.Name}' should be adaptable");
        }
    }
}