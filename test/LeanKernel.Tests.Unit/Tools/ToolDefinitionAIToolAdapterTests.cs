using System.Text.Json;
using FluentAssertions;
using LeanKernel.Logic.Tools;
using Microsoft.Extensions.AI;
using Xunit;

namespace LeanKernel.Tests.Unit.Tools;

public class ToolDefinitionAIToolAdapterTests
{
    private static ToolDefinition MakeTool(string name, ToolResult returnValue) => new()
    {
        Name = name,
        Description = $"Test tool {name}",
        Category = "test",
        Parameters =
        [
            new ToolParameter { Name = "param", Type = "string", Description = "A param", Required = true }
        ],
        Handler = (_, _) => Task.FromResult(returnValue)
    };

    [Fact]
    public void ToAITool_ReturnsAITool()
    {
        var tool = MakeTool("my_tool", new ToolResult { ToolName = "my_tool", Success = true, Output = "done" });

        var aiTool = ToolDefinitionAIToolAdapter.ToAITool(tool);

        aiTool.Should().NotBeNull();
    }

    [Fact]
    public void ToAITool_NullTool_Throws()
    {
        var act = () => ToolDefinitionAIToolAdapter.ToAITool(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ToAITools_ReturnsAllAdapted()
    {
        var tools = new[]
        {
            MakeTool("a", new ToolResult { ToolName = "a", Success = true, Output = "A" }),
            MakeTool("b", new ToolResult { ToolName = "b", Success = true, Output = "B" })
        };

        var aiTools = ToolDefinitionAIToolAdapter.ToAITools(tools).ToList();

        aiTools.Should().HaveCount(2);
    }

    [Fact]
    public void ToAITools_NullList_Throws()
    {
        var act = () => ToolDefinitionAIToolAdapter.ToAITools(null!).ToList();
        act.Should().Throw<ArgumentNullException>();
    }
}
