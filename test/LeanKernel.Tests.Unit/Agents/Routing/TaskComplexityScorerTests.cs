using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Agents.Routing;
using LeanKernel.Agents.Strategies;
using LeanKernel.Context;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Agents.Routing;

public class TaskComplexityScorerTests
{
    [Fact]
    public void Score_returns_economy_friendly_score_for_a_simple_prompt()
    {
        var scorer = CreateScorer();

        var result = scorer.Score(new AgentStrategyContext
        {
            SessionId = "session-1",
            TurnId = "turn-1",
            UserMessage = "Summarize the latest project status.",
            SystemMessage = "You are a helpful assistant.",
            History = []
        });

        result.Score.Should().BeLessThan(0.3);
        result.Factors.Should().ContainSingle(factor => factor.Contains("message-tokens", StringComparison.Ordinal));
    }

    [Fact]
    public void Score_returns_standard_score_for_a_medium_prompt_with_history()
    {
        var scorer = CreateScorer();
        var mediumPrompt = new string('m', 2400);

        var result = scorer.Score(new AgentStrategyContext
        {
            SessionId = "session-1",
            TurnId = "turn-2",
            UserMessage = mediumPrompt,
            SystemMessage = "You are a helpful assistant.",
            History =
            [
                new() { Role = "user", Content = "Prior requirement details", Timestamp = DateTimeOffset.UtcNow },
                new() { Role = "assistant", Content = "Prior draft response", Timestamp = DateTimeOffset.UtcNow }
            ]
        });

        result.Score.Should().BeGreaterThanOrEqualTo(0.3).And.BeLessThanOrEqualTo(0.7);
        result.Factors.Should().Contain(factor => factor.Contains("medium", StringComparison.OrdinalIgnoreCase));
        result.Factors.Should().Contain("history-turns:2");
    }

    [Fact]
    public void Score_returns_premium_score_for_a_complex_multi_step_prompt()
    {
        var scorer = CreateScorer();
        var complexPrompt = string.Join(' ', Enumerable.Repeat("analyze implement verify ", 900));
        var longSystemMessage = new string('s', 2600);

        var result = scorer.Score(new AgentStrategyContext
        {
            SessionId = "session-1",
            TurnId = "turn-3",
            UserMessage = $"First inspect the code. Then implement the change. Finally verify the results. {complexPrompt}",
            SystemMessage = longSystemMessage,
            History =
            [
                new() { Role = "user", Content = new string('h', 2200), Timestamp = DateTimeOffset.UtcNow },
                new() { Role = "assistant", Content = new string('a', 2200), Timestamp = DateTimeOffset.UtcNow },
                new() { Role = "user", Content = new string('b', 2200), Timestamp = DateTimeOffset.UtcNow }
            ],
            AvailableToolNames = ["wiki_read", "wiki_search", "database_query"]
        });

        result.Score.Should().BeGreaterThan(0.7);
        result.Factors.Should().Contain("multi-step-instructions");
        result.Factors.Should().Contain("tooling:3");
        result.Factors.Should().Contain(factor => factor.StartsWith("system-tokens:", StringComparison.Ordinal));
    }

    private static TaskComplexityScorer CreateScorer()
        => new(
            new SimpleTokenEstimator(),
            Options.Create(new LeanKernelConfig
            {
                Routing = new RoutingConfig
                {
                    Scoring = new ComplexityScoringConfig
                    {
                        HighComplexityTokenThreshold = 2000,
                        MediumComplexityTokenThreshold = 500,
                        ToolUsageComplexityBoost = 0.3,
                        MultiTurnComplexityBoost = 0.2,
                        LongContextComplexityBoost = 0.2,
                    }
                }
            }),
            NullLogger<TaskComplexityScorer>.Instance);
}
