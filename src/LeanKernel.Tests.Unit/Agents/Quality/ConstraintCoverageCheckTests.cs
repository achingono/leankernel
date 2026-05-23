using FluentAssertions;
using LeanKernel.Abstractions.Models;
using LeanKernel.Agents.Quality;

namespace LeanKernel.Tests.Unit.Agents.Quality;

public class ConstraintCoverageCheckTests
{
    [Fact]
    public void Evaluate_passes_when_derived_constraint_coverage_meets_threshold()
    {
        var check = new ConstraintCoverageCheck();

        var result = check.Evaluate(new QualityEvaluationContext
        {
            UserMessage = "Summarize the Atlas status milestones risks owners and next steps.",
            Response = "Atlas status update with milestones, risks, owners, and next steps for the current release.",
            MinOutputLength = 10,
            MinConstraintCoverage = 0.6,
        });

        result.Passed.Should().BeTrue();
        result.Score.Should().BeGreaterThanOrEqualTo(0.6);
    }

    [Fact]
    public void Evaluate_fails_when_expected_constraints_are_missing()
    {
        var check = new ConstraintCoverageCheck();

        var result = check.Evaluate(new QualityEvaluationContext
        {
            UserMessage = "Unused because explicit constraints are provided.",
            Response = "Detailed summary that only mentions milestones and risks for this release.",
            MinOutputLength = 10,
            MinConstraintCoverage = 0.75,
            ExpectedConstraints = ["milestones", "risks", "owners", "next steps"]
        });

        result.Passed.Should().BeFalse();
        result.Score.Should().Be(0.5);
        result.Details.Should().Be("Matched 2 of 4 constraints (0.50).");
    }
}
