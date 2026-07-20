using System.Collections.Concurrent;
using System.Text.Json;

using FluentAssertions;

using LeanKernel.Data;
using LeanKernel.Entities;
using LeanKernel.Logic.Providers;
using LeanKernel.Services.Common.Contracts;
using LeanKernel.Services.Common.Queue;
using LeanKernel.Services.Common.Scheduler;
using LeanKernel.Services.Learning.Configuration;
using LeanKernel.Services.Learning.Learning;
using LeanKernel.Services.Learning.Scheduler;
using LeanKernel.Tests.Unit.TestDoubles;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Scheduler;

public sealed class LearningAndSchedulerIntegrationTests
{
    [Fact]
    public async Task LearningWorker_WithRealPipeline_ProcessesQueueAndWritesAllLearningArtifacts()
    {
        var memoryClient = new RecordingMemoryClient();
        var services = new ServiceCollection();
        services.AddScoped<IMemoryClient>(_ => memoryClient);
        services.AddScoped<IKnowledgePageUpdateCoordinator, KnowledgePageUpdateCoordinator>();
        services.AddScoped<ILearningPipelineStep, FactExtractionLearningStep>();
        services.AddScoped<ILearningPipelineStep, IdentityIntentLearningStep>();
        services.AddScoped<ILearningPipelineStep, CapabilityGapLearningStep>();
        services.AddScoped<ILearningPipelineStep, EngagementTrackingLearningStep>();
        services.AddScoped<ISelfImprovementPipeline, SelfImprovementPipeline>();

        await using var rootProvider = services.BuildServiceProvider();

        var queue = new BoundedTurnEventQueue(8);
        var worker = new LearningBackgroundWorker(
            rootProvider.GetRequiredService<IServiceScopeFactory>(),
            queue,
            Options.Create(new LearningRuntimeOptions { Enabled = true }),
            Mock.Of<ILogger<LearningBackgroundWorker>>());

        var turn = CreateTurnEvent(
            userText: "My name is Ada and my email is ada@example.com.",
            assistantText: "I cannot access your calendar right now. Ada lives in London and likes tea.");

        await queue.EnqueueAsync(turn, CancellationToken.None);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(
            () => memoryClient.Keys.Any(static key => key.StartsWith("facts/what/learned/", StringComparison.Ordinal))
                  && memoryClient.Keys.Any(static key => key.StartsWith("identity/intent/", StringComparison.Ordinal))
                  && memoryClient.Keys.Any(static key => key.StartsWith("capability/gap/", StringComparison.Ordinal))
                  && memoryClient.Keys.Any(static key => key.StartsWith("engagement/signal/", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(3));
        await worker.StopAsync(CancellationToken.None);

        memoryClient.Keys.Should().Contain(key => key.StartsWith("facts/what/learned/", StringComparison.Ordinal));
        memoryClient.Keys.Should().Contain(key => key.StartsWith("identity/intent/", StringComparison.Ordinal));
        memoryClient.Keys.Should().Contain(key => key.StartsWith("capability/gap/", StringComparison.Ordinal));
        memoryClient.Keys.Should().Contain(key => key.StartsWith("engagement/signal/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SchedulerHostedService_WithDbBackedJobs_EvaluatesCronAndExecutesJob()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var dbOptions = new DbContextOptionsBuilder<EntityContext>()
            .UseSqlite(connection)
            .Options;

        var tenantId = Guid.NewGuid();
        var channelId = Guid.NewGuid();

        await using (var context = new EntityContext(dbOptions))
        {
            await context.Database.EnsureCreatedAsync();

            context.Tenants.Add(new TenantEntity
            {
                Id = tenantId,
                Name = "Scheduler Tenant",
                Description = "integration",
                HostName = "scheduler.local",
                IsActive = true,
                CreatedOn = DateTime.UtcNow,
                CreatedBy = new Badge { Id = Guid.NewGuid(), FullName = "Scheduler", Email = "scheduler@test.local" }
            });

            context.Channels.Add(new ChannelEntity { Id = channelId, Name = "openai-http" });

            context.ScheduledJobs.Add(new ScheduledJobEntity
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ChannelId = channelId,
                Name = "integration-job",
                Cron = "*/5 * * * *",
                Enabled = true,
                JobType = "integration.job",
                Payload = "{\"source\":\"integration\"}"
            });

            await context.SaveChangesAsync();
        }

        var jobProvider = new DbScheduledJobDefinitionProvider(new TestDbContextFactory(dbOptions));
        var handler = new RecordingScheduledJobHandler();
        var executor = new ScheduledJobExecutor([handler], Mock.Of<ILogger<ScheduledJobExecutor>>());

        var services = new ServiceCollection();
        services.AddScoped<IScheduledJobDefinitionProvider>(_ => jobProvider);
        services.AddScoped<IScheduledJobExecutor>(_ => executor);
        await using var rootProvider = services.BuildServiceProvider();

        var scheduler = new SchedulerHostedService(
            rootProvider.GetRequiredService<IServiceScopeFactory>(),
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 19, 20, 10, 0, TimeSpan.Zero)),
            Options.Create(new SchedulerRuntimeOptions
            {
                Enabled = true,
                PollIntervalSeconds = 1
            }),
            Mock.Of<ILogger<SchedulerHostedService>>());

        await scheduler.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => handler.ExecutedJobs.Count > 0, TimeSpan.FromSeconds(3));
        await scheduler.StopAsync(CancellationToken.None);

        handler.ExecutedJobs.Should().ContainSingle();
        var executed = handler.ExecutedJobs.Single();
        executed.Name.Should().Be("integration-job");
        executed.TenantId.Should().Be(tenantId);
        executed.ChannelId.Should().Be(channelId);
    }

    private static CompletedTurnEvent CreateTurnEvent(string userText, string assistantText)
    {
        return new CompletedTurnEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "session-integration",
            "turn-integration",
            DateTimeOffset.UtcNow,
            [new TurnMessage("user", userText)],
            [new TurnMessage("assistant", assistantText)]);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("Timed out waiting for expected asynchronous condition.");
    }

    private sealed class RecordingMemoryClient : IMemoryClient
    {
        private readonly ConcurrentBag<(MemoryScope Scope, string Key, string Content)> _writes = [];

        public IReadOnlyCollection<string> Keys => _writes.Select(static write => write.Key).ToList();

        public Task<IReadOnlyList<MemoryItem>> SearchMemoriesAsync(MemoryScope scope, string query, int maxResults = 10, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<MemoryItem>>([]);

        public Task SaveMemoryAsync(MemoryScope scope, string key, string content, CancellationToken ct = default)
        {
            _writes.Add((scope, key, content));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingScheduledJobHandler : IScheduledJobHandler
    {
        private readonly ConcurrentBag<ScheduledJobDefinition> _executedJobs = [];

        public string JobType => "integration.job";

        public IReadOnlyCollection<ScheduledJobDefinition> ExecutedJobs => _executedJobs.ToList();

        public Task ExecuteAsync(ScheduledJobDefinition job, JsonElement? payload, CancellationToken cancellationToken = default)
        {
            _executedJobs.Add(job);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
