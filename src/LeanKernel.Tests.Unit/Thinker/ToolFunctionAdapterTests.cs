using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.AI;
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

    [Fact]
    public void BuildTools_SimpleTool_FunctionSchemaContainsToolParameters()
    {
        const string schema = """{"type":"object","properties":{"path":{"type":"string"},"maxLines":{"type":"integer"}},"required":["path"]}""";
        var tool = new FakeTool("file_read", "Read file", success: true, output: "ok", parametersSchema: schema);
        var registry = new FakeToolRegistry([tool]);
        var adapter = new ToolFunctionAdapter(registry, NullLogger<ToolFunctionAdapter>.Instance);

        var tools = adapter.BuildTools();

        var aiFunc = Assert.IsAssignableFrom<Microsoft.Extensions.AI.AIFunction>(Assert.Single(tools));
        var jsonSchema = aiFunc.JsonSchema.ToString();
        Assert.Contains("path", jsonSchema);
        Assert.Contains("maxLines", jsonSchema);
        Assert.DoesNotContain("input", jsonSchema);
    }

    [Fact]
    public async Task BuildTools_SimpleTool_InvokesWithNamedSchemaArguments()
    {
        const string schema = """{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}""";
        var tool = new FakeTool("file_read", "Read file", success: true, output: "ok", parametersSchema: schema);
        var registry = new FakeToolRegistry([tool]);
        var adapter = new ToolFunctionAdapter(registry, NullLogger<ToolFunctionAdapter>.Instance);

        var aiFunc = Assert.IsAssignableFrom<Microsoft.Extensions.AI.AIFunction>(Assert.Single(adapter.BuildTools()));
        var result = await aiFunc.InvokeAsync(new AIFunctionArguments { ["path"] = "documents/profile.pdf" }, CancellationToken.None);

        Assert.Equal("ok", result?.ToString());
        Assert.Contains("\"path\":\"documents/profile.pdf\"", tool.LastParametersJson);
    }

    [Fact]
    public async Task BuildTools_SimpleTool_DeniedByExecutionAuthorizer_ReturnsError()
    {
        var tool = new FakeTool("file_write", "Write file", success: true, output: "ok");
        var registry = new FakeToolRegistry([tool]);
        var authorizer = new FakeToolExecutionAuthorizer("file_write", ToolExecutionAuthorizationResult.Deny("User permission required", "WriteFile"));
        var adapter = new ToolFunctionAdapter(registry, NullLogger<ToolFunctionAdapter>.Instance, authorizer);

        var tools = adapter.BuildTools();
        var aiFunc = Assert.IsAssignableFrom<Microsoft.Extensions.AI.AIFunction>(Assert.Single(tools));
        var result = await aiFunc.InvokeAsync(new AIFunctionArguments { ["path"] = "SELF.md" }, CancellationToken.None);

        Assert.Equal("Error: User permission required", result?.ToString());
    }

    [Fact]
    public async Task BuildTools_OperationsTool_DeniedByExecutionAuthorizer_ReturnsError()
    {
        var tool = new FakeOperationsTool("doughray", "Finance skill", [
            new ToolOperationDescriptor("health", "Check health", "{}")
        ]);
        var registry = new FakeToolRegistry([tool]);
        var authorizer = new FakeToolExecutionAuthorizer("doughray__health", ToolExecutionAuthorizationResult.Deny("Blocked", "HealthCheck"));
        var adapter = new ToolFunctionAdapter(registry, NullLogger<ToolFunctionAdapter>.Instance, authorizer);

        var tools = adapter.BuildTools();
        var aiFunc = Assert.IsAssignableFrom<Microsoft.Extensions.AI.AIFunction>(Assert.Single(tools));
        var result = await aiFunc.InvokeAsync(new AIFunctionArguments(), CancellationToken.None);

        Assert.Equal("Error: Blocked", result?.ToString());
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

    private sealed class FakeTool(
        string name,
        string description,
        bool success,
        string? output,
        string parametersSchema = "{}") : ITool
    {
        public string Name => name;
        public string Description => description;
        public string Category => "general";
        public string ParametersSchema => parametersSchema;

        public string? LastParametersJson { get; private set; }

        public Task<ToolResult> ExecuteAsync(string parametersJson, CancellationToken ct)
        {
            LastParametersJson = parametersJson;
            return Task.FromResult(new ToolResult
            {
                ToolName = name,
                Success = success,
                Output = output,
                Error = success ? null : "error"
            });
        }
    }

    private sealed class FakeToolRegistry(IEnumerable<ITool> tools) : IToolRegistry
    {
        public IReadOnlyDictionary<string, ITool> Tools { get; } =
            tools.ToDictionary(t => t.Name, t => t);

        public ITool? GetTool(string name) => Tools.GetValueOrDefault(name);
        public IEnumerable<string> GetToolNames() => Tools.Keys;
    }

    private sealed class FakeToolExecutionAuthorizer(string blockedToolName, ToolExecutionAuthorizationResult result) : IToolExecutionAuthorizer
    {
        public Task<ToolExecutionAuthorizationResult> AuthorizeAsync(string toolName, string parametersJson, CancellationToken ct)
        {
            return Task.FromResult(string.Equals(toolName, blockedToolName, StringComparison.OrdinalIgnoreCase)
                ? result
                : ToolExecutionAuthorizationResult.Allow());
        }
    }
}
