using FluentAssertions;

using LeanKernel.Services.Common.Contracts;
using LeanKernel.Services.Common.Publishing;
using LeanKernel.Services.Common.Queue;
using LeanKernel.Services.Common.Scheduler;
using LeanKernel.Services.Common;
using LeanKernel.Services.Learning.Configuration;

using Xunit;

namespace LeanKernel.Tests.Unit.Scheduler;

public sealed class SchedulerPrimitivesTests
{
    [Fact]
    public async Task BoundedTurnEventQueue_EnqueueAndDequeue_RoundTripsEvent()
    {
        var queue = new BoundedTurnEventQueue(2);
        var turn = CreateTurnEvent("turn-queue-1");

        var accepted = await queue.EnqueueAsync(turn, CancellationToken.None);
        accepted.Should().BeTrue();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await foreach (var item in queue.DequeueAllAsync(cts.Token))
        {
            item.TurnId.Should().Be("turn-queue-1");
            break;
        }
    }

    [Theory]
    [InlineData("*/5 * * * *", "2026-07-19T20:10:00Z", true)]
    [InlineData("*/5 * * * *", "2026-07-19T20:11:00Z", false)]
    [InlineData("0 8 * * 1", "2026-07-20T08:00:00Z", true)]
    [InlineData("0 8 * * 1", "2026-07-21T08:00:00Z", false)]
    [InlineData("0 0 31 2 *", "2026-02-01T00:00:00Z", false)]
    [InlineData("invalid cron", "2026-07-19T20:10:00Z", false)]
    public void CronScheduleEvaluator_IsDue_MatchesExpected(string cron, string utcNow, bool expected)
    {
        var isDue = CronScheduleEvaluator.IsDue(cron, DateTimeOffset.Parse(utcNow));
        isDue.Should().Be(expected);
    }

    [Fact]
    public async Task NoOpLearningEventPublisher_DoesNotThrow()
    {
        var publisher = new NoOpLearningEventPublisher();
        var act = () => publisher.PublishAsync(CreateTurnEvent("turn-noop"), CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void Options_Defaults_AreConfigured()
    {
        var learning = new LearningServiceOptions();
        learning.TimeoutSeconds.Should().Be(2);
        learning.IngestPath.Should().Be(LearningServiceRoutes.TurnEventsPath);

        var scheduler = new ScheduledJobDefinition();
        scheduler.Cron.Should().Be("*/5 * * * *");
        scheduler.Enabled.Should().BeTrue();

        var learningRuntime = new LearningRuntimeOptions();
        learningRuntime.Enabled.Should().BeTrue();
        learningRuntime.QueueCapacity.Should().Be(512);

        var schedulerRuntime = new SchedulerRuntimeOptions();
        schedulerRuntime.Enabled.Should().BeTrue();
        schedulerRuntime.PollIntervalSeconds.Should().Be(30);
    }

    private static CompletedTurnEvent CreateTurnEvent(string turnId)
    {
        return new CompletedTurnEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "session-queue",
            turnId,
            DateTimeOffset.UtcNow,
            [new TurnMessage("user", "hello")],
            [new TurnMessage("assistant", "world")]);
    }
}
