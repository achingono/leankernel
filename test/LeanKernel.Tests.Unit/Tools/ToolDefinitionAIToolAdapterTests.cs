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

        await aiTool!.InvokeAsync(new AIFunctionArguments { ["x"] = "hello" });

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
        await aiTool!.InvokeAsync(new AIFunctionArguments());

        capturedArgs.Should().NotBeNull();
        capturedArgs.Should().BeEmpty();
    }

    [Fact]
    public void ToAITool_ExposesNameDescriptionAndSchema()
    {
        var tool = MakeTool("my_tool", new ToolResult { ToolName = "my_tool", Success = true, Output = "done" });

        var aiTool = (AIFunction)ToolDefinitionAIToolAdapter.ToAITool(tool);

        aiTool.Name.Should().Be("my_tool");
        aiTool.Description.Should().Contain("Parameters:");
        aiTool.JsonSchema.GetProperty("type").GetString().Should().Be("object");
        aiTool.JsonSchema.GetProperty("required").EnumerateArray().Select(item => item.GetString()).Should().Equal("param");
    }

    [Fact]
    public void ToAITool_DescriptionWithoutParameters_ReturnsRawDescription()
    {
        var tool = new ToolDefinition
        {
            Name = "plain",
            Description = "Plain description",
            Category = "test",
            Parameters = []
        };

        var aiTool = (AIFunction)ToolDefinitionAIToolAdapter.ToAITool(tool);

        aiTool.Description.Should().Be("Plain description");
    }

    [Fact]
    public void ToAITool_DescriptionWithParameters_ListsTypesAndRequiredState()
    {
        var tool = new ToolDefinition
        {
            Name = "described",
            Description = "Base description",
            Category = "test",
            Parameters =
            [
                new ToolParameter { Name = "req", Type = "integer", Description = "count", Required = true },
                new ToolParameter { Name = "opt", Type = "boolean", Required = false }
            ]
        };

        var aiTool = (AIFunction)ToolDefinitionAIToolAdapter.ToAITool(tool);
        var description = aiTool.Description;

        description.Should().StartWith("Base description");
        description.Should().Contain("req (integer, required) - count");
        description.Should().Contain("opt (boolean, optional)");
    }

    [Fact]
    public void ToAITool_JsonSchema_NormalizesDeclaredTypes()
    {
        var tool = new ToolDefinition
        {
            Name = "typed",
            Description = "Typed",
            Category = "test",
            Parameters =
            [
                new ToolParameter { Name = "a", Type = "array" },
                new ToolParameter { Name = "b", Type = "boolean" },
                new ToolParameter { Name = "c", Type = "integer" },
                new ToolParameter { Name = "d", Type = "number" },
                new ToolParameter { Name = "e", Type = "object" },
                new ToolParameter { Name = "f", Type = "  Unknown " }
            ]
        };

        var aiTool = (AIFunction)ToolDefinitionAIToolAdapter.ToAITool(tool);
        var properties = aiTool.JsonSchema.GetProperty("properties");

        properties.GetProperty("a").GetProperty("type").GetString().Should().Be("array");
        properties.GetProperty("b").GetProperty("type").GetString().Should().Be("boolean");
        properties.GetProperty("c").GetProperty("type").GetString().Should().Be("integer");
        properties.GetProperty("d").GetProperty("type").GetString().Should().Be("number");
        properties.GetProperty("e").GetProperty("type").GetString().Should().Be("object");
        properties.GetProperty("f").GetProperty("type").GetString().Should().Be("string");
        aiTool.JsonSchema.TryGetProperty("required", out _).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_WithoutHandler_ReturnsMissingHandlerMessage()
    {
        var tool = new ToolDefinition
        {
            Name = "ghost",
            Description = "No handler",
            Category = "test"
        };

        var aiTool = (AIFunction)ToolDefinitionAIToolAdapter.ToAITool(tool);

        var result = await aiTool.InvokeAsync(new AIFunctionArguments());

        result.Should().Be("Tool 'ghost' has no execution handler");
    }

    [Fact]
    public async Task InvokeAsync_FailedResult_ReturnsError()
    {
        var tool = MakeTool("failing", new ToolResult { ToolName = "failing", Success = false, Error = "boom" });

        var aiTool = (AIFunction)ToolDefinitionAIToolAdapter.ToAITool(tool);

        var result = await aiTool.InvokeAsync(new AIFunctionArguments());

        result.Should().Be("boom");
    }

    [Fact]
    public async Task InvokeAsync_FailedResultWithoutError_ReturnsGenericMessage()
    {
        var tool = MakeTool("failing", new ToolResult { ToolName = "failing", Success = false });

        var aiTool = (AIFunction)ToolDefinitionAIToolAdapter.ToAITool(tool);

        var result = await aiTool.InvokeAsync(new AIFunctionArguments());

        result.Should().Be("Tool 'failing' failed.");
    }

    [Fact]
    public async Task InvokeAsync_SuccessWithNullOutput_ReturnsEmptyString()
    {
        var tool = MakeTool("empty_out", new ToolResult { ToolName = "empty_out", Success = true });

        var aiTool = (AIFunction)ToolDefinitionAIToolAdapter.ToAITool(tool);

        var result = await aiTool.InvokeAsync(new AIFunctionArguments());

        result.Should().Be(string.Empty);
    }
}
