using FluentAssertions;

using LeanKernel.Logic.Telemetry;

using Xunit;

namespace LeanKernel.Tests.Unit.Telemetry;

public class CostEstimateTableTests
{
    [Fact]
    public void Estimate_WithKnownModel_ReturnsCombinedInputAndOutputCost()
    {
        var table = new CostEstimateTable
        {
            CostPer1kInputTokens = { ["gpt-4o-mini"] = 0.001m },
            CostPer1kOutputTokens = { ["gpt-4o-mini"] = 0.002m }
        };

        var result = table.Estimate("gpt-4o-mini", 500, 250);

        result.Should().Be(0.001m);
    }

    [Fact]
    public void Estimate_WithUnknownModel_ReturnsNull()
    {
        var table = new CostEstimateTable();

        var result = table.Estimate("unknown", 200, 300);

        result.Should().BeNull();
    }
}