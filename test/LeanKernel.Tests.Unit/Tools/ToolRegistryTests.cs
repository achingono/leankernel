using FluentAssertions;
using LeanKernel.Logic.Tools;
using Xunit;

namespace LeanKernel.Tests.Unit.Tools;

public class ToolRegistryTests
{
    [Fact]
    public void Register_NewTool_AddsToCollection()
    {
        var registry = new ToolRegistry();
        var tool = MakeTool("test_tool");

        registry.Register(tool);

        registry.Tools.Should().HaveCount(1);
        registry.Tools[0].Name.Should().Be("test_tool");
    }

    [Fact]
    public void Register_DuplicateName_Throws()
    {
        var registry = new ToolRegistry();
        registry.Register(MakeTool("dup"));

        var act = () => registry.Register(MakeTool("dup"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*dup*");
    }

    [Fact]
    public void TryRegister_NewTool_ReturnsTrue()
    {
        var registry = new ToolRegistry();
        var result = registry.TryRegister(MakeTool("tool_a"));

        result.Should().BeTrue();
    }

    [Fact]
    public void TryRegister_DuplicateName_ReturnsFalse()
    {
        var registry = new ToolRegistry();
        registry.TryRegister(MakeTool("tool_a"));

        var result = registry.TryRegister(MakeTool("tool_a"));

        result.Should().BeFalse();
    }

    [Fact]
    public void TryRegister_CaseInsensitive_ReturnsFalse()
    {
        var registry = new ToolRegistry();
        registry.TryRegister(MakeTool("web_search"));

        var result = registry.TryRegister(MakeTool("WEB_SEARCH"));

        result.Should().BeFalse();
    }

    [Fact]
    public void Contains_ExistingTool_ReturnsTrue()
    {
        var registry = new ToolRegistry();
        registry.Register(MakeTool("my_tool"));

        registry.Contains("my_tool").Should().BeTrue();
    }

    [Fact]
    public void Contains_MissingTool_ReturnsFalse()
    {
        var registry = new ToolRegistry();

        registry.Contains("missing").Should().BeFalse();
    }

    [Fact]
    public void Contains_CaseInsensitive()
    {
        var registry = new ToolRegistry();
        registry.Register(MakeTool("my_Tool"));

        registry.Contains("MY_TOOL").Should().BeTrue();
    }

    [Fact]
    public void Register_NullTool_Throws()
    {
        var registry = new ToolRegistry();
        var act = () => registry.Register(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Tools_ReturnsAllRegistered()
    {
        var registry = new ToolRegistry();
        registry.Register(MakeTool("a"));
        registry.Register(MakeTool("b"));
        registry.Register(MakeTool("c"));

        registry.Tools.Should().HaveCount(3);
    }

    private static ToolDefinition MakeTool(string name) => new()
    {
        Name = name,
        Description = "Test",
        Category = "test",
        Handler = (_, _) => Task.FromResult(new ToolResult { ToolName = name, Success = true })
    };
}
