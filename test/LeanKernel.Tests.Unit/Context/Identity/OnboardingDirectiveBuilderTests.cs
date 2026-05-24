using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Models;
using LeanKernel.Context.Identity;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Context.Identity;

public class OnboardingDirectiveBuilderTests
{
    [Fact]
    public void BuildDirective_limits_questions_and_includes_non_blocking_instruction()
    {
        var builder = new OnboardingDirectiveBuilder(Options.Create(new IdentityConfig
        {
            MaxOnboardingQuestionsPerTurn = 2,
        }));

        var directive = builder.BuildDirective(new OnboardingResult
        {
            HasGaps = true,
            Gaps =
            [
                new IdentityGap { FieldName = "preferred_name", GapCode = "missing_preferred_name" },
                new IdentityGap { FieldName = "timezone", GapCode = "missing_timezone" },
                new IdentityGap { FieldName = "locale", GapCode = "missing_locale" },
            ],
        });

        directive.Should().NotBeNull();
        directive.Should().Contain("Continue answering the user's current request.");
        directive!.Split('\n').Count(line => line.StartsWith("- ", StringComparison.Ordinal)).Should().Be(2);
    }

    [Fact]
    public void BuildDirective_returns_null_when_there_are_no_gaps()
    {
        var builder = new OnboardingDirectiveBuilder(Options.Create(new IdentityConfig()));

        var directive = builder.BuildDirective(new OnboardingResult());

        directive.Should().BeNull();
    }
}
