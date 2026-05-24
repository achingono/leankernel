using Cronos;
using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Scheduler;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Scheduler;

public class CronScheduleEvaluatorTests
{
    [Fact]
    public void GetNextOccurrence_returns_the_next_utc_occurrence_for_the_default_timezone()
    {
        var evaluator = CreateEvaluator();
        var job = CreateJob("daily-summary", "0 8 * * *");

        var occurrence = evaluator.GetNextOccurrence(job, DateTimeOffset.Parse("2025-05-20T07:15:00Z"));

        occurrence.Should().Be(DateTimeOffset.Parse("2025-05-20T08:00:00Z"));
    }

    [Fact]
    public void GetNextOccurrence_respects_the_configured_job_timezone()
    {
        var evaluator = CreateEvaluator();
        var job = CreateJob("ny-briefing", "0 8 * * *", parameters: new Dictionary<string, string>
        {
            ["timezone"] = "America/New_York",
        });

        var occurrence = evaluator.GetNextOccurrence(job, DateTimeOffset.Parse("2025-05-20T11:00:00Z"));

        occurrence.Should().Be(DateTimeOffset.Parse("2025-05-20T12:00:00Z"));
    }

    [Fact]
    public void IsDue_returns_true_when_a_new_occurrence_exists_after_the_last_execution()
    {
        var evaluator = CreateEvaluator();
        var job = CreateJob("daily-summary", "0 8 * * *");

        var isDue = evaluator.IsDue(
            job,
            DateTimeOffset.Parse("2025-05-21T08:01:00Z"),
            DateTimeOffset.Parse("2025-05-20T08:00:00Z"),
            out var scheduledAt);

        isDue.Should().BeTrue();
        scheduledAt.Should().Be(DateTimeOffset.Parse("2025-05-21T08:00:00Z"));
    }

    [Fact]
    public void GetNextOccurrence_throws_for_invalid_cron_expressions()
    {
        var evaluator = CreateEvaluator();
        var job = CreateJob("bad-job", "bad cron");

        var act = () => evaluator.GetNextOccurrence(job, DateTimeOffset.Parse("2025-05-20T07:15:00Z"));

        act.Should().Throw<CronFormatException>();
    }

    private static CronScheduleEvaluator CreateEvaluator(string defaultTimezone = "UTC")
    {
        var boundaryService = new TimeBoundaryService(Options.Create(new SchedulerConfig
        {
            DefaultTimezone = defaultTimezone,
        }));

        return new CronScheduleEvaluator(boundaryService, NullLogger<CronScheduleEvaluator>.Instance);
    }

    private static ScheduledJobDefinition CreateJob(
        string name,
        string cronExpression,
        string jobType = "agent-prompt",
        Dictionary<string, string>? parameters = null)
        => new()
        {
            Name = name,
            CronExpression = cronExpression,
            JobType = jobType,
            Prompt = "prompt",
            Parameters = parameters ?? [],
        };
}
