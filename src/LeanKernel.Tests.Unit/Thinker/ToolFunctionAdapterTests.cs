using Microsoft.Extensions.Logging.Abstractions;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Thinker;

namespace LeanKernel.Tests.Unit.Thinker;

public class ToolFunctionAdapterTests
{
    [Fact]
    public void BuildTools_EmptyRegistry_ReturnsEmpty()
    {
        var registry = new FakeToolRegistry([]);
        var adapter = new ToolFunctionAdapter(registry, NullLogger<ToolFunctionAdapter>.Instance);

        var tools = adapter.BuildTools();

        Assert.Empty(tools);
    }

    [Fact]
    public void BuildTools_PreservesToolNameAndDescription()
    {
        var tool = new FakeTool("web_search", "Search the web", success: true, output: "result");
        var registry = new FakeToolRegistry([tool]);
        var adapter = new ToolFunctionAdapter(registry, NullLogger<ToolFunctionAdapter>.Instance);

        var tools = adapter.BuildTools();

        Assert.Single(tools);
        // AIFunctionFactory wraps as AIFunction which inherits AITool
        var aiFunc = Assert.IsAssignableFrom<Microsoft.Extensions.AI.AIFunction>(tools[0]);
        Assert.Equal("web_search", aiFunc.Name);
        Assert.Equal("Search the web", aiFunc.Description);
    }

    [Fact]
    public void BuildTools_MultipleTools_AllConverted()
    {
        var tools = new[]
        {
            new FakeTool("tool_a", "Tool A", success: true, output: "a"),
            new FakeTool("tool_b", "Tool B", success: true, output: "b"),
            new FakeTool("tool_c", "Tool C", success: true, output: "c")
        };
        var registry = new FakeToolRegistry(tools);
        var adapter = new ToolFunctionAdapter(registry, NullLogger<ToolFunctionAdapter>.Instance);

        var result = adapter.BuildTools();

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void BuildTools_OperationsTool_ExpandsToOneAIFunctionPerOperation()
    {
        var tool = new FakeOperationsTool("doughray", "Finance skill", [
            new ToolOperationDescriptor("health", "Check health", "{}"),
            new ToolOperationDescriptor("list_accounts", "List accounts", "{}")
        ]);
        var registry = new FakeToolRegistry([tool]);
        var adapter = new ToolFunctionAdapter(registry, NullLogger<ToolFunctionAdapter>.Instance);

        var tools = adapter.BuildTools();

        Assert.Equal(2, tools.Count);
        var names = tools.Select(t => t.Name).ToList();
        Assert.Contains("doughray__health", names);
        Assert.Contains("doughray__list_accounts", names);
    }

    [Fact]
    public void BuildTools_OperationsTool_FunctionDescriptionsIncludeSkillName()
    {
        var tool = new FakeOperationsTool("doughray", "Finance skill", [
            new ToolOperationDescriptor("health", "Check API health.", "{}")
        ]);
        var registry = new FakeToolRegistry([tool]);
        var adapter = new ToolFunctionAdapter(registry, NullLogger<ToolFunctionAdapter>.Instance);

        var tools = adapter.BuildTools();

        Assert.Single(tools);
        Assert.Equal("Check API health. (skill: doughray)", tools[0].Description);
    }

    [Fact]
    public void BuildTools_MixedTools_ExpandsOperationsToolAndKeepsSimpleTool()
    {
        var simpleTool = new FakeTool("wiki", "Wiki search", success: true, output: "result");
        var opsTool = new FakeOperationsTool("doughray", "Finance", [
            new ToolOperationDescriptor("health", "Health check.", "{}"),
            new ToolOperationDescriptor("summary", "Dashboard summary.", "{}")
        ]);
        var registry = new FakeToolRegistry([simpleTool, opsTool]);
        var adapter = new ToolFunctionAdapter(registry, NullLogger<ToolFunctionAdapter>.Instance);

        var tools = adapter.BuildTools();

        // wiki stays as 1 function; doughray expands to 2
        Assert.Equal(3, tools.Count);
        var names = tools.Select(t => t.Name).ToList();
        Assert.Contains("wiki", names);
        Assert.Contains("doughray__health", names);
        Assert.Contains("doughray__summary", names);
    }

    [Fact]
    public void BuildTools_OperationsTool_FunctionSchemaContainsOperationFields()
    {
        const string schema = """{"type":"object","properties":{"listId":{"type":"string"}}}""";
        var tool = new FakeOperationsTool("ms-todo", "To-do skill", [
            new ToolOperationDescriptor("task_list", "List tasks.", schema)
        ]);
        var registry = new FakeToolRegistry([tool]);
        var adapter = new ToolFunctionAdapter(registry, NullLogger<ToolFunctionAdapter>.Instance);

        var tools = adapter.BuildTools();

        Assert.Single(tools);
        var aiFunc = Assert.IsAssignableFrom<Microsoft.Extensions.AI.AIFunction>(tools[0]);
        var jsonSchema = aiFunc.JsonSchema.ToString();
        Assert.Contains("listId", jsonSchema);
    }

    private sealed class FakeOperationsTool(
        string name, string description, IReadOnlyList<ToolOperationDescriptor> operations) : IOperationsTool
    {
        public string Name => name;
        public string Description => description;
        public string Category => "general";
        public string ParametersSchema => "{}";
        public IReadOnlyList<ToolOperationDescriptor> Operations => operations;

        public Task<ToolResult> ExecuteAsync(string parametersJson, CancellationToken ct) =>
            Task.FromResult(new ToolResult { ToolName = name, Success = true, Output = "ok" });
    }

    private sealed class FakeTool(string name, string description, bool success, string? output) : ITool
    {
        public string Name => name;
        public string Description => description;
        public string Category => "general";
        public string ParametersSchema => "{}";

        public Task<ToolResult> ExecuteAsync(string parametersJson, CancellationToken ct) =>
            Task.FromResult(new ToolResult
            {
                ToolName = name,
                Success = success,
                Output = output,
                Error = success ? null : "error"
            });
    }

    private sealed class FakeToolRegistry(IEnumerable<ITool> tools) : IToolRegistry
    {
        public IReadOnlyDictionary<string, ITool> Tools { get; } =
            tools.ToDictionary(t => t.Name, t => t);

        public ITool? GetTool(string name) => Tools.GetValueOrDefault(name);
        public IEnumerable<string> GetToolNames() => Tools.Keys;
    }
}
