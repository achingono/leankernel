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

public class WorkerAgentTests
{
    [Fact]
    public async Task ExecuteTaskAsync_uses_the_worker_model_prompt_and_scoped_tools()
    {
        var workerClient = new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Research complete.")));
        var factory = new AgentFactory(
            workerClient,
            NullLogger<AgentFactory>.Instance,
            new Dictionary<string, IChatClient>(StringComparer.OrdinalIgnoreCase)
            {
                ["research-model"] = workerClient
            });
        var registry = new Mock<IToolRegistry>(MockBehavior.Strict);
        registry
            .Setup(item => item.GetVisibleTools(It.Is<ToolVisibilityContext>(context =>
                context.AgentRole == "global"
                && context.AllowedToolNames!.SequenceEqual(new[] { "wiki_search" }))))
            .Returns(
            [
                new ToolDefinition
                {
                    Name = "wiki_search",
                    Description = "Search the wiki",
                    Parameters = [new ToolParameter { Name = "query", Type = "string", Description = "Search query" }],
                    Handler = (arguments, _) => Task.FromResult(new ToolResult
                    {
                        ToolName = "wiki_search",
                        Success = true,
                        Output = arguments["query"]?.ToString()
                    })
                }
            ]);

        var worker = new WorkerAgent(
            new WorkerDefinition
            {
                Name = "researcher",
                Description = "Finds supporting knowledge",
                Model = "research-model",
                SystemPrompt = "You are a research worker.",
                AllowedTools = ["wiki_search"],
                Scope = "global"
            },
            factory,
            registry.Object,
            Options.Create(new LeanKernelConfig
            {
                Orchestration = new OrchestrationConfig
                {
                    WorkerTimeout = TimeSpan.FromSeconds(1),
                    MaxOrchestrationDepth = 2
                }
            }),
            NullLogger<WorkerAgent>.Instance);

        var contribution = await worker.ExecuteTaskAsync(CreateCoordinatorContext(), "Find Atlas facts", 2);

        contribution.Success.Should().BeTrue();
        contribution.WorkerName.Should().Be("researcher");
        contribution.Response.Should().Be("Research complete.");
        workerClient.ReceivedMessages[0].Role.Should().Be(ChatRole.System);
        workerClient.ReceivedMessages[0].Text.Should().ContainEquivalentOf("research worker");
        workerClient.ReceivedMessages[^1].Text.Should().Be("Find Atlas facts");
        workerClient.ReceivedOptions.Should().NotBeNull();
        workerClient.ReceivedOptions!.Tools.Should().ContainSingle(tool => tool.Name == "wiki_search");
        registry.VerifyAll();
    }

    [Fact]
    public async Task ExecuteTaskAsync_returns_a_failed_contribution_when_the_worker_times_out()
    {
        var workerClient = new SlowChatClient();
        var factory = new AgentFactory(
            workerClient,
            NullLogger<AgentFactory>.Instance,
            new Dictionary<string, IChatClient>(StringComparer.OrdinalIgnoreCase)
            {
                ["slow-model"] = workerClient
            });
        var registry = new Mock<IToolRegistry>(MockBehavior.Strict);
        var worker = new WorkerAgent(
            new WorkerDefinition
            {
                Name = "researcher",
                Description = "Finds supporting knowledge",
                Model = "slow-model"
            },
            factory,
            registry.Object,
            Options.Create(new LeanKernelConfig
            {
                Orchestration = new OrchestrationConfig
                {
                    WorkerTimeout = TimeSpan.FromMilliseconds(50),
                    MaxOrchestrationDepth = 2
                }
            }),
            NullLogger<WorkerAgent>.Instance);

        var contribution = await worker.ExecuteTaskAsync(CreateCoordinatorContext(), "Investigate Atlas", 2);

        contribution.Success.Should().BeFalse();
        contribution.Error.Should().ContainEquivalentOf("timed out");
        registry.Verify(item => item.GetVisibleTools(It.IsAny<ToolVisibilityContext>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteTaskAsync_returns_a_failed_contribution_when_depth_limit_is_exceeded()
    {
        var workerClient = new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "unused")));
        var factory = new AgentFactory(workerClient, NullLogger<AgentFactory>.Instance);
        var registry = new Mock<IToolRegistry>(MockBehavior.Strict);
        var worker = new WorkerAgent(
            new WorkerDefinition
            {
                Name = "researcher",
                Description = "Finds supporting knowledge"
            },
            factory,
            registry.Object,
            Options.Create(new LeanKernelConfig
            {
                Orchestration = new OrchestrationConfig
                {
                    WorkerTimeout = TimeSpan.FromSeconds(1),
                    MaxOrchestrationDepth = 1
                }
            }),
            NullLogger<WorkerAgent>.Instance);

        var contribution = await worker.ExecuteTaskAsync(CreateCoordinatorContext(), "Investigate Atlas", 2);

        contribution.Success.Should().BeFalse();
        contribution.Error.Should().ContainEquivalentOf("exceeds max depth");
        registry.Verify(item => item.GetVisibleTools(It.IsAny<ToolVisibilityContext>()), Times.Never);
    }

    private static AgentStrategyContext CreateCoordinatorContext() => new()
    {
        SessionId = "session-1",
        TurnId = "turn-1",
        UserMessage = "Original request",
        SystemMessage = "Coordinator system",
        History = []
    };

    private sealed class RecordingChatClient(ChatResponse response) : IChatClient
    {
        private readonly ChatResponse _response = response;

        public List<ChatMessage> ReceivedMessages { get; } = [];

        public ChatOptions? ReceivedOptions { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ReceivedMessages.Clear();
            ReceivedMessages.AddRange(messages);
            ReceivedOptions = options;
            return Task.FromResult(_response);
        }

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

    private sealed class SlowChatClient : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "unused"));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
