using LeanKernel.Core.Enums;
using LeanKernel.Core.Models;

namespace LeanKernel.Tests.Unit.Archivist;

public class RelevanceScorerTests
{
    [Fact]
    public void RecencyDecay_ReturnsOneForToday()
    {
        var decay = LeanKernel.Archivist.RelevanceScorer.RecencyDecay(DateTimeOffset.UtcNow);
        Assert.InRange(decay, 0.99, 1.0);
    }

    [Fact]
    public void RecencyDecay_ReturnsZeroForOldEntries()
    {
        var decay = LeanKernel.Archivist.RelevanceScorer.RecencyDecay(
            DateTimeOffset.UtcNow.AddDays(-100));
        Assert.Equal(0.0, decay);
    }

    [Fact]
    public void RecencyDecay_DecaysLinearly()
    {
        var halfLife = LeanKernel.Archivist.RelevanceScorer.RecencyDecay(
            DateTimeOffset.UtcNow.AddDays(-45), decayDays: 90.0);
        Assert.InRange(halfLife, 0.48, 0.52);
    }

    [Fact]
    public void DimensionMatch_ReturnsOneForActive()
    {
        var match = LeanKernel.Archivist.RelevanceScorer.DimensionMatch(
            WikiDimension.Who,
            new HashSet<WikiDimension> { WikiDimension.Who, WikiDimension.What });
        Assert.Equal(1.0, match);
    }

    [Fact]
    public void DimensionMatch_ReturnsLowForInactive()
    {
        var match = LeanKernel.Archivist.RelevanceScorer.DimensionMatch(
            WikiDimension.Where,
            new HashSet<WikiDimension> { WikiDimension.Who, WikiDimension.What });
        Assert.Equal(0.2, match);
    }

    [Fact]
    public void InteractionFrequency_ZeroForNoAccess()
    {
        var freq = LeanKernel.Archivist.RelevanceScorer.InteractionFrequency(0);
        Assert.Equal(0.0, freq);
    }

    [Fact]
    public void InteractionFrequency_CappedAtOne()
    {
        var freq = LeanKernel.Archivist.RelevanceScorer.InteractionFrequency(1000);
        Assert.InRange(freq, 0.99, 1.0);
    }

    [Fact]
    public void InteractionFrequency_ScalesLogarithmically()
    {
        var low = LeanKernel.Archivist.RelevanceScorer.InteractionFrequency(5);
        var mid = LeanKernel.Archivist.RelevanceScorer.InteractionFrequency(50);
        var high = LeanKernel.Archivist.RelevanceScorer.InteractionFrequency(95);

        Assert.True(low < mid);
        Assert.True(mid < high);
    }

    [Fact]
    public void Enrich_ComputesCompositeScore()
    {
        var candidate = new RelevanceScore
        {
            EntryId = "test",
            Content = "Test content",
            EstimatedTokens = 10,
            RecencyDecay = 0.8,
            DimensionMatch = 1.0,
            InteractionFrequency = 0.5
        };

        var enriched = LeanKernel.Archivist.RelevanceScorer.Enrich(
            candidate,
            new HashSet<WikiDimension> { WikiDimension.Who },
            semanticSimilarity: 0.9);

        Assert.True(enriched.Score > 0.0);
        Assert.Equal(0.9, enriched.SemanticSimilarity);
    }

    [Fact]
    public void ComputeSourceAwareScore_VectorSource_UsesSemanticSimilarityDirectly()
    {
        var vectorResult = new RelevanceScore
        {
            EntryId = "vec-1",
            Content = "Vector content",
            EstimatedTokens = 10,
            SemanticSimilarity = 0.85,
            RecencyDecay = 0.0,
            DimensionMatch = 0.0,
            InteractionFrequency = 0.0,
            SourceType = RelevanceSourceType.Vector
        };

        var score = vectorResult.ComputeSourceAwareScore();
        Assert.Equal(0.85, score);
    }

    [Fact]
    public void ComputeSourceAwareScore_WikiSource_UsesMultiFactorFormula()
    {
        var wikiResult = new RelevanceScore
        {
            EntryId = "wiki-1",
            Content = "Wiki content",
            EstimatedTokens = 10,
            SemanticSimilarity = 0.8,
            RecencyDecay = 1.0,
            DimensionMatch = 1.0,
            InteractionFrequency = 0.5,
            SourceType = RelevanceSourceType.Wiki
        };

        var score = wikiResult.ComputeSourceAwareScore();
        var expected = (0.8 * 0.40) + (1.0 * 0.20) + (1.0 * 0.25) + (0.5 * 0.15);
        Assert.Equal(expected, score, precision: 10);
    }

    [Fact]
    public void ComputeSourceAwareScore_VectorWithZeroOtherFactors_StillPassesThreshold()
    {
        // This was the critical bug: vector results had only SemanticSimilarity,
        // so multi-factor scoring produced max 0.40, below 0.65 threshold
        var vectorResult = new RelevanceScore
        {
            EntryId = "vec-2",
            Content = "Important document",
            EstimatedTokens = 10,
            SemanticSimilarity = 0.75,
            RecencyDecay = 0.0,
            DimensionMatch = 0.0,
            InteractionFrequency = 0.0,
            SourceType = RelevanceSourceType.Vector
        };

        var score = vectorResult.ComputeSourceAwareScore();
        Assert.True(score >= 0.65, $"Vector score {score} should exceed threshold 0.65");
    }
}
