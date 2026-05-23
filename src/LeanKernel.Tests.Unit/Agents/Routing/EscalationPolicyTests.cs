using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Enums;
using LeanKernel.Abstractions.Models;
using LeanKernel.Agents.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Agents.Routing;

public class EscalationPolicyTests
{
    [Fact]
    public void TryEscalate_moves_from_economy_to_standard_when_quality_fails()
    {
        var policy = CreatePolicy();

        var decision = policy.TryEscalate(
            new RoutingDecision
            {
                SelectedTier = ModelTier.Economy,
                SelectedModel = "gpt-4o-mini",
                ComplexityScore = 0.2,
                Reason = "initial",
                Factors = ["message-tokens:10:low"],
                EscalationAttempt = 0,
            },
            new TaskComplexityAssessment { Score = 0.2, Factors = ["message-tokens:10:low"] },
            QualityOutcome.FailedTooShort);

        decision.Should().NotBeNull();
        decision!.SelectedTier.Should().Be(ModelTier.Standard);
        decision.SelectedModel.Should().Be("gpt-4o");
        decision.EscalatedFrom.Should().Be(ModelTier.Economy);
        decision.EscalationAttempt.Should().Be(1);
    }

    [Fact]
    public void TryEscalate_returns_null_when_the_max_attempts_have_been_reached()
    {
        var policy = CreatePolicy();

        var decision = policy.TryEscalate(
            new RoutingDecision
            {
                SelectedTier = ModelTier.Standard,
                SelectedModel = "gpt-4o",
                ComplexityScore = 0.5,
                Reason = "already escalated",
                Factors = ["history-turns:2"],
                EscalationAttempt = 2,
            },
            new TaskComplexityAssessment { Score = 0.5, Factors = ["history-turns:2"] },
            QualityOutcome.FailedLowCoverage);

        decision.Should().BeNull();
    }

    [Fact]
    public void TryEscalate_returns_null_for_premium_tier()
    {
        var policy = CreatePolicy();

        var decision = policy.TryEscalate(
            new RoutingDecision
            {
                SelectedTier = ModelTier.Premium,
                SelectedModel = "claude-sonnet-4-20250514",
                ComplexityScore = 0.9,
                Reason = "premium",
                Factors = ["message-tokens:5000:high"],
                EscalationAttempt = 0,
            },
            new TaskComplexityAssessment { Score = 0.9, Factors = ["message-tokens:5000:high"] },
            QualityOutcome.FailedRefusal);

        decision.Should().BeNull();
    }

    private static EscalationPolicy CreatePolicy()
    {
        var selector = new PolicyModelSelector(
            Options.Create(new LeanKernelConfig
            {
                Routing = new RoutingConfig
                {
                    MaxEscalationAttempts = 2,
                    Economy = new ModelTierConfig { Model = "gpt-4o-mini", MaxTokens = 4096, CostWeight = 0.3 },
                    Standard = new ModelTierConfig { Model = "gpt-4o", MaxTokens = 8192, CostWeight = 1.0 },
                    Premium = new ModelTierConfig { Model = "claude-sonnet-4-20250514", MaxTokens = 16384, CostWeight = 3.0 },
                }
            }),
            NullLogger<PolicyModelSelector>.Instance);

        return new EscalationPolicy(
            selector,
            Options.Create(new LeanKernelConfig
            {
                Routing = new RoutingConfig
                {
                    MaxEscalationAttempts = 2,
                    Economy = new ModelTierConfig { Model = "gpt-4o-mini", MaxTokens = 4096, CostWeight = 0.3 },
                    Standard = new ModelTierConfig { Model = "gpt-4o", MaxTokens = 8192, CostWeight = 1.0 },
                    Premium = new ModelTierConfig { Model = "claude-sonnet-4-20250514", MaxTokens = 16384, CostWeight = 3.0 },
                }
            }),
            NullLogger<EscalationPolicy>.Instance);
    }
}
