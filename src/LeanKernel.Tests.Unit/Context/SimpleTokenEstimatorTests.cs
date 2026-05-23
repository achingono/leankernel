using FluentAssertions;
using LeanKernel.Context;

namespace LeanKernel.Tests.Unit.Context;

public class SimpleTokenEstimatorTests
{
    private readonly SimpleTokenEstimator _estimator = new();

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("a", 1)]
    [InlineData("abcd", 1)]
    [InlineData("abcde", 2)]
    [InlineData("12345678", 2)]
    public void EstimateTokens_returns_expected_count(string? text, int expected)
    {
        _estimator.EstimateTokens(text!).Should().Be(expected);
    }
}
