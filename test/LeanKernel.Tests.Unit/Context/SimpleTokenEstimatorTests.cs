using FluentAssertions;
using LeanKernel.Context;

namespace LeanKernel.Tests.Unit.Context;

public class SimpleTokenEstimatorTests
{
    private readonly SimpleTokenEstimator _estimator = new();

    [Fact]
    public void EstimateTokens_null_returns_zero()
    {
        _estimator.EstimateTokens(null!).Should().Be(0);
    }

    [Fact]
    public void EstimateTokens_empty_string_returns_zero()
    {
        _estimator.EstimateTokens("").Should().Be(0);
    }

    [Fact]
    public void EstimateTokens_four_chars_returns_one()
    {
        _estimator.EstimateTokens("abcd").Should().Be(1);
    }

    [Fact]
    public void EstimateTokens_five_chars_returns_two()
    {
        _estimator.EstimateTokens("abcde").Should().Be(2);
    }

    [Fact]
    public void EstimateTokens_eight_chars_returns_two()
    {
        _estimator.EstimateTokens("abcdefgh").Should().Be(2);
    }

    [Fact]
    public void EstimateTokens_nine_chars_returns_three()
    {
        _estimator.EstimateTokens("abcdefghi").Should().Be(3);
    }
}
