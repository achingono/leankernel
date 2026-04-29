using LeanKernel.Core.Models;
using Xunit;

namespace LeanKernel.Tests.Unit.CoreTests;

public class RelevanceScoreTests
{
    [Fact]
    public void ComputeScore_AllOnes_Returns1()
    {
        var score = RelevanceScore.ComputeScore(1.0, 1.0, 1.0, 1.0);
        Assert.Equal(1.0, score, precision: 5);
    }

    [Fact]
    public void ComputeScore_AllZeros_Returns0()
    {
        var score = RelevanceScore.ComputeScore(0.0, 0.0, 0.0, 0.0);
        Assert.Equal(0.0, score, precision: 5);
    }

    [Fact]
    public void ComputeScore_WeightsApplied()
    {
        // semantic=1.0 only → 0.40
        var score = RelevanceScore.ComputeScore(1.0, 0.0, 0.0, 0.0);
        Assert.Equal(0.40, score, precision: 5);
    }

    [Fact]
    public void ComputeScore_RecencyWeight()
    {
        var score = RelevanceScore.ComputeScore(0.0, 1.0, 0.0, 0.0);
        Assert.Equal(0.20, score, precision: 5);
    }

    [Fact]
    public void ComputeScore_DimensionWeight()
    {
        var score = RelevanceScore.ComputeScore(0.0, 0.0, 1.0, 0.0);
        Assert.Equal(0.25, score, precision: 5);
    }

    [Fact]
    public void ComputeScore_FrequencyWeight()
    {
        var score = RelevanceScore.ComputeScore(0.0, 0.0, 0.0, 1.0);
        Assert.Equal(0.15, score, precision: 5);
    }

    [Fact]
    public void ComputeScore_MixedValues()
    {
        var score = RelevanceScore.ComputeScore(0.8, 0.5, 0.6, 0.3);
        var expected = (0.8 * 0.40) + (0.5 * 0.20) + (0.6 * 0.25) + (0.3 * 0.15);
        Assert.Equal(expected, score, precision: 5);
    }
}
