using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using LeanKernel.Thinker;
using LeanKernel.Thinker.Agents;
using LeanKernel.Thinker.Workflows;

namespace LeanKernel.Tests.Unit.Thinker;

public class LeanKernelWorkflowBuilderTests
{
    private static AgentFactory CreateFactory(IChatClient? client = null) =>
        new(client ?? new WorkflowTestChatClient(), NullLogger<AgentFactory>.Instance);

    [Fact]
    public void BuildAsAgent_ReturnsNonNull()
    {
        var factory = CreateFactory();
        var workers = new WorkerAgent[]
        {
            new ResearchWorker(factory, NullLogger<ResearchWorker>.Instance),
            new CodeWorker(factory, NullLogger<CodeWorker>.Instance)
        };
        var builder = new LeanKernelWorkflowBuilder(factory, workers,
            NullLogger<LeanKernelWorkflowBuilder>.Instance);

        var agent = builder.BuildAsAgent();

        Assert.NotNull(agent);
    }

    [Fact]
    public void BuildAsAgent_NoWorkers_ReturnsAgent()
    {
        var factory = CreateFactory();
        var builder = new LeanKernelWorkflowBuilder(factory, [],
            NullLogger<LeanKernelWorkflowBuilder>.Instance);

        var agent = builder.BuildAsAgent();

        Assert.NotNull(agent);
    }

    [Fact]
    public void BuildAsAgent_WithAllWorkers_ReturnsAgent()
    {
        var factory = CreateFactory();
        var workers = new WorkerAgent[]
        {
            new ResearchWorker(factory, NullLogger<ResearchWorker>.Instance),
            new CodeWorker(factory, NullLogger<CodeWorker>.Instance),
            new ScheduleWorker(factory, NullLogger<ScheduleWorker>.Instance)
        };
        var builder = new LeanKernelWorkflowBuilder(factory, workers,
            NullLogger<LeanKernelWorkflowBuilder>.Instance);

        var agent = builder.BuildAsAgent();

        Assert.NotNull(agent);
    }

    private sealed class WorkflowTestChatClient : IChatClient
    {
        public void Dispose() { }
        public ChatClientMetadata Metadata => new();
        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType == typeof(IChatClient) ? this : null;
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "workflow result")));
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }
}
