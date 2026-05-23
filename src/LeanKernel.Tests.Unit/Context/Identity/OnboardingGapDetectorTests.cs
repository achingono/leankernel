using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Models;
using LeanKernel.Context.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Context.Identity;

public class OnboardingGapDetectorTests
{
    [Fact]
    public async Task DetectGapsAsync_returns_missing_weak_and_stale_gaps()
    {
        var detector = new OnboardingGapDetector(
            Options.Create(new IdentityConfig()),
            NullLogger<OnboardingGapDetector>.Instance);

        var result = await detector.DetectGapsAsync(new IdentityContext
        {
            UserId = "user-1",
            UserPreferences = new IdentityPage
            {
                Key = "identity-user-default",
                Content = string.Empty,
                Fields = new Dictionary<string, IdentityField>
                {
                    ["timezone"] = new() { Name = "timezone", Value = "UTC+2", Confidence = 0.3 },
                    ["recurring_goals"] = new() { Name = "recurring_goals", Value = "Ship weekly status update", LastUpdated = DateTimeOffset.UtcNow.AddDays(-120) },
                }
            },
            OverallConfidence = 0.4,
        });

        result.HasGaps.Should().BeTrue();
        var gapCodes = result.Gaps.Select(gap => gap.GapCode).ToList();
        gapCodes.Should().Contain("missing_preferred_name");
        gapCodes.Should().Contain("weak_timezone");
        gapCodes.Should().Contain("stale_recurring_goals");
    }

    [Fact]
    public async Task DetectGapsAsync_returns_placeholder_gap_for_placeholder_values()
    {
        var detector = new OnboardingGapDetector(
            Options.Create(new IdentityConfig
            {
                AllowedIdentityFields = ["preferred_name"]
            }),
            NullLogger<OnboardingGapDetector>.Instance);

        var result = await detector.DetectGapsAsync(new IdentityContext
        {
            UserId = "user-1",
            UserPreferences = new IdentityPage
            {
                Key = "identity-user-default",
                Content = string.Empty,
                Fields = new Dictionary<string, IdentityField>
                {
                    ["preferred_name"] = new() { Name = "preferred_name", Value = "TODO" }
                }
            },
        });

        result.Gaps.Should().ContainSingle(gap => gap.GapCode == "placeholder_preferred_name");
    }
}
