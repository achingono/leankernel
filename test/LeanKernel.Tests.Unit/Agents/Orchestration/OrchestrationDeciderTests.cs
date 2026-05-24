using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Agents.Orchestration;
using LeanKernel.Agents.Routing;
using LeanKernel.Agents.Strategies;
using LeanKernel.Context;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Agents.Orchestration;

public class OrchestrationDeciderTests
{
    [Fact]
    public void Decide_returns_true_for_multi_step_requests()
    {
        var decider = CreateDecider();

        var decision = decider.Decide(CreateContext("First inspect the code, then implement the change, and finally summarize the result."));

        decision.ShouldOrchestrate.Should().BeTrue();
        decision.Reason.Should().ContainEquivalentOf("multi-step");
    }

    [Fact]
    public void Decide_returns_true_for_explicit_delegation_requests()
    {
        var decider = CreateDecider();

        var decision = decider.Decide(CreateContext("Delegate this task to a worker and coordinate the final answer."));

        decision.ShouldOrchestrate.Should().BeTrue();
        decision.Reason.Should().ContainEquivalentOf("delegation");
    }

    [Fact]
    public void Decide_returns_true_when_complexity_exceeds_threshold()
    {
        var decider = CreateDecider();
        var complexPrompt = string.Join(' ', Enumerable.Repeat("analyze implement verify", 500));

        var decision = decider.Decide(CreateContext(complexPrompt));

        decision.ShouldOrchestrate.Should().BeTrue();
        decision.Reason.Should().ContainEquivalentOf("complexity score");
    }

    [Fact]
    public void Decide_returns_false_for_simple_single_step_requests()
    {
        var decider = CreateDecider();

        var decision = decider.Decide(CreateContext("Summarize the latest status."));

        decision.ShouldOrchestrate.Should().BeFalse();
        decision.Reason.Should().ContainEquivalentOf("below orchestration threshold");
    }

    private static AgentStrategyContext CreateContext(string userMessage) => new()
    {
        SessionId = "session-1",
        TurnId = "turn-1",
        UserMessage = userMessage,
        SystemMessage = "You are a helpful assistant.",
        History = []
    };

    private static OrchestrationDecider CreateDecider()
    {
        var config = Options.Create(new LeanKernelConfig
        {
            Routing = new RoutingConfig
            {
                Scoring = new ComplexityScoringConfig
                {
                    HighComplexityTokenThreshold = 2000,
                    MediumComplexityTokenThreshold = 500,
                    ToolUsageComplexityBoost = 0.3,
                    MultiTurnComplexityBoost = 0.2,
                    LongContextComplexityBoost = 0.2
                }
            }
        });
        var scorer = new TaskComplexityScorer(new SimpleTokenEstimator(), config, NullLogger<TaskComplexityScorer>.Instance);
        return new OrchestrationDecider(scorer, NullLogger<OrchestrationDecider>.Instance);
    }
}
