using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Thinker;
using LeanKernel.Thinker.Agents;

namespace LeanKernel.Tests.Unit.Thinker;

/// <summary>
/// Tests for AgentOrchestrator methods that require mocked dependencies
/// (ProcessAsync, DelegateToWorkerAsync, InvokeDirectAsync, BuildWorkerTools).
/// Static methods (AnalyzeComplexity, DecomposeTask) are tested in AgentOrchestratorTests.
/// </summary>
public class AgentOrchestratorFullTests
{
    private static ConversationContext MakeContext() => new()
    {
        SystemPrompt = "You are LeanKernel.",
        History = [],
        WikiLeanKernels = [],
        RetrievedLeanKernels = [],
        ActiveToolNames = []
    };

    private static AgentFactory CreateFactory(IChatClient? client = null) =>
        new(client ?? new OrchestratorTestChatClient("response"), NullLogger<AgentFactory>.Instance);

    private static AgentOrchestrator CreateOrchestrator(
        AgentFactory? factory = null,
        IEnumerable<WorkerAgent>? workers = null)
    {
        var f = factory ?? CreateFactory();
        var assembler = new PromptAssembler(NullLogger<PromptAssembler>.Instance);
        var w = workers ?? Array.Empty<WorkerAgent>();
        return new AgentOrchestrator(f, assembler, w, NullLogger<AgentOrchestrator>.Instance);
    }

    [Fact]
    public async Task ProcessAsync_SimpleQuery_InvokesDirect()
    {
        var orch = CreateOrchestrator();
        var msg = new LeanKernelMessage
        {
            Id = "1", ChannelId = "c", SenderId = "u",
            Content = "Hello", Timestamp = DateTimeOffset.UtcNow
        };

        var result = await orch.ProcessAsync(msg, MakeContext(), CancellationToken.None);

        Assert.Equal("response", result);
    }

    [Fact]
    public async Task ProcessAsync_ComplexQuery_UsesWorkers()
    {
        var factory = CreateFactory();
        var workers = new WorkerAgent[]
        {
            new ResearchWorker(factory, NullLogger<ResearchWorker>.Instance),
            new CodeWorker(factory, NullLogger<CodeWorker>.Instance)
        };
        var orch = CreateOrchestrator(factory, workers);
        var msg = new LeanKernelMessage
        {
            Id = "1", ChannelId = "c", SenderId = "u",
            Content = "Research best practices and then implement them",
            Timestamp = DateTimeOffset.UtcNow
        };

        var result = await orch.ProcessAsync(msg, MakeContext(), CancellationToken.None);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task DelegateToWorkerAsync_ExistingWorker_ReturnsResult()
    {
        var factory = CreateFactory(new OrchestratorTestChatClient("worker output"));
        var worker = new ResearchWorker(factory, NullLogger<ResearchWorker>.Instance);
        var orch = CreateOrchestrator(factory, new WorkerAgent[] { worker });
        var budget = ContextBudget.FromModelWindow(4000);

        var result = await orch.DelegateToWorkerAsync("research", "find info", budget, CancellationToken.None);

        Assert.Equal("worker output", result);
    }

    [Fact]
    public async Task DelegateToWorkerAsync_UnknownWorker_ReturnsError()
    {
        var orch = CreateOrchestrator();
        var budget = ContextBudget.FromModelWindow(4000);

        var result = await orch.DelegateToWorkerAsync("nonexistent", "task", budget, CancellationToken.None);

        Assert.Contains("not found", result);
    }

    [Fact]
    public void BuildWorkerTools_ReturnsToolPerWorker()
    {
        var factory = CreateFactory();
        var workers = new WorkerAgent[]
        {
            new ResearchWorker(factory, NullLogger<ResearchWorker>.Instance),
            new CodeWorker(factory, NullLogger<CodeWorker>.Instance)
        };
        var orch = CreateOrchestrator(factory, workers);

        var tools = orch.BuildWorkerTools();

        Assert.Equal(2, tools.Count);
    }

    [Fact]
    public void BuildWorkerTools_EmptyWorkers_ReturnsEmpty()
    {
        var orch = CreateOrchestrator();
        var tools = orch.BuildWorkerTools();
        Assert.Empty(tools);
    }

    [Fact]
    public async Task ProcessAsync_LlmError_ReturnsFallbackMessage()
    {
        var factory = CreateFactory(new OrchestratorTestChatClient(throwOnCall: true));
        var orch = CreateOrchestrator(factory);
        var msg = new LeanKernelMessage
        {
            Id = "1", ChannelId = "c", SenderId = "u",
            Content = "Hello", Timestamp = DateTimeOffset.UtcNow
        };

        var result = await orch.ProcessAsync(msg, MakeContext(), CancellationToken.None);

        Assert.Contains("error", result, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class OrchestratorTestChatClient : IChatClient
    {
        private readonly string _response;
        private readonly bool _throwOnCall;

        public OrchestratorTestChatClient(string response = "response", bool throwOnCall = false)
        {
            _response = response;
            _throwOnCall = throwOnCall;
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
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _response)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
