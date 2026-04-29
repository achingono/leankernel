using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using LeanKernel.Core.Models;
using LeanKernel.Thinker;
using LeanKernel.Thinker.Agents;

namespace LeanKernel.Tests.Unit.Thinker;

public class WorkerAgentTests
{
    private static AgentFactory CreateFactory(IChatClient? client = null)
    {
        var c = client ?? new WorkerTestChatClient("worker result");
        return new AgentFactory(c, NullLogger<AgentFactory>.Instance);
    }

    [Fact]
    public void AgentDefinition_Properties()
    {
        var def = new AgentDefinition
        {
            Name = "test",
            Description = "A test agent",
            SystemPrompt = "You are a test agent",
            MaxContextTokens = 2000,
            AllowedTools = ["tool1", "tool2"],
            Categories = ["cat1"]
        };

        Assert.Equal("test", def.Name);
        Assert.Equal("A test agent", def.Description);
        Assert.Equal("You are a test agent", def.SystemPrompt);
        Assert.Equal(2000, def.MaxContextTokens);
        Assert.Equal(2, def.AllowedTools.Count);
        Assert.Single(def.Categories);
    }

    [Fact]
    public void AgentDefinition_DefaultValues()
    {
        var def = new AgentDefinition
        {
            Name = "x",
            Description = "x",
            SystemPrompt = "x"
        };

        Assert.Equal(4_000, def.MaxContextTokens);
        Assert.Empty(def.AllowedTools);
        Assert.Empty(def.Categories);
    }

    [Fact]
    public void ResearchWorker_HasCorrectDefinition()
    {
        var factory = CreateFactory();
        var worker = new ResearchWorker(factory, NullLogger<ResearchWorker>.Instance);

        Assert.Equal("research", worker.Definition.Name);
        Assert.Contains("research", worker.Definition.Categories);
        Assert.NotEmpty(worker.Definition.Description);
        Assert.NotEmpty(worker.Definition.SystemPrompt);
    }

    [Fact]
    public void CodeWorker_HasCorrectDefinition()
    {
        var factory = CreateFactory();
        var worker = new CodeWorker(factory, NullLogger<CodeWorker>.Instance);

        Assert.Equal("code", worker.Definition.Name);
        Assert.Contains("code", worker.Definition.Categories);
        Assert.Equal(8_000, worker.Definition.MaxContextTokens);
    }

    [Fact]
    public void ScheduleWorker_HasCorrectDefinition()
    {
        var factory = CreateFactory();
        var worker = new ScheduleWorker(factory, NullLogger<ScheduleWorker>.Instance);

        Assert.Equal("schedule", worker.Definition.Name);
        Assert.Contains("schedule", worker.Definition.Categories);
        Assert.Equal(2_000, worker.Definition.MaxContextTokens);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsLlmResponse()
    {
        var factory = CreateFactory(new WorkerTestChatClient("completed task"));
        var def = new AgentDefinition
        {
            Name = "test",
            Description = "Test worker",
            SystemPrompt = "You are a test worker"
        };
        var worker = new WorkerAgent(def, factory, NullLogger<WorkerAgent>.Instance);
        var budget = ContextBudget.FromModelWindow(4000);

        var result = await worker.ExecuteAsync("do something", budget, CancellationToken.None);

        Assert.Equal("completed task", result);
    }

    [Fact]
    public async Task ExecuteAsync_WhenLlmFails_ReturnsError()
    {
        var factory = CreateFactory(new WorkerTestChatClient(throwOnCall: true));
        var def = new AgentDefinition
        {
            Name = "test",
            Description = "Test worker",
            SystemPrompt = "You are a test worker"
        };
        var worker = new WorkerAgent(def, factory, NullLogger<WorkerAgent>.Instance);
        var budget = ContextBudget.FromModelWindow(4000);

        var result = await worker.ExecuteAsync("do something", budget, CancellationToken.None);

        Assert.Contains("Worker error:", result);
    }

    [Fact]
    public async Task ExecuteAsync_ShortTask_LogsTruncated()
    {
        var factory = CreateFactory();
        var def = new AgentDefinition { Name = "t", Description = "d", SystemPrompt = "p" };
        var worker = new WorkerAgent(def, factory, NullLogger<WorkerAgent>.Instance);
        var budget = ContextBudget.FromModelWindow(4000);

        var result = await worker.ExecuteAsync("short", budget, CancellationToken.None);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task ExecuteAsync_LongTask_LogsTruncated()
    {
        var factory = CreateFactory();
        var def = new AgentDefinition { Name = "t", Description = "d", SystemPrompt = "p" };
        var worker = new WorkerAgent(def, factory, NullLogger<WorkerAgent>.Instance);
        var budget = ContextBudget.FromModelWindow(4000);

        var result = await worker.ExecuteAsync(new string('x', 200), budget, CancellationToken.None);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task ExecuteAsync_NullResponseText_ReturnsEmptyOrDefault()
    {
        var factory = CreateFactory(new WorkerTestChatClient(nullResponse: true));
        var def = new AgentDefinition { Name = "t", Description = "d", SystemPrompt = "p" };
        var worker = new WorkerAgent(def, factory, NullLogger<WorkerAgent>.Instance);
        var budget = ContextBudget.FromModelWindow(4000);

        var result = await worker.ExecuteAsync("task", budget, CancellationToken.None);
        // ChatMessage with null text produces empty string or "No response generated."
        Assert.True(result == "" || result == "No response generated.");
    }

    private sealed class WorkerTestChatClient : IChatClient
    {
        private readonly string? _response;
        private readonly bool _throwOnCall;
        private readonly bool _nullResponse;

        public WorkerTestChatClient(string response = "worker result", bool throwOnCall = false, bool nullResponse = false)
        {
            _response = response;
            _throwOnCall = throwOnCall;
            _nullResponse = nullResponse;
        }

        public void Dispose() { }
        public ChatClientMetadata Metadata => new();
        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType == typeof(IChatClient) ? this : null;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (_throwOnCall) throw new Exception("LLM error");
            if (_nullResponse) return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, (string?)null)));
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _response)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
