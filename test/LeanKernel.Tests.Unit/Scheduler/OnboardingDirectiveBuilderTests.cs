using FluentAssertions;

using LeanKernel.Services.Common.Contracts;
using LeanKernel.Services.Learning.Learning;

using Xunit;

namespace LeanKernel.Tests.Unit.Scheduler;

public sealed class OnboardingDirectiveBuilderTests
{
    [Fact]
    public void BuildDirective_WithNoGaps_ReturnsNoopMessage()
    {
        var builder = new OnboardingDirectiveBuilder();
        var directive = builder.BuildDirective(CreateTurnEvent(), []);

        directive.Should().Be("No onboarding gaps were detected.");
    }

    [Fact]
    public void BuildDirective_WithKnownGaps_UsesFriendlyFieldNames()
    {
        var builder = new OnboardingDirectiveBuilder();
        var directive = builder.BuildDirective(CreateTurnEvent(), ["name", "email", "timezone", "language"]);

        directive.Should().Contain("preferred name");
        directive.Should().Contain("email address");
        directive.Should().Contain("timezone");
        directive.Should().Contain("preferred language");
        directive.Should().Contain("Turn=turn-b");
    }

    private static CompletedTurnEvent CreateTurnEvent()
    {
        return new CompletedTurnEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "session-b",
            "turn-b",
            DateTimeOffset.UtcNow,
            [new TurnMessage("user", "hello")],
            [new TurnMessage("assistant", "world")]);
    }
}
