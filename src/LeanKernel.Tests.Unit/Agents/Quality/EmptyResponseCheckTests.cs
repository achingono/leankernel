using FluentAssertions;
using LeanKernel.Abstractions.Models;
using LeanKernel.Agents.Quality;

namespace LeanKernel.Tests.Unit.Agents.Quality;

public class EmptyResponseCheckTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Evaluate_fails_for_empty_or_whitespace_responses(string response)
    {
        var check = new EmptyResponseCheck();

        var result = check.Evaluate(CreateContext(response));

        result.Passed.Should().BeFalse();
        result.Score.Should().Be(0.0);
        result.Details.Should().Be("Response was empty or whitespace.");
    }

    [Fact]
    public void Evaluate_passes_for_non_empty_response()
    {
        var check = new EmptyResponseCheck();

        var result = check.Evaluate(CreateContext("A complete answer."));

        result.Passed.Should().BeTrue();
        result.Score.Should().Be(1.0);
        result.Details.Should().BeNull();
    }

    private static QualityEvaluationContext CreateContext(string response)
        => new()
        {
            UserMessage = "Summarize status and next steps.",
            Response = response,
            MinOutputLength = 10,
            MinConstraintCoverage = 0.6,
        };
}
