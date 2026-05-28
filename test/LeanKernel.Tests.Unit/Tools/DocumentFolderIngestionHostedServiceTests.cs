using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Tools;

public sealed class DocumentFolderIngestionHostedServiceTests
{
    [Fact]
    public async Task StartAsync_when_disabled_does_not_create_watch_folder()
    {
        var tempRoot = CreateTempRoot();
        var watchFolder = Path.Combine(tempRoot, "documents");
        try
        {
            var service = CreateService(watchFolder, new DocumentIngestionConfig
            {
                Enabled = true,
                WatchFolderEnabled = false
            });

            await service.StartAsync(CancellationToken.None);
            await service.StopAsync(CancellationToken.None);

            Directory.Exists(watchFolder).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task StartAsync_when_startup_scan_disabled_marks_existing_files_without_queueing()
    {
        var tempRoot = CreateTempRoot();
        var watchFolder = Path.Combine(tempRoot, "documents");
        Directory.CreateDirectory(watchFolder);
        await File.WriteAllTextAsync(Path.Combine(watchFolder, "existing.txt"), "existing");

        try
        {
            var queue = new DocumentIngestionQueue();
            var service = CreateService(watchFolder, CreateEnabledConfig(startupScanEnabled: false, pollingIntervalSeconds: 0), queue);

            await service.StartAsync(CancellationToken.None);
            await Task.Delay(50);
            await service.StopAsync(CancellationToken.None);

            queue.PendingCount.Should().Be(0);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task StartAsync_when_startup_scan_enabled_queues_existing_files()
    {
        var tempRoot = CreateTempRoot();
        var watchFolder = Path.Combine(tempRoot, "documents");
        Directory.CreateDirectory(watchFolder);
        var sourcePath = Path.Combine(watchFolder, "existing.txt");
        await File.WriteAllTextAsync(sourcePath, "existing");

        try
        {
            var queue = new DocumentIngestionQueue();
            var service = CreateService(watchFolder, CreateEnabledConfig(startupScanEnabled: true, pollingIntervalSeconds: 0), queue);

            await service.StartAsync(CancellationToken.None);
            await WaitForPendingCountAsync(queue, expectedCount: 1);
            await service.StopAsync(CancellationToken.None);

            queue.PendingCount.Should().Be(1);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Polling_queues_files_created_after_start()
    {
        var tempRoot = CreateTempRoot();
        var watchFolder = Path.Combine(tempRoot, "documents");
        try
        {
            var queue = new DocumentIngestionQueue();
            var service = CreateService(watchFolder, CreateEnabledConfig(startupScanEnabled: false, pollingIntervalSeconds: 1), queue);

            await service.StartAsync(CancellationToken.None);
            var sourcePath = Path.Combine(watchFolder, "new.txt");
            await File.WriteAllTextAsync(sourcePath, "new");

            await WaitForPendingCountAsync(queue, expectedCount: 1, timeout: TimeSpan.FromSeconds(4));
            await service.StopAsync(CancellationToken.None);

            queue.PendingCount.Should().Be(1);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Polling_queues_nested_files_when_recursive_watch_is_enabled()
    {
        var tempRoot = CreateTempRoot();
        var watchFolder = Path.Combine(tempRoot, "documents");
        try
        {
            var queue = new DocumentIngestionQueue();
            var service = CreateService(watchFolder, CreateEnabledConfig(startupScanEnabled: false, pollingIntervalSeconds: 1), queue);

            await service.StartAsync(CancellationToken.None);
            var nestedFolder = Path.Combine(watchFolder, "projects", "alpha");
            Directory.CreateDirectory(nestedFolder);
            await File.WriteAllTextAsync(Path.Combine(nestedFolder, "nested.txt"), "nested");

            await WaitForPendingCountAsync(queue, expectedCount: 1, timeout: TimeSpan.FromSeconds(4));
            await service.StopAsync(CancellationToken.None);

            queue.PendingCount.Should().Be(1);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static DocumentIngestionConfig CreateEnabledConfig(bool startupScanEnabled, int pollingIntervalSeconds)
        => new()
        {
            Enabled = true,
            WatchFolderEnabled = true,
            WatchStartupScanEnabled = startupScanEnabled,
            WatchPollingIntervalSeconds = pollingIntervalSeconds,
            WatchSettleDelaySeconds = 0,
            WatchIncludeSubdirectories = true,
            WatchDefaultTags = ["auto-import"]
        };

    private static DocumentFolderIngestionHostedService CreateService(
        string watchFolder,
        DocumentIngestionConfig ingestionConfig,
        DocumentIngestionQueue? queue = null)
    {
        ingestionConfig.WatchFolderPath = watchFolder;

        var config = new LeanKernelConfig
        {
            DocumentIngestion = ingestionConfig
        };

        return new DocumentFolderIngestionHostedService(
            queue ?? new DocumentIngestionQueue(),
            Options.Create(config),
            NullLogger<DocumentFolderIngestionHostedService>.Instance);
    }

    private static async Task WaitForPendingCountAsync(
        DocumentIngestionQueue queue,
        int expectedCount,
        TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(2));
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (queue.PendingCount == expectedCount)
            {
                return;
            }

            await Task.Delay(25);
        }

        queue.PendingCount.Should().Be(expectedCount);
    }

    private static string CreateTempRoot()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"lk-doc-folder-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        return tempRoot;
    }
}
