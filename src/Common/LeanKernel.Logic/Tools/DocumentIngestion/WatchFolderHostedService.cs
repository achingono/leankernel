namespace LeanKernel.Logic.Tools.DocumentIngestion;

using System.IO;

using LeanKernel.Logic.Configuration;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Background service that monitors configured watch folders and enqueues new files for ingestion.
/// </summary>
public sealed class WatchFolderHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<FileSettings> _fileSettings;
    private readonly IOptions<DocumentIngestionToolSettings> _diSettings;
    private readonly ILogger<WatchFolderHostedService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchFolderHostedService"/> class.
    /// </summary>
    /// <param name="scopeFactory">The service scope factory.</param>
    /// <param name="fileSettings">The file settings providing watch folder configuration.</param>
    /// <param name="diSettings">The document ingestion tool settings.</param>
    /// <param name="logger">The logger instance.</param>
    public WatchFolderHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<FileSettings> fileSettings,
        IOptions<DocumentIngestionToolSettings> diSettings,
        ILogger<WatchFolderHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _fileSettings = fileSettings;
        _diSettings = diSettings;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var watchFolders = _fileSettings.Value.WatchFolders;

        if (watchFolders.Count == 0)
        {
            _logger.LogInformation("No watch folders configured; WatchFolderHostedService will idle.");
        }
        else
        {
            _logger.LogInformation("WatchFolder hosted service started with {Count} folder(s).", watchFolders.Count);
        }

        foreach (var config in watchFolders)
        {
            if (!Directory.Exists(config.Path))
            {
                _logger.LogWarning("Watch folder does not exist: {Path}", config.Path);
                continue;
            }

            _ = MonitorFolderAsync(config, stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }

    private async Task MonitorFolderAsync(WatchFolderConfiguration config, CancellationToken ct)
    {
        var watcher = new FileSystemWatcher(config.Path, config.FilePattern)
        {
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };

        var settleDelaySeconds = _diSettings.Value.WatchSettleDelaySeconds > 0
            ? _diSettings.Value.WatchSettleDelaySeconds
            : 2;
        var maxStabilityRetries = _diSettings.Value.WatchMaxRetries > 0
            ? _diSettings.Value.WatchMaxRetries
            : 3;
        var settleDelay = TimeSpan.FromSeconds(settleDelaySeconds);
        var tcs = new TaskCompletionSource<string?>();

        watcher.Created += (_, e) =>
        {
            tcs.TrySetResult(e.FullPath);
        };

        while (!ct.IsCancellationRequested)
        {
            tcs = new TaskCompletionSource<string?>();
            var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(-1, ct));

            if (ct.IsCancellationRequested || completedTask != tcs.Task)
            {
                continue;
            }

            var path = tcs.Task.Result;

            if (path is null)
            {
                continue;
            }

            try
            {
                await WaitForStabilityAsync(path, settleDelay, maxStabilityRetries, ct);

                using var scope = _scopeFactory.CreateScope();
                var queue = scope.ServiceProvider.GetRequiredService<IDocumentIngestionQueue>();

                var job = new DocumentIngestionJob(
                    path,
                    Path.GetFileName(path),
                    DetectContentType(path),
                    config.TenantId,
                    config.UserId,
                    config.PersonId,
                    config.ChannelId,
                    config.AvailabilityScope,
                    DocumentIngestionSource.WatchedFile);

                await queue.EnqueueAsync(job, ct);
                _logger.LogDebug("Enqueued watched file: {Path}", path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process watched file: {Path}", path);
            }
        }

        watcher.Dispose();
    }

    private static async Task WaitForStabilityAsync(string path, TimeSpan delay, int maxRetries, CancellationToken ct)
    {
        await Task.Delay(delay, ct);

        long previousSize = -1;
        for (var i = 0; i < maxRetries; i++)
        {
            try
            {
                var fileInfo = new FileInfo(path);
                if (!fileInfo.Exists)
                {
                    return;
                }

                if (fileInfo.Length == previousSize)
                {
                    return;
                }

                previousSize = fileInfo.Length;
                await Task.Delay(delay, ct);
            }
            catch
            {
                return;
            }
        }
    }

    private static string DetectContentType(string path)
    {
        var ext = Path.GetExtension(path)?.ToLowerInvariant();
        return ext switch
        {
            ".txt" => "text/plain",
            ".md" => "text/markdown",
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".csv" => "text/csv",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".html" => "text/html",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream",
        };
    }
}
