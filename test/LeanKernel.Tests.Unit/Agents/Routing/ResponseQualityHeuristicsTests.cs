using FluentAssertions;
using LeanKernel.Agents.Routing;

namespace LeanKernel.Tests.Unit.Agents.Routing;

public class ResponseQualityHeuristicsTests
{
    private static readonly string[] DefaultPatterns = ["I cannot"];

    [Fact]
    public void LooksLikeRefusal_returns_false_when_response_is_null()
    {
        ResponseQualityHeuristics.LooksLikeRefusal(null!, DefaultPatterns).Should().BeFalse();
    }

    [Fact]
    public void LooksLikeRefusal_throws_when_patterns_is_null()
    {
        var act = () => ResponseQualityHeuristics.LooksLikeRefusal("hello", null!);

        act.Should().Throw<NullReferenceException>();
    }

    [Fact]
    public void LooksLikeRefusal_returns_false_when_patterns_is_empty()
    {
        ResponseQualityHeuristics.LooksLikeRefusal("hello", []).Should().BeFalse();
    }

    [Fact]
    public void LooksLikeRefusal_returns_true_when_pattern_matches_case_insensitively()
    {
        ResponseQualityHeuristics.LooksLikeRefusal("I cannot do that", DefaultPatterns).Should().BeTrue();
    }

    [Fact]
    public void LooksLikeRefusal_returns_true_when_pattern_matches_different_case()
    {
        ResponseQualityHeuristics.LooksLikeRefusal("i cannot", ["I Cannot"]).Should().BeTrue();
    }

    [Fact]
    public void LooksLikeRefusal_returns_false_when_no_pattern_matches()
    {
        ResponseQualityHeuristics.LooksLikeRefusal("sure thing", DefaultPatterns).Should().BeFalse();
    }

    [Fact]
    public void LooksLikeRefusal_returns_true_when_second_pattern_matches()
    {
        var patterns = new[] { "first pattern", "sure thing" };

        ResponseQualityHeuristics.LooksLikeRefusal("sure thing", patterns).Should().BeTrue();
    }

    [Fact]
    public void LooksLikeRefusal_skips_whitespace_patterns()
    {
        var patterns = new[] { "   ", "I cannot" };

        ResponseQualityHeuristics.LooksLikeRefusal("I cannot do that", patterns).Should().BeTrue();
    }
}
