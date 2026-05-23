using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Models;
using LeanKernel.Context;
using LeanKernel.Context.History;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Context.History;

public class HistoryCompactionStrategyTests
{
    [Fact]
    public void CreatePlan_assigns_expected_tiers_with_compaction_and_summarization_enabled()
    {
        var strategy = CreateStrategy(new HistoryConfig
        {
            RecentTurnsVerbatim = 2,
            CompactedTurnsMax = 2,
            SummarizedTurnsMax = 1,
            EnableCompaction = true,
            EnableSummarization = true,
        });

        var plan = strategy.CreatePlan(CreateTurns(6), budgetTokens: 20);

        plan.AssignedEntries.Select(entry => entry.Tier).Should().Equal(
            HistoryTier.Dropped,
            HistoryTier.Summarized,
            HistoryTier.Compacted,
            HistoryTier.Compacted,
            HistoryTier.Verbatim,
            HistoryTier.Verbatim);
        plan.Diagnostics.TotalTurns.Should().Be(6);
        plan.Diagnostics.VerbatimTurns.Should().Be(2);
        plan.Diagnostics.CompactedTurns.Should().Be(2);
        plan.Diagnostics.SummarizedTurns.Should().Be(1);
        plan.Diagnostics.DroppedTurns.Should().Be(1);
    }

    [Fact]
    public void CreatePlan_folds_summary_budget_into_compaction_when_summarization_is_disabled()
    {
        var strategy = CreateStrategy(new HistoryConfig
        {
            RecentTurnsVerbatim = 1,
            CompactedTurnsMax = 2,
            SummarizedTurnsMax = 2,
            EnableCompaction = true,
            EnableSummarization = false,
        });

        var plan = strategy.CreatePlan(CreateTurns(6), budgetTokens: 20);

        plan.AssignedEntries.Select(entry => entry.Tier).Should().Equal(
            HistoryTier.Dropped,
            HistoryTier.Compacted,
            HistoryTier.Compacted,
            HistoryTier.Compacted,
            HistoryTier.Compacted,
            HistoryTier.Verbatim);
        plan.Diagnostics.CompactedTurns.Should().Be(4);
        plan.Diagnostics.SummarizedTurns.Should().Be(0);
    }

    [Fact]
    public void CreatePlan_tracks_token_counts_for_non_dropped_turns()
    {
        var strategy = CreateStrategy(new HistoryConfig
        {
            RecentTurnsVerbatim = 2,
            CompactedTurnsMax = 1,
            SummarizedTurnsMax = 1,
            EnableCompaction = true,
            EnableSummarization = true,
        });

        var plan = strategy.CreatePlan(CreateTurns(5), budgetTokens: 20);

        plan.Diagnostics.TotalTokensBefore.Should().Be(5);
        plan.Diagnostics.TotalTokensAfter.Should().Be(4);
        plan.Diagnostics.BudgetAvailable.Should().Be(20);
    }

    private static HistoryCompactionStrategy CreateStrategy(HistoryConfig config)
        => new(new SimpleTokenEstimator(), Options.Create(config), NullLogger<HistoryCompactionStrategy>.Instance);

    private static IReadOnlyList<ConversationTurn> CreateTurns(int count)
        => Enumerable.Range(1, count)
            .Select(index => new ConversationTurn
            {
                TurnId = $"t{index}",
                Role = index % 2 == 0 ? "assistant" : "user",
                Content = "1234",
                Timestamp = DateTimeOffset.Parse($"2025-05-20T10:0{index - 1}:00Z")
            })
            .ToList();
}
