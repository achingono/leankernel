using System.Collections.Concurrent;
using LeanKernel.Abstractions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tools;

/// <summary>
/// Monitors a configured document folder and queues dropped files for ingestion.
/// </summary>
public sealed class DocumentFolderIngestionHostedService : IHostedService
{
    private readonly DocumentIngestionQueue _queue;
    private readonly DocumentIngestionConfig _config;
    private readonly ILogger<DocumentFolderIngestionHostedService> _logger;
    private readonly ConcurrentDictionary<string, byte> _knownPaths = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _scheduledPaths = new(StringComparer.Ordinal);
    private CancellationTokenSource? _shutdownCts;
    private FileSystemWatcher? _watcher;
    private Task? _pollingTask;
    private string _watchFolderPath = string.Empty;

    public DocumentFolderIngestionHostedService(
        DocumentIngestionQueue queue,
        IOptions<LeanKernelConfig> config,
        ILogger<DocumentFolderIngestionHostedService> logger)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _config = (config ?? throw new ArgumentNullException(nameof(config))).Value.DocumentIngestion ?? new();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_config.Enabled || !_config.WatchFolderEnabled)
        {
            _logger.LogInformation("Document folder ingestion monitor is disabled");
            return Task.CompletedTask;
        }

        _watchFolderPath = Path.GetFullPath(_config.WatchFolderPath);
        Directory.CreateDirectory(_watchFolderPath);

        _shutdownCts = new CancellationTokenSource();

        if (_config.WatchStartupScanEnabled)
        {
            ScanExistingFiles(queueFiles: true);
        }
        else
        {
            ScanExistingFiles(queueFiles: false);
        }

        _watcher = new FileSystemWatcher(_watchFolderPath, NormalizeFilter(_config.WatchFilter))
        {
            IncludeSubdirectories = _config.WatchIncludeSubdirectories,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        _watcher.Created += OnFileArrived;
        _watcher.Renamed += OnFileArrived;
        _watcher.Error += OnWatcherError;
        _watcher.EnableRaisingEvents = true;

        if (_config.WatchPollingIntervalSeconds > 0)
        {
            _pollingTask = PollForNewFilesAsync(_shutdownCts.Token);
        }

        _logger.LogInformation(
            "Document folder ingestion monitor started for {WatchFolder} with polling interval {PollingIntervalSeconds}s",
            _watchFolderPath,
            _config.WatchPollingIntervalSeconds);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_shutdownCts is null)
        {
            return;
        }

        _logger.LogInformation("Stopping document folder ingestion monitor");
        _shutdownCts.Cancel();

        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnFileArrived;
            _watcher.Renamed -= OnFileArrived;
            _watcher.Error -= OnWatcherError;
            _watcher.Dispose();
            _watcher = null;
        }

        if (_pollingTask is not null)
        {
            try
            {
                await _pollingTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _shutdownCts.IsCancellationRequested)
            {
                _logger.LogDebug("Document folder ingestion polling stopped");
            }
        }

        _shutdownCts.Dispose();
        _shutdownCts = null;
    }

    private void OnFileArrived(object sender, FileSystemEventArgs e)
        => SchedulePath(e.FullPath);

    private void OnWatcherError(object sender, ErrorEventArgs e)
        => _logger.LogError(e.GetException(), "Document folder watcher failed for {WatchFolder}", _watchFolderPath);

    private async Task PollForNewFilesAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, _config.WatchPollingIntervalSeconds)));

        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                ScanExistingFiles(queueFiles: true);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("Document folder ingestion polling cancelled");
        }
    }

    private void ScanExistingFiles(bool queueFiles)
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(_watchFolderPath, NormalizeFilter(_config.WatchFilter), CreateEnumerationOptions()))
            {
                var normalizedPath = Path.GetFullPath(path);
                if (queueFiles)
                {
                    SchedulePath(normalizedPath);
                }
                else
                {
                    _knownPaths.TryAdd(normalizedPath, 0);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            _logger.LogError(ex, "Failed to scan document watch folder {WatchFolder}", _watchFolderPath);
        }
    }

    private void SchedulePath(string path)
    {
        if (_shutdownCts is null || _shutdownCts.IsCancellationRequested)
        {
            return;
        }

        var normalizedPath = Path.GetFullPath(path);
        if (_knownPaths.ContainsKey(normalizedPath) || Directory.Exists(normalizedPath))
        {
            return;
        }

        if (!_scheduledPaths.TryAdd(normalizedPath, 0))
        {
            return;
        }

        _ = QueueWhenStableAsync(normalizedPath, _shutdownCts.Token);
    }

    private async Task QueueWhenStableAsync(string path, CancellationToken ct)
    {
        try
        {
            if (!await WaitForStableFileAsync(path, ct).ConfigureAwait(false))
            {
                _logger.LogWarning("Document import file did not become stable before queueing: {Path}", path);
                return;
            }

            _queue.QueuePath(path, title: null, [.. _config.WatchDefaultTags]);
            _knownPaths.TryAdd(path, 0);
            _logger.LogInformation("Queued document import from watched folder: {Path}", path);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("Document import queueing cancelled for {Path}", path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to queue document import from watched folder: {Path}", path);
        }
        finally
        {
            _scheduledPaths.TryRemove(path, out _);
        }
    }

    private async Task<bool> WaitForStableFileAsync(string path, CancellationToken ct)
    {
        var settleDelay = TimeSpan.FromSeconds(Math.Clamp(_config.WatchSettleDelaySeconds, 0, 300));

        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            var first = ReadFileState(path);
            await Task.Delay(settleDelay, ct).ConfigureAwait(false);

            if (!File.Exists(path))
            {
                return false;
            }

            var second = ReadFileState(path);
            if (first.Length == second.Length
                && first.LastWriteUtc == second.LastWriteUtc
                && CanOpenForRead(path))
            {
                return true;
            }
        }

        return false;
    }

    private static (long Length, DateTime LastWriteUtc) ReadFileState(string path)
    {
        var fileInfo = new FileInfo(path);
        return (fileInfo.Length, fileInfo.LastWriteTimeUtc);
    }

    private static bool CanOpenForRead(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return stream.CanRead;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private EnumerationOptions CreateEnumerationOptions()
        => new()
        {
            RecurseSubdirectories = _config.WatchIncludeSubdirectories,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.PlatformDefault
        };

    private static string NormalizeFilter(string? filter)
        => string.IsNullOrWhiteSpace(filter) ? "*.*" : filter;
}
