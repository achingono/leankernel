using FluentAssertions;

using LeanKernel.Services.Common.Contracts;
using LeanKernel.Services.Learning.Learning;

using Xunit;

namespace LeanKernel.Tests.Unit.Scheduler;

public sealed class OnboardingGapDetectorTests
{
    [Fact]
    public void DetectGaps_WhenIdentityDetailsMissing_ReturnsExpectedGaps()
    {
        var detector = new OnboardingGapDetector();
        var turn = CreateTurnEvent("Hi, can you help me organize my tasks?");

        var gaps = detector.DetectGaps(turn);

        gaps.Should().Contain(["name", "email", "timezone", "language"]);
    }

    [Fact]
    public void DetectGaps_WhenIdentityDetailsPresent_ReturnsNoGaps()
    {
        var detector = new OnboardingGapDetector();
        var turn = CreateTurnEvent("My name is Ada. My email is ada@example.com. My timezone is PST. I speak English.");

        var gaps = detector.DetectGaps(turn);

        gaps.Should().BeEmpty();
    }

    private static CompletedTurnEvent CreateTurnEvent(string userText)
    {
        return new CompletedTurnEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "session-a",
            "turn-a",
            DateTimeOffset.UtcNow,
            [new TurnMessage("user", userText)],
            [new TurnMessage("assistant", "ok")]);
    }
}
