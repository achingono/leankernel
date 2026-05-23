using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Enums;
using LeanKernel.Abstractions.Models;
using LeanKernel.Agents.Quality;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Agents.Quality;

public class ResponseQualityGateTests
{
    [Fact]
    public void Evaluate_returns_passed_when_all_checks_succeed()
    {
        var gate = CreateGate();

        var result = gate.Evaluate(new QualityEvaluationContext
        {
            UserMessage = "Summarize the Atlas status milestones risks owners and next steps.",
            Response = "Atlas status summary with milestones, risks, owners, and next steps for the current release.",
            MinOutputLength = 20,
            MinConstraintCoverage = 0.6,
        });

        result.Passed.Should().BeTrue();
        result.Outcome.Should().Be(QualityOutcome.Passed);
        result.Checks.Should().HaveCount(4);
        result.OverallScore.Should().Be(1.0);
        result.FailureReason.Should().BeNull();
    }

    [Fact]
    public void Evaluate_returns_the_first_failure_outcome_and_keeps_all_check_results()
    {
        var gate = CreateGate();

        var result = gate.Evaluate(new QualityEvaluationContext
        {
            UserMessage = "Summarize the Atlas status milestones risks owners and next steps.",
            Response = "   ",
            MinOutputLength = 20,
            MinConstraintCoverage = 0.6,
        });

        result.Passed.Should().BeFalse();
        result.Outcome.Should().Be(QualityOutcome.FailedEmpty);
        result.Checks.Should().HaveCount(4);
        result.Checks[0].CheckName.Should().Be("empty-response");
        result.Checks[0].Passed.Should().BeFalse();
        result.Checks.Should().Contain(check => check.CheckName == "min-length" && !check.Passed);
        result.FailureReason.Should().Be("Response was empty or whitespace.");
    }

    private static ResponseQualityGate CreateGate()
    {
        var config = new LeanKernelConfig
        {
            Routing = new RoutingConfig
            {
                RefusalPatterns = ["I cannot", "As an AI language model"]
            }
        };

        return new ResponseQualityGate(
            new EmptyResponseCheck(),
            new MinLengthCheck(),
            new RefusalDetectionCheck(Options.Create(config)),
            new ConstraintCoverageCheck());
    }
}
