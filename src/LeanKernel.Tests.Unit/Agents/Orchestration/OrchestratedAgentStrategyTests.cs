using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Models;
using LeanKernel.Agents;
using LeanKernel.Agents.Orchestration;
using LeanKernel.Agents.Quality;
using LeanKernel.Agents.Routing;
using LeanKernel.Agents.Strategies;
using LeanKernel.Context;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace LeanKernel.Tests.Unit.Agents.Orchestration;

public class OrchestratedAgentStrategyTests
{
    [Fact]
    public async Task InvokeAsync_falls_back_to_static_when_orchestration_is_not_required()
    {
        var coordinatorClient = new FixedChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Static response.")));
        var config = CreateConfig(orchestrationEnabled: true, routingEnabled: false);
        var strategy = CreateStrategy(config, coordinatorClient, Array.Empty<WorkerAgent>());
        var context = CreateContext("Summarize the latest status.");

        var response = await strategy.InvokeAsync(context);

        response.Should().Be("Static response.");
        context.OrchestrationResult.Should().BeNull();
        context.ModelUsed.Should().Be("gpt-4o-mini");
        coordinatorClient.InvocationCount.Should().Be(1);
    }

    [Fact]
    public async Task InvokeAsync_falls_back_gracefully_when_no_workers_are_configured()
    {
        var coordinatorClient = new FixedChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Static response.")));
        var config = CreateConfig(orchestrationEnabled: true, routingEnabled: false);
        var strategy = CreateStrategy(config, coordinatorClient, Array.Empty<WorkerAgent>());
        var context = CreateContext("First research Atlas, then summarize the findings for me.");

        var response = await strategy.InvokeAsync(context);

        response.Should().Be("Static response.");
        context.OrchestrationResult.Should().BeNull();
        coordinatorClient.InvocationCount.Should().Be(1);
    }

    [Fact]
    public async Task InvokeAsync_uses_worker_tools_and_captures_orchestration_results()
    {
        var coordinatorClient = new CoordinatingChatClient("researcher", "Find Atlas facts", "Final answer with research.");
        var workerClient = new FixedChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Atlas facts.")));
        var config = CreateConfig(orchestrationEnabled: true, routingEnabled: false);
        var worker = CreateWorker(config, workerClient);
        var strategy = CreateStrategy(config, coordinatorClient, [worker], workerClient);
        var context = CreateContext("First research Atlas, then summarize the findings for me.");

        var response = await strategy.InvokeAsync(context);

        response.Should().Be("Final answer with research.");
        context.OrchestrationResult.Should().NotBeNull();
        context.OrchestrationResult!.CoordinatorResponse.Should().Be("Final answer with research.");
        context.OrchestrationResult!.TotalWorkerInvocations.Should().Be(1);
        context.OrchestrationResult!.WorkerContributions.Should().ContainSingle();
        context.OrchestrationResult!.WorkerContributions[0].WorkerName.Should().Be("researcher");
        context.OrchestrationResult!.WorkerContributions[0].Response.Should().Be("Atlas facts.");
        context.OrchestrationResult!.WorkerContributions[0].Success.Should().BeTrue();
        coordinatorClient.ReceivedOptions.Should().NotBeNull();
        coordinatorClient.ReceivedOptions!.Tools.Should().ContainSingle(tool => tool.Name == "researcher");
    }

    private static OrchestratedAgentStrategy CreateStrategy(
        LeanKernelConfig config,
        IChatClient coordinatorClient,
        IReadOnlyList<WorkerAgent> workers,
        IChatClient? workerClient = null)
    {
        var clients = new Dictionary<string, IChatClient>(StringComparer.OrdinalIgnoreCase);
        if (workerClient is not null)
        {
            clients["worker-model"] = workerClient;
        }

        var factory = new AgentFactory(coordinatorClient, NullLogger<AgentFactory>.Instance, clients);
        var staticStrategy = new StaticAgentStrategy(factory, NullLogger<StaticAgentStrategy>.Instance);
        var routedStrategy = CreateRoutedStrategy(factory, config);
        var scorer = new TaskComplexityScorer(new SimpleTokenEstimator(), Options.Create(config), NullLogger<TaskComplexityScorer>.Instance);
        var decider = new OrchestrationDecider(scorer, NullLogger<OrchestrationDecider>.Instance);

        return new OrchestratedAgentStrategy(
            staticStrategy,
            routedStrategy,
            workers,
            decider,
            Options.Create(config),
            NullLogger<OrchestratedAgentStrategy>.Instance);
    }

    private static WorkerAgent CreateWorker(LeanKernelConfig config, IChatClient workerClient)
    {
        var factory = new AgentFactory(
            workerClient,
            NullLogger<AgentFactory>.Instance,
            new Dictionary<string, IChatClient>(StringComparer.OrdinalIgnoreCase)
            {
                ["worker-model"] = workerClient
            });

        return new WorkerAgent(
            new WorkerDefinition
            {
                Name = "researcher",
                Description = "Finds supporting knowledge",
                Model = "worker-model"
            },
            factory,
            Mock.Of<LeanKernel.Abstractions.Interfaces.IToolRegistry>(),
            Options.Create(config),
            NullLogger<WorkerAgent>.Instance);
    }

    private static RoutedAgentStrategy CreateRoutedStrategy(AgentFactory factory, LeanKernelConfig config)
    {
        var options = Options.Create(config);
        var scorer = new TaskComplexityScorer(new SimpleTokenEstimator(), options, NullLogger<TaskComplexityScorer>.Instance);
        var selector = new PolicyModelSelector(options, NullLogger<PolicyModelSelector>.Instance);
        var escalation = new EscalationPolicy(selector, options, NullLogger<EscalationPolicy>.Instance);
        var qualityGate = new ResponseQualityGate(
            new EmptyResponseCheck(),
            new MinLengthCheck(),
            new RefusalDetectionCheck(options),
            new ConstraintCoverageCheck());

        return new RoutedAgentStrategy(
            factory,
            scorer,
            selector,
            escalation,
            qualityGate,
            options,
            NullLogger<RoutedAgentStrategy>.Instance);
    }

    private static LeanKernelConfig CreateConfig(bool orchestrationEnabled, bool routingEnabled)
        => new()
        {
            Orchestration = new OrchestrationConfig
            {
                Enabled = orchestrationEnabled,
                MaxWorkerConcurrency = 2,
                MaxOrchestrationDepth = 2,
                WorkerTimeout = TimeSpan.FromSeconds(1)
            },
            Routing = new RoutingConfig
            {
                Enabled = routingEnabled,
                QualityMinOutputLength = 20,
                QualityMinConstraintCoverage = 0.3,
                MaxEscalationAttempts = 2,
                Economy = new ModelTierConfig { Model = "gpt-4o-mini", MaxTokens = 4096, CostWeight = 0.3 },
                Standard = new ModelTierConfig { Model = "gpt-4o", MaxTokens = 8192, CostWeight = 1.0 },
                Premium = new ModelTierConfig { Model = "claude-sonnet-4-20250514", MaxTokens = 16384, CostWeight = 3.0 }
            }
        };

    private static AgentStrategyContext CreateContext(string userMessage) => new()
    {
        SessionId = "session-1",
        TurnId = "turn-1",
        UserMessage = userMessage,
        SystemMessage = "You are a helpful assistant.",
        History = []
    };

    private sealed class FixedChatClient(ChatResponse response) : IChatClient
    {
        private readonly ChatResponse _response = response;

        public int InvocationCount { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            InvocationCount++;
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

    private sealed class CoordinatingChatClient(string workerToolName, string delegatedTask, string finalResponse) : IChatClient
    {
        public int InvocationCount { get; private set; }

        public ChatOptions? ReceivedOptions { get; private set; }

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            ReceivedOptions = options;

            var workerTool = options!.Tools!.Single(tool => tool.Name == workerToolName).GetService<AIFunction>();
            await workerTool!.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
            {
                ["task"] = delegatedTask
            }), cancellationToken);

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, finalResponse));
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
}
