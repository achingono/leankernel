using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Agents.Routing;
using LeanKernel.Agents.Strategies;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace LeanKernel.Tests.Unit.Agents.Routing;

public class TaskComplexityScorerTests
{
    [Fact]
    public void Score_throws_ArgumentNullException_when_context_is_null()
    {
        var scorer = CreateScorer();

        var act = () => scorer.Score(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Score_returns_zero_for_empty_message_with_no_tools_or_history()
    {
        var tokenEstimator = new Mock<ITokenEstimator>();
        tokenEstimator.Setup(x => x.EstimateTokens(It.IsAny<string>())).Returns(0);
        var scorer = CreateScorer(tokenEstimator.Object);

        var result = scorer.Score(new AgentStrategyContext
        {
            SessionId = "s",
            TurnId = "t",
            UserMessage = "",
            SystemMessage = "",
            History = []
        });

        result.Score.Should().Be(0);
        result.Factors.Should().BeEmpty();
        result.MessageTokens.Should().Be(0);
    }

    [Fact]
    public void Score_returns_low_score_for_low_token_count()
    {
        var tokenEstimator = new Mock<ITokenEstimator>();
        tokenEstimator.Setup(x => x.EstimateTokens(It.IsAny<string>())).Returns(100);
        var scorer = CreateScorer(tokenEstimator.Object);

        var result = scorer.Score(new AgentStrategyContext
        {
            SessionId = "s",
            TurnId = "t",
            UserMessage = "some message",
            SystemMessage = "",
            History = []
        });

        result.Score.Should().BeGreaterThan(0).And.BeLessThan(0.25);
        result.Factors.Should().Contain(f => f.Contains("message-tokens") && f.Contains(":low"));
    }

    [Fact]
    public void Score_returns_medium_score_for_medium_token_count()
    {
        var tokenEstimator = new Mock<ITokenEstimator>();
        tokenEstimator.Setup(x => x.EstimateTokens("medium message")).Returns(1000);
        tokenEstimator.Setup(x => x.EstimateTokens(It.Is<string>(s => s != "medium message"))).Returns(0);
        var scorer = CreateScorer(tokenEstimator.Object);

        var result = scorer.Score(new AgentStrategyContext
        {
            SessionId = "s",
            TurnId = "t",
            UserMessage = "medium message",
            SystemMessage = "",
            History = []
        });

        result.Score.Should().Be(0.35);
        result.Factors.Should().Contain(f => f.StartsWith("message-tokens:") && f.EndsWith(":medium"));
    }

    [Fact]
    public void Score_returns_high_score_for_high_token_count()
    {
        var tokenEstimator = new Mock<ITokenEstimator>();
        tokenEstimator.Setup(x => x.EstimateTokens("long message")).Returns(2000);
        tokenEstimator.Setup(x => x.EstimateTokens(It.Is<string>(s => s != "long message"))).Returns(0);
        var scorer = CreateScorer(tokenEstimator.Object);

        var result = scorer.Score(new AgentStrategyContext
        {
            SessionId = "s",
            TurnId = "t",
            UserMessage = "long message",
            SystemMessage = "",
            History = []
        });

        result.Score.Should().Be(0.7);
        result.Factors.Should().Contain(f => f.StartsWith("message-tokens:") && f.EndsWith(":high"));
    }

    [Fact]
    public void Score_adds_tooling_factor_when_three_or_more_tools_present()
    {
        var tokenEstimator = new Mock<ITokenEstimator>();
        tokenEstimator.Setup(x => x.EstimateTokens(It.IsAny<string>())).Returns(100);
        var scorer = CreateScorer(tokenEstimator.Object);

        var result = scorer.Score(new AgentStrategyContext
        {
            SessionId = "s",
            TurnId = "t",
            UserMessage = "use tools",
            SystemMessage = "",
            History = [],
            AvailableToolNames = ["tool_a", "tool_b", "tool_c"]
        });

        result.Factors.Should().Contain("tooling:3");
        result.Score.Should().BeGreaterThanOrEqualTo(0.3);
    }

    [Fact]
    public void Score_adds_history_turns_factor_when_multiple_turns_exist()
    {
        var tokenEstimator = new Mock<ITokenEstimator>();
        tokenEstimator.Setup(x => x.EstimateTokens(It.IsAny<string>())).Returns(100);
        var scorer = CreateScorer(tokenEstimator.Object);

        var result = scorer.Score(new AgentStrategyContext
        {
            SessionId = "s",
            TurnId = "t",
            UserMessage = "continue",
            SystemMessage = "",
            History =
            [
                new() { Role = "user", Content = "first", Timestamp = DateTimeOffset.UtcNow },
                new() { Role = "assistant", Content = "response", Timestamp = DateTimeOffset.UtcNow }
            ]
        });

        result.Factors.Should().Contain("history-turns:2");
    }

    [Fact]
    public void Score_adds_multi_step_instructions_factor_when_ordered_list_detected()
    {
        var tokenEstimator = new Mock<ITokenEstimator>();
        tokenEstimator.Setup(x => x.EstimateTokens(It.IsAny<string>())).Returns(100);
        var scorer = CreateScorer(tokenEstimator.Object);

        var result = scorer.Score(new AgentStrategyContext
        {
            SessionId = "s",
            TurnId = "t",
            UserMessage = "1. First do this\n2. Then do that",
            SystemMessage = "",
            History = []
        });

        result.Factors.Should().Contain("multi-step-instructions");
        result.Score.Should().BeGreaterThan(0.1);
    }

    [Fact]
    public void Score_adds_multi_step_instructions_factor_when_keyword_markers_detected()
    {
        var tokenEstimator = new Mock<ITokenEstimator>();
        tokenEstimator.Setup(x => x.EstimateTokens(It.IsAny<string>())).Returns(100);
        var scorer = CreateScorer(tokenEstimator.Object);

        var result = scorer.Score(new AgentStrategyContext
        {
            SessionId = "s",
            TurnId = "t",
            UserMessage = "First analyze the code. Then implement the fix. Finally verify.",
            SystemMessage = "",
            History = []
        });

        result.Factors.Should().Contain("multi-step-instructions");
    }

    private static TaskComplexityScorer CreateScorer(ITokenEstimator? tokenEstimator = null)
    {
        var configMock = new Mock<IOptions<LeanKernelConfig>>();
        configMock.Setup(x => x.Value).Returns(new LeanKernelConfig
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
        });

        return new TaskComplexityScorer(
            tokenEstimator ?? Mock.Of<ITokenEstimator>(),
            configMock.Object,
            NullLogger<TaskComplexityScorer>.Instance);
    }
}
