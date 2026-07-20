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

    [Fact]
    public async Task ToAITool_InvokedWithValidJson_CallsHandler()
    {
        var invoked = false;
        var tool = new ToolDefinition
        {
            Name = "test_invoke",
            Description = "Test",
            Category = "test",
            Parameters = [new ToolParameter { Name = "x", Type = "string", Required = true }],
            Handler = (args, _) =>
            {
                invoked = true;
                return Task.FromResult(new ToolResult { ToolName = "test_invoke", Success = true, Output = "ok" });
            }
        };

        var aiTool = ToolDefinitionAIToolAdapter.ToAITool(tool) as AIFunction;
        aiTool.Should().NotBeNull();

        // The adapter wraps a (string argsJson, CancellationToken) delegate;
        // pass the JSON args as the 'argsJson' parameter
        await aiTool!.InvokeAsync(new AIFunctionArguments { ["argsJson"] = """{"x":"hello"}""" });

        invoked.Should().BeTrue();
    }

    [Fact]
    public async Task ToAITool_InvokedWithEmptyArgs_UsesEmptyDictionary()
    {
        var capturedArgs = (IReadOnlyDictionary<string, object?>?)null;
        var tool = new ToolDefinition
        {
            Name = "no_args",
            Description = "No args",
            Category = "test",
            Parameters = [],
            Handler = (args, _) =>
            {
                capturedArgs = args;
                return Task.FromResult(new ToolResult { ToolName = "no_args", Success = true, Output = "ok" });
            }
        };

        var aiTool = ToolDefinitionAIToolAdapter.ToAITool(tool) as AIFunction;
        await aiTool!.InvokeAsync(new AIFunctionArguments { ["argsJson"] = string.Empty });

        capturedArgs.Should().NotBeNull();
        capturedArgs.Should().BeEmpty();
    }
}