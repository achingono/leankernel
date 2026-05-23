using Microsoft.Extensions.Logging.Abstractions;
using LeanKernel.Core.Models;
using LeanKernel.Scheduler;

namespace LeanKernel.Tests.Unit.Scheduler;

public class FileScheduledJobStoreTests
{
    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsJobsAndState()
    {
        var root = CreateTempDirectory();
        try
        {
            var store = new FileScheduledJobStore(root, NullLogger<FileScheduledJobStore>.Instance);
            var snapshot = new ScheduledJobStoreSnapshot
            {
                Jobs =
                [
                    new ScheduledJobDefinition
                    {
                        Id = "job-a",
                        Name = "Morning",
                        PayloadMessage = "Run morning brief",
                        DeliveryChannel = "signal",
                        DeliveryRecipient = "user-a",
                        OwnerUserId = "user-a",
                        OwnerChannelId = "signal",
                        CronExpression = "0 7 * * *"
                    }
                ],
                States = new Dictionary<string, ScheduledJobState>(StringComparer.OrdinalIgnoreCase)
                {
                    ["job-a"] = new ScheduledJobState
                    {
                        LastStatus = "ok",
                        ConsecutiveErrors = 0
                    }
                }
            };

            await store.SaveAsync(snapshot, CancellationToken.None);
            var loaded = await store.LoadAsync(CancellationToken.None);

            Assert.Single(loaded.Jobs);
            Assert.Equal("job-a", loaded.Jobs[0].Id);
            Assert.True(loaded.States.ContainsKey("job-a"));
            Assert.Equal("ok", loaded.States["job-a"].LastStatus);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_InvalidJson_DoesNotThrow()
    {
        var root = CreateTempDirectory();
        try
        {
            var schedulerDir = Path.Combine(root, "scheduler");
            Directory.CreateDirectory(schedulerDir);
            await File.WriteAllTextAsync(Path.Combine(schedulerDir, "jobs.json"), "{not-json");
            await File.WriteAllTextAsync(Path.Combine(schedulerDir, "jobs-state.json"), "{also-not-json");

            var store = new FileScheduledJobStore(root, NullLogger<FileScheduledJobStore>.Instance);
            var loaded = await store.LoadAsync(CancellationToken.None);

            Assert.Empty(loaded.Jobs);
            Assert.Empty(loaded.States);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"leankernel-scheduler-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
