using FluentAssertions;
using LeanKernel.Logic.Telemetry;
using LeanKernel.Logic.Telemetry.Models;
using Xunit;

namespace LeanKernel.Tests.Unit.Telemetry;

public sealed class TelemetryModelContractTests
{
    [Fact]
    public void TelemetryExportRecord_StoresConstructorValues()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var record = new TelemetryExportRecord(
            timestamp,
            "requested",
            "served",
            "provider",
            10,
            5,
            0.001m,
            true);

        record.Timestamp.Should().Be(timestamp);
        record.RequestedModel.Should().Be("requested");
        record.ServedModel.Should().Be("served");
        record.Provider.Should().Be("provider");
        record.PromptTokens.Should().Be(10);
        record.CompletionTokens.Should().Be(5);
        record.ResponseCost.Should().Be(0.001m);
        record.CostIsEstimated.Should().BeTrue();
    }

    [Fact]
    public void ModelEfficiency_StoresConstructorValues()
    {
        var modelEfficiency = new ModelEfficiency(
            "gpt-4o-mini",
            "openai",
            2,
            450,
            0.009m,
            0.02m,
            150m,
            75m,
            0.3333m);

        modelEfficiency.Model.Should().Be("gpt-4o-mini");
        modelEfficiency.Provider.Should().Be("openai");
        modelEfficiency.TotalTurns.Should().Be(2);
        modelEfficiency.TotalTokens.Should().Be(450);
        modelEfficiency.TotalCost.Should().Be(0.009m);
        modelEfficiency.CostPer1kTokens.Should().Be(0.02m);
        modelEfficiency.AvgPromptTokensPerTurn.Should().Be(150m);
        modelEfficiency.AvgCompletionTokensPerTurn.Should().Be(75m);
        modelEfficiency.CompletionRatio.Should().Be(0.3333m);
    }

    [Fact]
    public void DateRange_FactoryMethodsProduceValidRanges()
    {
        var last7Days = DateRange.Last7Days();
        var last30Days = DateRange.Last30Days();
        var currentMonth = DateRange.CurrentMonth();

        last7Days.IsValid.Should().BeTrue();
        last30Days.IsValid.Should().BeTrue();
        currentMonth.IsValid.Should().BeTrue();
        currentMonth.From.Day.Should().Be(1);

        var invalid = new DateRange(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(-1));
        invalid.IsValid.Should().BeFalse();
    }

    [Fact]
    public void CostSummary_StoresConstructorValues()
    {
        var range = new DateRange(DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow);
        var summary = new CostSummary(
            range,
            1.25m,
            100,
            40,
            5,
            2,
            3,
            4,
            0.25m,
            28m,
            "USD");

        summary.Range.Should().Be(range);
        summary.TotalCost.Should().Be(1.25m);
        summary.TotalPromptTokens.Should().Be(100);
        summary.TotalCompletionTokens.Should().Be(40);
        summary.TotalTurns.Should().Be(5);
        summary.UniqueUsers.Should().Be(2);
        summary.UniqueSessions.Should().Be(3);
        summary.UniqueModels.Should().Be(4);
        summary.AvgCostPerTurn.Should().Be(0.25m);
        summary.AvgTokensPerTurn.Should().Be(28m);
        summary.Currency.Should().Be("USD");
    }

    [Fact]
    public void CostBreakdown_StoresConstructorValues()
    {
        var breakdown = new CostBreakdown(
            "model",
            "gpt-4o-mini",
            0.009m,
            300,
            150,
            450,
            2,
            0.0045m,
            225m,
            1,
            1);

        breakdown.Dimension.Should().Be("model");
        breakdown.Key.Should().Be("gpt-4o-mini");
        breakdown.TotalCost.Should().Be(0.009m);
        breakdown.PromptTokens.Should().Be(300);
        breakdown.CompletionTokens.Should().Be(150);
        breakdown.TotalTokens.Should().Be(450);
        breakdown.TurnCount.Should().Be(2);
        breakdown.AvgCostPerTurn.Should().Be(0.0045m);
        breakdown.AvgTokensPerTurn.Should().Be(225m);
        breakdown.EstimatedTurnCount.Should().Be(1);
        breakdown.ReportedTurnCount.Should().Be(1);
    }
}
