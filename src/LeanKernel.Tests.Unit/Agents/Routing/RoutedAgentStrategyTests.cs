using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Enums;
using LeanKernel.Abstractions.Models;
using LeanKernel.Agents;
using LeanKernel.Agents.Quality;
using LeanKernel.Agents.Routing;
using LeanKernel.Agents.Strategies;
using LeanKernel.Context;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Agents.Routing;

public class RoutedAgentStrategyTests
{
    [Fact]
    public async Task InvokeAsync_routes_simple_work_to_the_economy_model()
    {
        var economyClient = new RecordingChatClient(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "Project status summary with current milestones, risks, owners, and next steps included.")));
        var standardClient = new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "unused")));
        var premiumClient = new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "unused")));
        var strategy = CreateStrategy(economyClient, standardClient, premiumClient);

        var context = new AgentStrategyContext
        {
            SessionId = "session-1",
            TurnId = "turn-1",
            UserMessage = "Summarize the project status milestones risks owners and next steps.",
            SystemMessage = "You are a helpful assistant.",
            History = []
        };

        var response = await strategy.InvokeAsync(context);

        response.Should().Contain("milestones");
        context.ModelUsed.Should().Be("gpt-4o-mini");
        context.RoutingDecision.Should().NotBeNull();
        context.RoutingDecision!.SelectedTier.Should().Be(ModelTier.Economy);
        context.QualityOutcome.Should().Be(QualityOutcome.Passed);
        context.QualityGateResult.Should().NotBeNull();
        context.QualityGateResult!.Passed.Should().BeTrue();
        economyClient.InvocationCount.Should().Be(1);
        standardClient.InvocationCount.Should().Be(0);
        premiumClient.InvocationCount.Should().Be(0);
    }

    [Fact]
    public async Task InvokeAsync_escalates_when_the_initial_response_fails_quality_gates()
    {
        var economyClient = new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Too short.")));
        var standardClient = new RecordingChatClient(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "Detailed project status summary covering milestones, risks, owners, and next steps for the current release.")));
        var premiumClient = new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "unused")));
        var strategy = CreateStrategy(economyClient, standardClient, premiumClient);

        var context = new AgentStrategyContext
        {
            SessionId = "session-1",
            TurnId = "turn-2",
            UserMessage = "Summarize the project status milestones risks owners and next steps.",
            SystemMessage = "You are a helpful assistant.",
            History = [],
            AvailableToolNames = ["wiki_search"]
        };

        var response = await strategy.InvokeAsync(context);

        response.Should().Contain("owners");
        context.ModelUsed.Should().Be("gpt-4o");
        context.RoutingDecision.Should().NotBeNull();
        context.RoutingDecision!.SelectedTier.Should().Be(ModelTier.Standard);
        context.RoutingDecision.EscalatedFrom.Should().Be(ModelTier.Economy);
        context.RoutingDecision.EscalationAttempt.Should().Be(1);
        context.QualityOutcome.Should().Be(QualityOutcome.Passed);
        context.QualityGateResult.Should().NotBeNull();
        context.QualityGateResult!.Passed.Should().BeTrue();
        economyClient.InvocationCount.Should().Be(1);
        standardClient.InvocationCount.Should().Be(1);
        premiumClient.InvocationCount.Should().Be(0);
    }

    [Fact]
    public async Task InvokeAsync_returns_the_final_response_when_all_escalation_attempts_are_exhausted()
    {
        var economyClient = new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Too short.")));
        var standardClient = new RecordingChatClient(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "As an AI language model, I cannot provide that exact response, but I can offer a generic alternative.")));
        var premiumClient = new RecordingChatClient(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "Detailed implementation guidance focused on architecture decisions and scheduling details without the requested status specifics.")));
        var strategy = CreateStrategy(economyClient, standardClient, premiumClient);

        var context = new AgentStrategyContext
        {
            SessionId = "session-1",
            TurnId = "turn-3",
            UserMessage = "Summarize the Atlas project status milestones risks owners and next steps.",
            SystemMessage = "You are a helpful assistant.",
            History = []
        };

        var response = await strategy.InvokeAsync(context);

        response.Should().Be("Detailed implementation guidance focused on architecture decisions and scheduling details without the requested status specifics.");
        context.ModelUsed.Should().Be("claude-sonnet-4-20250514");
        context.RoutingDecision.Should().NotBeNull();
        context.RoutingDecision!.SelectedTier.Should().Be(ModelTier.Premium);
        context.RoutingDecision.EscalatedFrom.Should().Be(ModelTier.Standard);
        context.RoutingDecision.EscalationAttempt.Should().Be(2);
        context.QualityOutcome.Should().Be(QualityOutcome.FailedLowCoverage);
        context.QualityGateResult.Should().NotBeNull();
        context.QualityGateResult!.Passed.Should().BeFalse();
        context.QualityGateResult.Outcome.Should().Be(QualityOutcome.FailedLowCoverage);
        economyClient.InvocationCount.Should().Be(1);
        standardClient.InvocationCount.Should().Be(1);
        premiumClient.InvocationCount.Should().Be(1);
    }

    private static RoutedAgentStrategy CreateStrategy(
        IChatClient economyClient,
        IChatClient standardClient,
        IChatClient premiumClient)
    {
        var config = new LeanKernelConfig
        {
            Routing = new RoutingConfig
            {
                Enabled = true,
                QualityMinOutputLength = 50,
                QualityMinConstraintCoverage = 0.6,
                MaxEscalationAttempts = 2,
                RefusalPatterns = ["I cannot", "As an AI language model"],
                Economy = new ModelTierConfig { Model = "gpt-4o-mini", MaxTokens = 4096, CostWeight = 0.3 },
                Standard = new ModelTierConfig { Model = "gpt-4o", MaxTokens = 8192, CostWeight = 1.0 },
                Premium = new ModelTierConfig { Model = "claude-sonnet-4-20250514", MaxTokens = 16384, CostWeight = 3.0 },
            }
        };

        var factory = new AgentFactory(
            economyClient,
            NullLogger<AgentFactory>.Instance,
            new Dictionary<string, IChatClient>(StringComparer.OrdinalIgnoreCase)
            {
                ["gpt-4o-mini"] = economyClient,
                ["gpt-4o"] = standardClient,
                ["claude-sonnet-4-20250514"] = premiumClient,
            });
        var scorer = new TaskComplexityScorer(new SimpleTokenEstimator(), Options.Create(config), NullLogger<TaskComplexityScorer>.Instance);
        var selector = new PolicyModelSelector(Options.Create(config), NullLogger<PolicyModelSelector>.Instance);
        var escalationPolicy = new EscalationPolicy(selector, Options.Create(config), NullLogger<EscalationPolicy>.Instance);
        var qualityGate = new ResponseQualityGate(
            new EmptyResponseCheck(),
            new MinLengthCheck(),
            new RefusalDetectionCheck(Options.Create(config)),
            new ConstraintCoverageCheck());

        return new RoutedAgentStrategy(
            factory,
            scorer,
            selector,
            escalationPolicy,
            qualityGate,
            Options.Create(config),
            NullLogger<RoutedAgentStrategy>.Instance);
    }

    private sealed class RecordingChatClient(ChatResponse response) : IChatClient
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
}
