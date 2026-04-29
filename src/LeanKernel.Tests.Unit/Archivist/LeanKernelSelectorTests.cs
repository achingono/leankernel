using LeanKernel.Core.Enums;
using LeanKernel.Core.Models;

namespace LeanKernel.Tests.Unit.Archivist;

public class LeanKernelSelectorTests
{
    [Fact]
    public void Select_ReturnsHighestScoredWithinBudget()
    {
        var candidates = new List<RelevanceScore>
        {
            new() { EntryId = "a", Content = "A", EstimatedTokens = 50, Score = 0.9 },
            new() { EntryId = "b", Content = "B", EstimatedTokens = 50, Score = 0.8 },
            new() { EntryId = "c", Content = "C", EstimatedTokens = 50, Score = 0.7 },
            new() { EntryId = "d", Content = "D", EstimatedTokens = 50, Score = 0.6 },
        };

        var selected = LeanKernel.Archivist.LeanKernelSelector.Select(candidates, tokenBudget: 120, minThreshold: 0.65);

        Assert.Equal(2, selected.Count);
        Assert.Equal("a", selected[0].EntryId);
        Assert.Equal("b", selected[1].EntryId);
    }

    [Fact]
    public void Select_ExcludesBelowThreshold()
    {
        var candidates = new List<RelevanceScore>
        {
            new() { EntryId = "a", Content = "A", EstimatedTokens = 10, Score = 0.9 },
            new() { EntryId = "b", Content = "B", EstimatedTokens = 10, Score = 0.3 },
        };

        var selected = LeanKernel.Archivist.LeanKernelSelector.Select(candidates, tokenBudget: 100);

        Assert.Single(selected);
        Assert.Equal("a", selected[0].EntryId);
    }

    [Fact]
    public void Select_ReturnsEmpty_WhenBudgetIsZero()
    {
        var candidates = new List<RelevanceScore>
        {
            new() { EntryId = "a", Content = "A", EstimatedTokens = 10, Score = 0.9 },
        };

        var selected = LeanKernel.Archivist.LeanKernelSelector.Select(candidates, tokenBudget: 0);
        Assert.Empty(selected);
    }

    [Fact]
    public void Score_ComputesWeightedAverage()
    {
        var score = LeanKernel.Archivist.LeanKernelSelector.Score(
            semanticSimilarity: 1.0,
            recencyDecay: 1.0,
            dimensionMatch: 1.0,
            interactionFrequency: 1.0);

        Assert.Equal(1.0, score, precision: 2);
    }

    [Fact]
    public void Score_ZeroInputs_ReturnsZero()
    {
        var score = LeanKernel.Archivist.LeanKernelSelector.Score(0, 0, 0, 0);
        Assert.Equal(0.0, score);
    }
}
