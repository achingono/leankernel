using System.Collections.Concurrent;
using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Agents;
using LeanKernel.Agents.Orchestration;
using LeanKernel.Agents.Strategies;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace LeanKernel.Tests.Unit.Agents.Orchestration;

public class WorkerAsToolAdapterTests
{
    [Fact]
    public async Task ToAITool_invokes_the_worker_and_records_its_contribution()
    {
        var worker = CreateWorker(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Worker summary.")), maxDepth: 2);
        var contributions = new ConcurrentQueue<WorkerContribution>();
        var adapter = new WorkerAsToolAdapter(worker, CreateCoordinatorContext(), 1, new SemaphoreSlim(1), contributions);
        var function = adapter.ToAITool().GetService<AIFunction>();

        var result = await function!.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["task"] = "Investigate Atlas"
        }));

        result.Should().Be("Worker summary.");
        contributions.Should().ContainSingle();
        contributions.TryPeek(out var contribution).Should().BeTrue();
        contribution!.Success.Should().BeTrue();
        contribution.WorkerName.Should().Be("researcher");
        contribution.Task.Should().Be("Investigate Atlas");
        contribution.Response.Should().Be("Worker summary.");
    }

    [Fact]
    public async Task ToAITool_returns_failure_text_when_the_worker_fails()
    {
        var worker = CreateWorker(new ChatResponse(new ChatMessage(ChatRole.Assistant, "unused")), maxDepth: 1);
        var contributions = new ConcurrentQueue<WorkerContribution>();
        var adapter = new WorkerAsToolAdapter(worker, CreateCoordinatorContext(), 1, new SemaphoreSlim(1), contributions);
        var function = adapter.ToAITool().GetService<AIFunction>();

        var result = await function!.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["task"] = "Investigate Atlas"
        }));

        result.Should().Be("researcher failed: Orchestration depth 2 exceeds max depth 1.");
        contributions.Should().ContainSingle();
        contributions.TryPeek(out var contribution).Should().BeTrue();
        contribution!.Success.Should().BeFalse();
        contribution.Error.Should().ContainEquivalentOf("exceeds max depth");
    }

    private static WorkerAgent CreateWorker(ChatResponse response, int maxDepth)
    {
        var chatClient = new FixedChatClient(response);
        var factory = new AgentFactory(
            chatClient,
            NullLogger<AgentFactory>.Instance,
            new Dictionary<string, IChatClient>(StringComparer.OrdinalIgnoreCase)
            {
                ["worker-model"] = chatClient
            });

        return new WorkerAgent(
            new WorkerDefinition
            {
                Name = "researcher",
                Description = "Finds supporting knowledge",
                Model = "worker-model"
            },
            factory,
            Mock.Of<IToolRegistry>(),
            Options.Create(new LeanKernelConfig
            {
                Orchestration = new OrchestrationConfig
                {
                    WorkerTimeout = TimeSpan.FromSeconds(1),
                    MaxOrchestrationDepth = maxDepth
                }
            }),
            NullLogger<WorkerAgent>.Instance);
    }

    private static AgentStrategyContext CreateCoordinatorContext() => new()
    {
        SessionId = "session-1",
        TurnId = "turn-1",
        UserMessage = "Original request",
        SystemMessage = "Coordinator system",
        History = []
    };

    private sealed class FixedChatClient(ChatResponse response) : IChatClient
    {
        private readonly ChatResponse _response = response;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_response);

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
