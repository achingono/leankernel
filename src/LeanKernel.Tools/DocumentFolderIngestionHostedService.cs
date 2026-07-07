using System.Collections.Concurrent;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tools;

public sealed class DocumentFolderIngestionHostedService : IHostedService
{
    private readonly IDocumentIngestionQueue _queue;
    private readonly IDocumentFingerprintService _fingerprintService;
    private readonly DocumentIngestionConfig _config;
    private readonly ILogger<DocumentFolderIngestionHostedService> _logger;

    // Per-path lifecycle state (Pending, Queued, Completed, Failed, RetryDue)
    private readonly ConcurrentDictionary<string, WatchedPathState> _paths = new(StringComparer.Ordinal);

    // Gates per-path execution so only one inflight task runs per path at a time.
    // Keyed by normalized path; value is the running QueuePathCoreAsync task.
    // StopAsync drains this dictionary before completing shutdown.
    private readonly ConcurrentDictionary<string, Task> _inflight = new(StringComparer.Ordinal);

    private CancellationTokenSource? _shutdownCts;
    private FileSystemWatcher? _watcher;
    private Task? _processingTask;
    private string _watchFolderPath = string.Empty;

    public DocumentFolderIngestionHostedService(
        IDocumentIngestionQueue queue,
        IDocumentFingerprintService fingerprintService,
        IOptions<LeanKernelConfig> config,
        ILogger<DocumentFolderIngestionHostedService> logger)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _fingerprintService = fingerprintService ?? throw new ArgumentNullException(nameof(fingerprintService));
        _config = (config ?? throw new ArgumentNullException(nameof(config))).Value.DocumentIngestion ?? new();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

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

        // Startup scan: seed known paths. When enabled, marks files as Pending
        // so the background processing loop queues them with backpressure.
        // When disabled, marks them Completed to skip on restart.
        ScanExistingFiles(queueFiles: _config.WatchStartupScanEnabled);

        _watcher = new FileSystemWatcher(_watchFolderPath, NormalizeFilter(_config.WatchFilter))
        {
            IncludeSubdirectories = _config.WatchIncludeSubdirectories,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        _watcher.Created += OnFileArrived;
        _watcher.Renamed += OnFileArrived;
        _watcher.Error += OnWatcherError;
        _watcher.EnableRaisingEvents = true;

        // Background processing loop for pending paths and retries
        _processingTask = ProcessPendingPathsAsync(_shutdownCts.Token);

        _logger.LogInformation(
            "Document folder ingestion monitor started for {WatchFolder}",
            _watchFolderPath);

        return Task.CompletedTask;
    }

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

        if (_processingTask is not null)
        {
            try
            {
                await _processingTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _shutdownCts.IsCancellationRequested)
            {
                _logger.LogDebug("Document folder ingestion processing stopped");
            }
        }

        // Drain in-flight path processing tasks (_inflight is populated by TryQueuePathAsync).
        // Each task removes itself from _inflight in its finally block.
        // We snapshot the values because _inflight is mutated concurrently.
        if (_inflight.Count > 0)
        {
            _logger.LogDebug("Waiting for {Count} in-flight path processing tasks to complete", _inflight.Count);
            var snapshot = _inflight.Values.ToArray();
            try
            {
                await Task.WhenAll(snapshot).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
            {
                _logger.LogWarning("Timed out waiting for {Count} in-flight path processing tasks to complete", _inflight.Count);
            }
        }

        _shutdownCts.Dispose();
        _shutdownCts = null;
    }

    private void OnFileArrived(object sender, FileSystemEventArgs e)
        => SchedulePath(e.FullPath, isNewArrival: true);

    private void OnWatcherError(object sender, ErrorEventArgs e)
        => _logger.LogError(e.GetException(), "Document folder watcher failed for {WatchFolder}", _watchFolderPath);

    /// <summary>
    /// Background loop that processes pending and retry-due paths.
    /// </summary>
    private async Task ProcessPendingPathsAsync(CancellationToken ct)
    {
        // Use a periodic timer at a reasonable interval for retry processing.
        var interval = _config.WatchPollingIntervalSeconds > 0
            ? TimeSpan.FromSeconds(Math.Max(1, _config.WatchPollingIntervalSeconds))
            : TimeSpan.FromSeconds(10); // default retry check interval

        using var timer = new PeriodicTimer(interval);

        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                var now = DateTimeOffset.UtcNow;
                var duePaths = _paths.Values
                    .Where(s => s.State == WatchedPathState.StateValue.Pending ||
                                (s.State == WatchedPathState.StateValue.RetryDue && s.NextRetryAt <= now))
                    .Select(s => s.Path)
                    .ToList();

                foreach (var path in duePaths)
                {
                    if (ct.IsCancellationRequested) break;
                    _ = TryQueuePathAsync(path, ct);
                }

                // Also scan for new files if polling is enabled
                if (_config.WatchPollingIntervalSeconds > 0)
                {
                    ScanExistingFiles(queueFiles: true);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("Document folder ingestion processing cancelled");
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
                    SchedulePath(normalizedPath, isNewArrival: false);
                }
                else
                {
                    // Seed as completed so they are not re-queued on startup
                    _paths.TryAdd(normalizedPath, new WatchedPathState
                    {
                        Path = normalizedPath,
                        State = WatchedPathState.StateValue.Completed,
                        CreatedAt = DateTimeOffset.UtcNow
                    });
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            _logger.LogError(ex, "Failed to scan document watch folder {WatchFolder}", _watchFolderPath);
        }
    }

    private void SchedulePath(string path, bool isNewArrival)
    {
        if (_shutdownCts is null || _shutdownCts.IsCancellationRequested)
        {
            return;
        }

        var normalizedPath = Path.GetFullPath(path);
        if (Directory.Exists(normalizedPath))
        {
            return;
        }

        // Only add if not already tracked, or force re-process for new arrivals
        var state = _paths.GetOrAdd(normalizedPath, p => new WatchedPathState
        {
            Path = p,
            State = WatchedPathState.StateValue.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        });

        if (state.State == WatchedPathState.StateValue.Completed && isNewArrival)
        {
            // File re-appeared (watcher Created event for a new file after deletion)
            state.State = WatchedPathState.StateValue.Pending;
            state.RetryCount = 0;
            state.NextRetryAt = null;
            state.LastError = null;
            _logger.LogInformation("Rescheduling previously completed path: {Path}", normalizedPath);
        }

        // For new arrivals that are pending, kick off immediate processing
        if (state.State == WatchedPathState.StateValue.Pending)
        {
            _ = TryQueuePathAsync(normalizedPath, _shutdownCts.Token);
        }
    }

    /// <summary>
    /// Gates per-path execution so only one inflight task runs per path at a time.
    /// Uses GetOrAdd so concurrent callers for the same path share a single Task.
    /// The task removes itself from _inflight in its finally block.
    /// </summary>
    private Task TryQueuePathAsync(string path, CancellationToken ct)
    {
        return _inflight.GetOrAdd(path, p => QueuePathCoreAsync(p, ct));
    }

    /// <summary>
    /// Core processing pipeline for a single watched file path:
    /// 1. Wait for stable file size/mtime
    /// 2. Compute fingerprint and check for duplicates
    /// 3. Enqueue the path-based job
    /// 4. Record fingerprint on successful enqueue
    /// 5. Schedule retry with exponential backoff on transient failures
    /// </summary>
    private async Task QueuePathCoreAsync(string path, CancellationToken ct)
    {
        try
        {
            if (!_paths.TryGetValue(path, out var state))
            {
                return;
            }

            if (!await WaitForStableFileAsync(path, ct).ConfigureAwait(false))
            {
                _logger.LogWarning("Document import file did not become stable: {Path}", path);
                ScheduleRetry(state);
                return;
            }

            // Compute fingerprint after WaitForStableFileAsync so size/mtime reflect the final
            // on-disk state. The same fingerprint value is used for the dedupe check below and
            // the RecordFingerprintAsync call after a successful enqueue, ensuring consistency.
            var fingerprint = _fingerprintService.ComputeFingerprint(path);
            try
            {
                var isDuplicate = await _fingerprintService.IsKnownFingerprintAsync(fingerprint, ct).ConfigureAwait(false);
                if (isDuplicate)
                {
                    state.State = WatchedPathState.StateValue.Completed;
                    _logger.LogInformation("Skipped unchanged file (fingerprint match): {Path}", path);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fingerprint check failed for {Path}, proceeding with queue", path);
            }

            var result = await _queue.QueuePathAsync(path, title: null, [.. _config.WatchDefaultTags], ct).ConfigureAwait(false);

            switch (result.Outcome)
            {
                case EnqueueOutcome.Queued:
                    state.State = WatchedPathState.StateValue.Queued;
                    state.RetryCount = 0;
                    state.NextRetryAt = null;
                    state.LastError = null;
                    _logger.LogInformation("Queued document import from watched folder: {Path} [{JobId}]",
                        path, result.Job?.JobId);

                    try
                    {
                        var fileInfo = new FileInfo(path);
                        await _fingerprintService.RecordFingerprintAsync(fingerprint, path, fileInfo.Length, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to record fingerprint for {Path}", path);
                    }
                    break;

                case EnqueueOutcome.TimedOut:
                    _logger.LogWarning("Queue full, will retry later: {Path} (queue_pressure)", path);
                    ScheduleRetry(state);
                    break;

                case EnqueueOutcome.Cancelled:
                    _logger.LogDebug("Enqueue cancelled for: {Path}", path);
                    break;

                case EnqueueOutcome.Rejected:
                    _logger.LogError("Enqueue rejected for {Path}: {Reason}", path, result.Reason);
                    ScheduleRetry(state);
                    break;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("Queue processing cancelled for {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing file {Path}", path);
        }
        finally
        {
            _inflight.TryRemove(path, out _);
        }
    }

    /// <summary>
    /// Schedules a retry for a transiently failed path using exponential backoff with jitter.
    /// Transitions to Failed permanently once WatchMaxRetries is exceeded.
    /// </summary>
    private void ScheduleRetry(WatchedPathState state)
    {
        state.RetryCount++;
        state.State = WatchedPathState.StateValue.RetryDue;

        if (state.RetryCount >= _config.WatchMaxRetries)
        {
            state.State = WatchedPathState.StateValue.Failed;
            _logger.LogError("Document import failed after {RetryCount} retries: {Path}",
                state.RetryCount, state.Path);
            return;
        }

        // Exponential backoff with jitter: base * 2^(retry-1) + random 0..base, capped at max
        var baseDelay = Math.Max(1, _config.WatchRetryBaseDelaySeconds);
        var maxDelay = Math.Max(baseDelay, _config.WatchRetryMaxDelaySeconds);
        var delay = Math.Min(
            baseDelay * (int)Math.Pow(2, state.RetryCount - 1) + Random.Shared.Next(0, baseDelay),
            maxDelay);

        state.NextRetryAt = DateTimeOffset.UtcNow.AddSeconds(delay);
        _logger.LogInformation(
            "Scheduled retry {RetryCount}/{MaxRetries} for {Path} in {Delay}s",
            state.RetryCount, _config.WatchMaxRetries, state.Path, delay);
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

    /// <summary>
    /// Tracks the lifecycle state of a watched file path.
    /// </summary>
    internal sealed class WatchedPathState
    {
        internal enum StateValue { Pending, Queued, Completed, Failed, RetryDue }

        public required string Path { get; init; }
        public StateValue State { get; set; } = StateValue.Pending;
        public int RetryCount { get; set; }
        public DateTimeOffset? NextRetryAt { get; set; }
        public string? LastError { get; set; }
        public DateTimeOffset CreatedAt { get; init; }
    }
}