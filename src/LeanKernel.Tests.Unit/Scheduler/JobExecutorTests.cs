using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Persistence;
using LeanKernel.Persistence.Entities;
using LeanKernel.Scheduler;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace LeanKernel.Tests.Unit.Scheduler;

public class JobExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_runs_agent_prompt_jobs_and_persists_the_execution()
    {
        var runtime = new Mock<IAgentRuntime>(MockBehavior.Strict);
        var knowledge = new Mock<IKnowledgeService>(MockBehavior.Strict);
        var factory = CreateFactory();
        var timeProvider = new TestTimeProvider(DateTimeOffset.Parse("2025-05-20T08:00:00Z"));
        runtime
            .Setup(candidate => candidate.RunTurnAsync(
                It.Is<LeanKernelMessage>(message =>
                    message.Content == "Provide a summary" &&
                    message.ChannelId == "scheduler" &&
                    message.SenderId == "system" &&
                    message.Metadata!["scheduler_job_name"] == "morning-briefing"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("summary");

        var executor = CreateExecutor(runtime.Object, knowledge.Object, factory, timeProvider);
        var job = new ScheduledJobDefinition
        {
            Name = "morning-briefing",
            CronExpression = "0 8 * * *",
            JobType = "agent-prompt",
            Prompt = "Provide a summary",
            ChannelId = "scheduler",
            UserId = "system",
        };

        var execution = await executor.ExecuteAsync(job, DateTimeOffset.Parse("2025-05-20T08:00:00Z"));

        execution.Success.Should().BeTrue();
        execution.Result.Should().Be("summary");
        runtime.VerifyAll();
        await using var db = await factory.CreateDbContextAsync();
        db.ScheduledJobExecutions.Should().ContainSingle();
        db.ScheduledJobExecutions.Single().JobName.Should().Be("morning-briefing");
    }

    [Fact]
    public async Task ExecuteAsync_refreshes_knowledge_pages_by_key()
    {
        var runtime = new Mock<IAgentRuntime>(MockBehavior.Strict);
        var knowledge = new Mock<IKnowledgeService>(MockBehavior.Strict);
        var factory = CreateFactory();
        var timeProvider = new TestTimeProvider(DateTimeOffset.Parse("2025-05-20T08:00:00Z"));
        knowledge
            .Setup(candidate => candidate.GetPageAsync("projects/atlas", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgePage
            {
                Key = "projects/atlas",
                Content = "# Atlas",
            });
        knowledge
            .Setup(candidate => candidate.PutPageAsync("projects/atlas", "# Atlas", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var executor = CreateExecutor(runtime.Object, knowledge.Object, factory, timeProvider);
        var job = new ScheduledJobDefinition
        {
            Name = "atlas-refresh",
            CronExpression = "0 8 * * *",
            JobType = "knowledge-refresh",
            Parameters = new Dictionary<string, string>
            {
                ["key"] = "projects/atlas",
            },
        };

        var execution = await executor.ExecuteAsync(job, DateTimeOffset.Parse("2025-05-20T08:00:00Z"));

        execution.Success.Should().BeTrue();
        execution.Result.Should().Contain("projects/atlas");
        knowledge.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_runs_maintenance_cleanup_tasks()
    {
        var runtime = new Mock<IAgentRuntime>(MockBehavior.Strict);
        var knowledge = new Mock<IKnowledgeService>(MockBehavior.Strict);
        var factory = CreateFactory();
        var timeProvider = new TestTimeProvider(DateTimeOffset.Parse("2025-05-20T08:00:00Z"));
        await SeedAsync(factory, db =>
        {
            db.DiagnosticEntries.AddRange(
                new DiagnosticEntryEntity
                {
                    Id = "old-diagnostic",
                    SessionId = "session-1",
                    Category = "scheduler",
                    Payload = "{}",
                    Timestamp = DateTimeOffset.Parse("2025-04-01T00:00:00Z"),
                },
                new DiagnosticEntryEntity
                {
                    Id = "fresh-diagnostic",
                    SessionId = "session-1",
                    Category = "scheduler",
                    Payload = "{}",
                    Timestamp = DateTimeOffset.Parse("2025-05-19T00:00:00Z"),
                });
            db.CompactionMarkers.AddRange(
                new CompactionMarkerEntity
                {
                    Id = Guid.NewGuid(),
                    SessionId = "session-1",
                    MarkerType = "compacted",
                    CompactedAt = DateTimeOffset.Parse("2025-04-01T00:00:00Z"),
                    OriginalTurnCount = 10,
                    OriginalTokenCount = 100,
                    CompactedTokenCount = 40,
                },
                new CompactionMarkerEntity
                {
                    Id = Guid.NewGuid(),
                    SessionId = "session-1",
                    MarkerType = "compacted",
                    CompactedAt = DateTimeOffset.Parse("2025-05-19T00:00:00Z"),
                    OriginalTurnCount = 10,
                    OriginalTokenCount = 100,
                    CompactedTokenCount = 40,
                });
        });

        var executor = CreateExecutor(runtime.Object, knowledge.Object, factory, timeProvider);
        var job = new ScheduledJobDefinition
        {
            Name = "maintenance",
            CronExpression = "0 2 * * 0",
            JobType = "maintenance",
            Parameters = new Dictionary<string, string>
            {
                ["task"] = "cleanup-all",
                ["retention_days"] = "30",
            },
        };

        var execution = await executor.ExecuteAsync(job, DateTimeOffset.Parse("2025-05-20T02:00:00Z"));

        execution.Success.Should().BeTrue();
        execution.Result.Should().Contain("removed 1 diagnostic entries and 1 compaction markers");
        await using var db = await factory.CreateDbContextAsync();
        db.DiagnosticEntries.Should().ContainSingle(entry => entry.Id == "fresh-diagnostic");
        db.CompactionMarkers.Should().ContainSingle(marker => marker.CompactedAt == DateTimeOffset.Parse("2025-05-19T00:00:00Z"));
    }

    [Fact]
    public async Task ExecuteAsync_records_failures_for_unsupported_job_types()
    {
        var runtime = new Mock<IAgentRuntime>(MockBehavior.Strict);
        var knowledge = new Mock<IKnowledgeService>(MockBehavior.Strict);
        var factory = CreateFactory();
        var timeProvider = new TestTimeProvider(DateTimeOffset.Parse("2025-05-20T08:00:00Z"));
        var executor = CreateExecutor(runtime.Object, knowledge.Object, factory, timeProvider);
        var job = new ScheduledJobDefinition
        {
            Name = "bad-job",
            CronExpression = "0 8 * * *",
            JobType = "unsupported",
        };

        var execution = await executor.ExecuteAsync(job, DateTimeOffset.Parse("2025-05-20T08:00:00Z"));

        execution.Success.Should().BeFalse();
        execution.Error.Should().Contain("Unsupported scheduled job type");
        await using var db = await factory.CreateDbContextAsync();
        db.ScheduledJobExecutions.Should().ContainSingle(entity => !entity.Success && entity.JobName == "bad-job");
    }

    private static JobExecutor CreateExecutor(
        IAgentRuntime runtime,
        IKnowledgeService knowledgeService,
        TestDbContextFactory factory,
        TestTimeProvider timeProvider)
    {
        var boundaryService = new TimeBoundaryService(Options.Create(new SchedulerConfig
        {
            DefaultTimezone = "UTC",
        }));

        return new JobExecutor(
            runtime,
            knowledgeService,
            factory,
            boundaryService,
            timeProvider,
            NullLogger<JobExecutor>.Instance);
    }

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
