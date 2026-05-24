using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Scheduler;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Scheduler;

public class TimeBoundaryServiceTests
{
    [Theory]
    [InlineData("2025-05-20T05:59:00Z", TimeBoundary.Night)]
    [InlineData("2025-05-20T06:00:00Z", TimeBoundary.Morning)]
    [InlineData("2025-05-20T12:00:00Z", TimeBoundary.Afternoon)]
    [InlineData("2025-05-20T18:00:00Z", TimeBoundary.Evening)]
    [InlineData("2025-05-20T22:00:00Z", TimeBoundary.Night)]
    public void GetCurrentBoundary_returns_the_expected_boundary(string utcNow, TimeBoundary expectedBoundary)
    {
        var service = CreateService();

        var boundary = service.GetCurrentBoundary(DateTimeOffset.Parse(utcNow));

        boundary.Should().Be(expectedBoundary);
    }

    [Fact]
    public void GetBoundaryStart_returns_the_previous_evening_for_overnight_hours()
    {
        var service = CreateService();

        var start = service.GetStartOfCurrentBoundary(DateTimeOffset.Parse("2025-05-20T01:30:00Z"));

        start.Should().Be(DateTimeOffset.Parse("2025-05-19T22:00:00Z"));
    }

    [Fact]
    public void ResolveTimeZone_throws_for_invalid_timezones()
    {
        var service = CreateService();

        var act = () => service.ResolveTimeZone("not-a-timezone");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*not-a-timezone*");
    }

    private static TimeBoundaryService CreateService(string defaultTimezone = "UTC")
        => new(Options.Create(new SchedulerConfig
        {
            DefaultTimezone = defaultTimezone,
        }));
}
