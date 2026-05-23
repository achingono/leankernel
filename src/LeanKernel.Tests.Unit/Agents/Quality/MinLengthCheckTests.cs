using FluentAssertions;
using LeanKernel.Abstractions.Models;
using LeanKernel.Agents.Quality;

namespace LeanKernel.Tests.Unit.Agents.Quality;

public class MinLengthCheckTests
{
    [Fact]
    public void Evaluate_passes_when_response_meets_minimum_length()
    {
        var check = new MinLengthCheck();

        var result = check.Evaluate(CreateContext("This response easily clears the minimum threshold."));

        result.Passed.Should().BeTrue();
        result.Score.Should().Be(1.0);
        result.Details.Should().BeNull();
    }

    [Fact]
    public void Evaluate_fails_when_response_is_too_short()
    {
        var check = new MinLengthCheck();

        var result = check.Evaluate(CreateContext("short"));

        result.Passed.Should().BeFalse();
        result.Score.Should().BeApproximately(0.25, 0.001);
        result.Details.Should().Be("Response length 5 is below minimum 20.");
    }

    private static QualityEvaluationContext CreateContext(string response)
        => new()
        {
            UserMessage = "Summarize status and next steps.",
            Response = response,
            MinOutputLength = 20,
            MinConstraintCoverage = 0.6,
        };
}
