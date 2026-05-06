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
