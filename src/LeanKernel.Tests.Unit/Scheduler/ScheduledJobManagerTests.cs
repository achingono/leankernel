using Microsoft.Extensions.Logging.Abstractions;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Scheduler;

namespace LeanKernel.Tests.Unit.Scheduler;

public class ScheduledJobManagerTests
{
    [Fact]
    public async Task CreateAsync_ScopedJob_DefaultsOwnerAndDeliveryToActor()
    {
        var store = new InMemoryScheduledJobStore();
        var executor = new FakeExecutor();
        var manager = new ScheduledJobManager(store, executor, NullLogger<ScheduledJobManager>.Instance);

        var actor = new ScheduledJobActor
        {
            UserId = "user-a",
            ChannelId = "signal",
            SessionId = "sess-a",
            IsAdmin = false
        };

        var created = await manager.CreateAsync(new ScheduledJobCreateRequest
        {
            Id = "job-a",
            Name = "Morning brief",
            CronExpression = "0 7 * * *",
            PayloadMessage = "Run morning brief",
            TimeZoneId = "UTC"
        }, actor, CancellationToken.None);

        Assert.Equal("user-a", created.Definition.OwnerUserId);
        Assert.Equal("signal", created.Definition.OwnerChannelId);
        Assert.Equal("signal", created.Definition.DeliveryChannel);
        Assert.Equal("user-a", created.Definition.DeliveryRecipient);
        Assert.Equal(ScheduledJobScope.Scoped, created.Definition.Scope);
        Assert.NotNull(created.State.NextRunAtUtc);
    }

    [Fact]
    public async Task ListAsync_NonAdmin_SeesOnlyOwnedJobs()
    {
        var store = new InMemoryScheduledJobStore();
        var executor = new FakeExecutor();
        var manager = new ScheduledJobManager(store, executor, NullLogger<ScheduledJobManager>.Instance);

        var actorA = new ScheduledJobActor
        {
            UserId = "user-a",
            ChannelId = "signal",
            SessionId = "sess-a"
        };

        var actorB = new ScheduledJobActor
        {
            UserId = "user-b",
            ChannelId = "signal",
            SessionId = "sess-b"
        };

        await manager.CreateAsync(new ScheduledJobCreateRequest
        {
            Id = "job-a",
            Name = "A",
            CronExpression = "0 7 * * *",
            PayloadMessage = "A"
        }, actorA, CancellationToken.None);

        await manager.CreateAsync(new ScheduledJobCreateRequest
        {
            Id = "job-b",
            Name = "B",
            CronExpression = "0 8 * * *",
            PayloadMessage = "B"
        }, actorB, CancellationToken.None);

        var visibleToA = await manager.ListAsync(new ScheduledJobListOptions(), actorA, CancellationToken.None);
        Assert.Single(visibleToA);
        Assert.Equal("job-a", visibleToA[0].Definition.Id);
    }

    [Fact]
    public async Task CreateAsync_GlobalScope_RequiresAdmin()
    {
        var store = new InMemoryScheduledJobStore();
        var executor = new FakeExecutor();
        var manager = new ScheduledJobManager(store, executor, NullLogger<ScheduledJobManager>.Instance);

        var actor = new ScheduledJobActor
        {
            UserId = "user-a",
            ChannelId = "signal",
            SessionId = "sess-a",
            IsAdmin = false
        };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => manager.CreateAsync(
            new ScheduledJobCreateRequest
            {
                Id = "global-job",
                Name = "Global",
                CronExpression = "0 7 * * *",
                PayloadMessage = "Global message",
                Scope = ScheduledJobScope.Global,
                ScopeReason = "Need cross-team run"
            },
            actor,
            CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_GlobalScope_RequiresReasonEvenForAdmin()
    {
        var store = new InMemoryScheduledJobStore();
        var executor = new FakeExecutor();
        var manager = new ScheduledJobManager(store, executor, NullLogger<ScheduledJobManager>.Instance);

        var admin = new ScheduledJobActor
        {
            UserId = "admin",
            ChannelId = "signal",
            SessionId = "sess-admin",
            IsAdmin = true
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.CreateAsync(
            new ScheduledJobCreateRequest
            {
                Id = "global-job",
                Name = "Global",
                CronExpression = "0 7 * * *",
                PayloadMessage = "Global message",
                Scope = ScheduledJobScope.Global
            },
            admin,
            CancellationToken.None));
    }

    [Fact]
    public async Task TriggerAsync_UpdatesLastRunState()
    {
        var store = new InMemoryScheduledJobStore();
        var executor = new FakeExecutor();
        var manager = new ScheduledJobManager(store, executor, NullLogger<ScheduledJobManager>.Instance);

        var actor = new ScheduledJobActor
        {
            UserId = "user-a",
            ChannelId = "signal",
            SessionId = "sess-a",
            IsAdmin = false
        };

        await manager.CreateAsync(new ScheduledJobCreateRequest
        {
            Id = "job-a",
            Name = "A",
            CronExpression = "0 7 * * *",
            PayloadMessage = "A"
        }, actor, CancellationToken.None);

        var triggered = await manager.TriggerAsync("job-a", actor, CancellationToken.None);

        Assert.Equal("ok", triggered.State.LastStatus);
        Assert.NotNull(triggered.State.LastRunAtUtc);
    }

    [Fact]
    public async Task TriggerAsync_AtSchedule_DisablesAfterRun()
    {
        var store = new InMemoryScheduledJobStore();
        var executor = new FakeExecutor();
        var manager = new ScheduledJobManager(store, executor, NullLogger<ScheduledJobManager>.Instance);

        var actor = new ScheduledJobActor
        {
            UserId = "user-a",
            ChannelId = "signal",
            SessionId = "sess-a",
            IsAdmin = false
        };

        await manager.CreateAsync(new ScheduledJobCreateRequest
        {
            Id = "job-once",
            Name = "Once",
            ScheduleKind = ScheduledJobScheduleKind.At,
            RunAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            PayloadMessage = "A"
        }, actor, CancellationToken.None);

        var triggered = await manager.TriggerAsync("job-once", actor, CancellationToken.None);

        Assert.False(triggered.Definition.Enabled);
        Assert.Null(triggered.State.NextRunAtUtc);
    }

    [Fact]
    public async Task TriggerAsync_SkipOverlap_DoesNotExecuteSecondRun()
    {
        var store = new InMemoryScheduledJobStore();
        var executor = new BlockingExecutor();
        var manager = new ScheduledJobManager(store, executor, NullLogger<ScheduledJobManager>.Instance);

        var actor = new ScheduledJobActor
        {
            UserId = "user-a",
            ChannelId = "signal",
            SessionId = "sess-a",
            IsAdmin = false
        };

        await manager.CreateAsync(new ScheduledJobCreateRequest
        {
            Id = "job-overlap",
            Name = "Overlap",
            CronExpression = "* * * * *",
            PayloadMessage = "A",
            OverlapPolicy = ScheduledJobOverlapPolicy.Skip
        }, actor, CancellationToken.None);

        var firstRun = manager.TriggerAsync("job-overlap", actor, CancellationToken.None);
        await executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await manager.TriggerAsync("job-overlap", actor, CancellationToken.None);
        executor.Release.TrySetResult(true);
        await firstRun;

        var view = await manager.GetAsync("job-overlap", actor, CancellationToken.None);
        Assert.NotNull(view);
        Assert.Equal(1, executor.ExecuteCount);
        Assert.Equal(1, view!.State.ConsecutiveSkips);
    }

    [Fact]
    public async Task UpdateAndDelete_RequireOwnershipOrAdmin()
    {
        var store = new InMemoryScheduledJobStore();
        var executor = new FakeExecutor();
        var manager = new ScheduledJobManager(store, executor, NullLogger<ScheduledJobManager>.Instance);

        var owner = new ScheduledJobActor
        {
            UserId = "owner",
            ChannelId = "signal",
            SessionId = "sess-owner"
        };

        var otherUser = new ScheduledJobActor
        {
            UserId = "other",
            ChannelId = "signal",
            SessionId = "sess-other"
        };

        await manager.CreateAsync(new ScheduledJobCreateRequest
        {
            Id = "job-x",
            Name = "X",
            CronExpression = "0 7 * * *",
            PayloadMessage = "X"
        }, owner, CancellationToken.None);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            manager.UpdateAsync("job-x", new ScheduledJobUpdateRequest { Name = "Hacked" }, otherUser, CancellationToken.None));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            manager.DeleteAsync("job-x", otherUser, CancellationToken.None));

        var admin = new ScheduledJobActor
        {
            UserId = "admin",
            ChannelId = "signal",
            SessionId = "sess-admin",
            IsAdmin = true
        };

        var updated = await manager.UpdateAsync("job-x", new ScheduledJobUpdateRequest { Name = "Admin Updated" }, admin, CancellationToken.None);
        Assert.Equal("Admin Updated", updated.Definition.Name);
        await manager.DeleteAsync("job-x", admin, CancellationToken.None);

        var lookup = await manager.GetAsync("job-x", admin, CancellationToken.None);
        Assert.Null(lookup);
    }

    private sealed class InMemoryScheduledJobStore : IScheduledJobStore
    {
        private ScheduledJobStoreSnapshot _snapshot = new();

        public Task<ScheduledJobStoreSnapshot> LoadAsync(CancellationToken ct) =>
            Task.FromResult(new ScheduledJobStoreSnapshot
            {
                Version = _snapshot.Version,
                Jobs = _snapshot.Jobs.Select(CloneJob).ToList(),
                States = _snapshot.States.ToDictionary(
                    kvp => kvp.Key,
                    kvp => CloneState(kvp.Value),
                    StringComparer.OrdinalIgnoreCase)
            });

        public Task SaveAsync(ScheduledJobStoreSnapshot snapshot, CancellationToken ct)
        {
            _snapshot = new ScheduledJobStoreSnapshot
            {
                Version = snapshot.Version,
                Jobs = snapshot.Jobs.Select(CloneJob).ToList(),
                States = snapshot.States.ToDictionary(
                    kvp => kvp.Key,
                    kvp => CloneState(kvp.Value),
                    StringComparer.OrdinalIgnoreCase)
            };
            return Task.CompletedTask;
        }

        private static ScheduledJobDefinition CloneJob(ScheduledJobDefinition source) => new()
        {
            Id = source.Id,
            Name = source.Name,
            Enabled = source.Enabled,
            ScheduleKind = source.ScheduleKind,
            CronExpression = source.CronExpression,
            RunAtUtc = source.RunAtUtc,
            TimeZoneId = source.TimeZoneId,
            ExecutionTimeoutSeconds = source.ExecutionTimeoutSeconds,
            OverlapPolicy = source.OverlapPolicy,
            AgentId = source.AgentId,
            SessionKey = source.SessionKey,
            SessionTarget = source.SessionTarget,
            WakeMode = source.WakeMode,
            PayloadMessage = source.PayloadMessage,
            DeliveryChannel = source.DeliveryChannel,
            DeliveryRecipient = source.DeliveryRecipient,
            DeliveryMode = source.DeliveryMode,
            Scope = source.Scope,
            OwnerUserId = source.OwnerUserId,
            OwnerChannelId = source.OwnerChannelId,
            OwnerSessionId = source.OwnerSessionId,
            CreatedAtUtc = source.CreatedAtUtc,
            UpdatedAtUtc = source.UpdatedAtUtc
        };

        private static ScheduledJobState CloneState(ScheduledJobState source) => new()
        {
            NextRunAtUtc = source.NextRunAtUtc,
            LastRunAtUtc = source.LastRunAtUtc,
            LastStatus = source.LastStatus,
            LastDurationMs = source.LastDurationMs,
            LastDeliveryStatus = source.LastDeliveryStatus,
            LastError = source.LastError,
            LastErrorReason = source.LastErrorReason,
            ConsecutiveErrors = source.ConsecutiveErrors,
            ConsecutiveSkips = source.ConsecutiveSkips
        };
    }

    private sealed class FakeExecutor : IProactiveJobExecutor
    {
        public Task<ScheduledJobExecutionResult> ExecuteAsync(ScheduledJobDefinition job, CancellationToken ct) =>
            Task.FromResult(ScheduledJobExecutionResult.Successful("delivered", "ref-123"));
    }

    private sealed class BlockingExecutor : IProactiveJobExecutor
    {
        public int ExecuteCount { get; private set; }
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ScheduledJobExecutionResult> ExecuteAsync(ScheduledJobDefinition job, CancellationToken ct)
        {
            ExecuteCount++;
            Started.TrySetResult(true);
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(2), ct);
            return ScheduledJobExecutionResult.Successful("delivered", "ref-123");
        }
    }
}
