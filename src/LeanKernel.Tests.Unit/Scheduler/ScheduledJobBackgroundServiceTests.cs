using Microsoft.Extensions.Logging.Abstractions;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Scheduler;

namespace LeanKernel.Tests.Unit.Scheduler;

public class ScheduledJobBackgroundServiceTests
{
    [Fact]
    public async Task StartAsync_InitializesAndProcessesDueJobs()
    {
        var manager = new FakeManager();
        var service = new ScheduledJobBackgroundService(
            manager,
            NullLogger<ScheduledJobBackgroundService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await manager.ProcessCalled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.StopAsync(CancellationToken.None);

        Assert.True(manager.InitializeCount >= 1);
        Assert.True(manager.ProcessCount >= 1);
    }

    private sealed class FakeManager : IScheduledJobManager
    {
        public int InitializeCount { get; private set; }
        public int ProcessCount { get; private set; }
        public TaskCompletionSource<bool> ProcessCalled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task InitializeAsync(CancellationToken ct)
        {
            InitializeCount++;
            return Task.CompletedTask;
        }

        public Task ProcessDueJobsAsync(CancellationToken ct)
        {
            ProcessCount++;
            ProcessCalled.TrySetResult(true);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ScheduledJobView>> ListAsync(ScheduledJobListOptions options, ScheduledJobActor actor, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ScheduledJobView>>([]);
        public Task<ScheduledJobView?> GetAsync(string jobId, ScheduledJobActor actor, CancellationToken ct) =>
            Task.FromResult<ScheduledJobView?>(null);
        public Task<ScheduledJobView> CreateAsync(ScheduledJobCreateRequest request, ScheduledJobActor actor, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task<ScheduledJobView> UpdateAsync(string jobId, ScheduledJobUpdateRequest request, ScheduledJobActor actor, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task DeleteAsync(string jobId, ScheduledJobActor actor, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task<ScheduledJobView> SetEnabledAsync(string jobId, bool enabled, ScheduledJobActor actor, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task<ScheduledJobView> TriggerAsync(string jobId, ScheduledJobActor actor, CancellationToken ct) =>
            throw new NotImplementedException();
    }
}
