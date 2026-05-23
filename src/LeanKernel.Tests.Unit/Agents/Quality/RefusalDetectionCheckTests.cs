using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Models;
using LeanKernel.Agents.Quality;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Agents.Quality;

public class RefusalDetectionCheckTests
{
    [Fact]
    public void Evaluate_fails_when_a_refusal_pattern_is_present_case_insensitively()
    {
        var check = CreateCheck();

        var result = check.Evaluate(CreateContext("I CANNOT provide that response."));

        result.Passed.Should().BeFalse();
        result.Score.Should().Be(0.0);
        result.Details.Should().Be("Matched refusal pattern 'I cannot'.");
    }

    [Fact]
    public void Evaluate_passes_when_no_refusal_pattern_is_present()
    {
        var check = CreateCheck();

        var result = check.Evaluate(CreateContext("Here is the detailed implementation summary you requested."));

        result.Passed.Should().BeTrue();
        result.Score.Should().Be(1.0);
        result.Details.Should().BeNull();
    }

    private static RefusalDetectionCheck CreateCheck()
        => new(Options.Create(new LeanKernelConfig
        {
            Routing = new RoutingConfig
            {
                RefusalPatterns = ["I cannot"]
            }
        }));

    private static QualityEvaluationContext CreateContext(string response)
        => new()
        {
            UserMessage = "Summarize status and next steps.",
            Response = response,
            MinOutputLength = 10,
            MinConstraintCoverage = 0.6,
        };
}
