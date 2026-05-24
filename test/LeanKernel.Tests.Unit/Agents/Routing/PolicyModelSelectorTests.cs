using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Enums;
using LeanKernel.Agents.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Agents.Routing;

public class PolicyModelSelectorTests
{
    [Theory]
    [InlineData(0.29, ModelTier.Economy, "gpt-4o-mini")]
    [InlineData(0.30, ModelTier.Standard, "gpt-4o")]
    [InlineData(0.70, ModelTier.Standard, "gpt-4o")]
    [InlineData(0.71, ModelTier.Premium, "claude-sonnet-4-20250514")]
    public void Select_maps_complexity_score_to_the_expected_tier(
        double score,
        ModelTier expectedTier,
        string expectedModel)
    {
        var selector = CreateSelector();

        var decision = selector.Select(new TaskComplexityAssessment
        {
            Score = score,
            Factors = ["message-tokens:test"],
        });

        decision.SelectedTier.Should().Be(expectedTier);
        decision.SelectedModel.Should().Be(expectedModel);
        decision.ComplexityScore.Should().Be(score);
    }

    private static PolicyModelSelector CreateSelector()
        => new(
            Options.Create(new LeanKernelConfig
            {
                Routing = new RoutingConfig
                {
                    Economy = new ModelTierConfig { Model = "gpt-4o-mini", MaxTokens = 4096, CostWeight = 0.3 },
                    Standard = new ModelTierConfig { Model = "gpt-4o", MaxTokens = 8192, CostWeight = 1.0 },
                    Premium = new ModelTierConfig { Model = "claude-sonnet-4-20250514", MaxTokens = 16384, CostWeight = 3.0 },
                }
            }),
            NullLogger<PolicyModelSelector>.Instance);
}
