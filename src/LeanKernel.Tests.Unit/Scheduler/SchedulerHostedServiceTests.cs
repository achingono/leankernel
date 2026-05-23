using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Persistence;
using LeanKernel.Persistence.Entities;
using LeanKernel.Scheduler;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace LeanKernel.Tests.Unit.Scheduler;

public class SchedulerHostedServiceTests
{
    [Fact]
    public async Task ProcessTickAsync_does_not_double_fire_the_same_occurrence()
    {
        var config = CreateConfig(CreateAgentPromptJob("morning-briefing"));
        var runtime = new Mock<IAgentRuntime>(MockBehavior.Strict);
        var knowledge = new Mock<IKnowledgeService>(MockBehavior.Strict);
        runtime
            .Setup(candidate => candidate.RunTurnAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("done");

        var factory = CreateFactory();
        var timeProvider = new TestTimeProvider(DateTimeOffset.Parse("2025-05-20T08:00:30Z"));
        var service = CreateService(config, runtime.Object, knowledge.Object, factory, timeProvider);

        await service.ProcessTickAsync(timeProvider.GetUtcNow());
        await service.AwaitInFlightAsync();
        await service.ProcessTickAsync(timeProvider.GetUtcNow());
        await service.AwaitInFlightAsync();

        runtime.Verify(candidate => candidate.RunTurnAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<CancellationToken>()), Times.Once);
        await using var db = await factory.CreateDbContextAsync();
        db.ScheduledJobExecutions.Should().ContainSingle();
    }

    [Fact]
    public async Task ProcessTickAsync_uses_persisted_history_to_skip_the_already_processed_occurrence()
    {
        var config = CreateConfig(CreateAgentPromptJob("morning-briefing"));
        var runtime = new Mock<IAgentRuntime>(MockBehavior.Strict);
        var knowledge = new Mock<IKnowledgeService>(MockBehavior.Strict);
        var factory = CreateFactory();
        await SeedAsync(factory, db => db.ScheduledJobExecutions.Add(new ScheduledJobEntity
        {
            JobName = "morning-briefing",
            ScheduledAt = DateTimeOffset.Parse("2025-05-20T08:00:00Z"),
            StartedAt = DateTimeOffset.Parse("2025-05-20T08:00:01Z"),
            CompletedAt = DateTimeOffset.Parse("2025-05-20T08:00:02Z"),
            Success = true,
            Result = "done",
        }));

        var service = CreateService(
            config,
            runtime.Object,
            knowledge.Object,
            factory,
            new TestTimeProvider(DateTimeOffset.Parse("2025-05-20T08:00:30Z")));

        await service.ProcessTickAsync(DateTimeOffset.Parse("2025-05-20T08:00:30Z"));
        await service.AwaitInFlightAsync();

        runtime.Verify(candidate => candidate.RunTurnAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessTickAsync_respects_the_configured_max_concurrency()
    {
        var config = CreateConfig(
            CreateAgentPromptJob("job-1"),
            CreateAgentPromptJob("job-2"),
            maxConcurrentJobs: 1);
        var knowledge = new Mock<IKnowledgeService>(MockBehavior.Strict);
        var runtime = new Mock<IAgentRuntime>(MockBehavior.Strict);
        var activeCount = 0;
        var observedMaxConcurrency = 0;
        runtime
            .Setup(candidate => candidate.RunTurnAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                var current = Interlocked.Increment(ref activeCount);
                observedMaxConcurrency = Math.Max(observedMaxConcurrency, current);
                await Task.Delay(25);
                Interlocked.Decrement(ref activeCount);
                return "done";
            });

        var factory = CreateFactory();
        var timeProvider = new TestTimeProvider(DateTimeOffset.Parse("2025-05-20T08:00:30Z"));
        var service = CreateService(config, runtime.Object, knowledge.Object, factory, timeProvider);

        await service.ProcessTickAsync(timeProvider.GetUtcNow());
        await service.AwaitInFlightAsync();

        observedMaxConcurrency.Should().Be(1);
        await using var db = await factory.CreateDbContextAsync();
        db.ScheduledJobExecutions.Should().HaveCount(2);
    }

    [Fact]
    public async Task StopAsync_waits_for_in_flight_jobs_to_finish()
    {
        var config = CreateConfig(CreateAgentPromptJob("morning-briefing"));
        var knowledge = new Mock<IKnowledgeService>(MockBehavior.Strict);
        var runtime = new Mock<IAgentRuntime>(MockBehavior.Strict);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime
            .Setup(candidate => candidate.RunTurnAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                started.SetResult();
                return completion.Task;
            });

        var factory = CreateFactory();
        var timeProvider = new TestTimeProvider(DateTimeOffset.Parse("2025-05-20T08:00:30Z"));
        var service = CreateService(config, runtime.Object, knowledge.Object, factory, timeProvider);

        await service.StartAsync(CancellationToken.None);
        await started.Task;

        var stopTask = service.StopAsync(CancellationToken.None);
        stopTask.IsCompleted.Should().BeFalse();

        completion.SetResult("done");
        await stopTask;

        service.InFlightCount.Should().Be(0);
        runtime.Verify(candidate => candidate.RunTurnAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static SchedulerHostedService CreateService(
        SchedulerConfig config,
        IAgentRuntime runtime,
        IKnowledgeService knowledgeService,
        TestDbContextFactory factory,
        TestTimeProvider timeProvider)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(runtime);
        services.AddSingleton(knowledgeService);
        services.AddSingleton<IDbContextFactory<LeanKernelDbContext>>(factory);
        services.AddSingleton<TimeProvider>(timeProvider);
        services.AddSingleton<IOptions<SchedulerConfig>>(Options.Create(config));
        services.AddSingleton<TimeBoundaryService>();
        services.AddSingleton<CronScheduleEvaluator>();
        services.AddScoped<JobExecutor>();

        var provider = services.BuildServiceProvider();
        return new SchedulerHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            factory,
            provider.GetRequiredService<CronScheduleEvaluator>(),
            Options.Create(config),
            timeProvider,
            NullLogger<SchedulerHostedService>.Instance);
    }

    private static SchedulerConfig CreateConfig(ScheduledJobDefinition job, int maxConcurrentJobs = 2)
        => new()
        {
            Enabled = true,
            TickIntervalSeconds = 1,
            MaxConcurrentJobs = maxConcurrentJobs,
            DefaultTimezone = "UTC",
            Jobs = [job],
        };

    private static SchedulerConfig CreateConfig(
        ScheduledJobDefinition firstJob,
        ScheduledJobDefinition secondJob,
        int maxConcurrentJobs = 2)
        => new()
        {
            Enabled = true,
            TickIntervalSeconds = 1,
            MaxConcurrentJobs = maxConcurrentJobs,
            DefaultTimezone = "UTC",
            Jobs = [firstJob, secondJob],
        };

    private static ScheduledJobDefinition CreateAgentPromptJob(string name)
        => new()
        {
            Name = name,
            CronExpression = "0 8 * * *",
            JobType = "agent-prompt",
            Prompt = "Provide a summary",
            ChannelId = "scheduler",
            UserId = "system",
        };

    private static TestDbContextFactory CreateFactory()
        => new(new DbContextOptionsBuilder<LeanKernelDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static async Task SeedAsync(TestDbContextFactory factory, Action<LeanKernelDbContext> seed)
    {
        await using var db = await factory.CreateDbContextAsync();
        seed(db);
        await db.SaveChangesAsync();
    }
}
