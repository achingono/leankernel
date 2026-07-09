using System.Collections.Concurrent;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Tools.Ingestion;

public sealed record BackfillOptions(
    string SourceDirectory,
    string Filter = "*.*",
    bool Recursive = true,
    List<string>? Tags = null,
    int MaxConcurrency = 2,
    string? CheckpointPath = null,
    bool DryRun = false);

public sealed class DocumentBackfillService
{
    private readonly DocumentLibraryService _libraryService;
    private readonly IDocumentFingerprintService _fingerprintService;
    private readonly ILogger<DocumentBackfillService> _logger;

    public DocumentBackfillService(
        DocumentLibraryService libraryService,
        IDocumentFingerprintService fingerprintService,
        ILogger<DocumentBackfillService> logger)
    {
        _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
        _fingerprintService = fingerprintService ?? throw new ArgumentNullException(nameof(fingerprintService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<int> RunBackfillAsync(
        string sourceDirectory,
        string filter = "*.*",
        bool recursive = true,
        List<string>? tags = null,
        int maxConcurrency = 2,
        string? checkpointPath = null,
        bool dryRun = false,
        CancellationToken ct = default)
        => await RunBackfillAsync(new BackfillOptions(sourceDirectory, filter, recursive, tags, maxConcurrency, checkpointPath, dryRun), ct).ConfigureAwait(false);

    public async Task<int> RunBackfillAsync(BackfillOptions options, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.SourceDirectory);

        var resolvedDir = Path.GetFullPath(options.SourceDirectory);
        if (!Directory.Exists(resolvedDir))
        {
            throw new DirectoryNotFoundException($"Backfill source directory not found: {resolvedDir}");
        }

        var effectiveTags = options.Tags ?? [];
        var lastCheckpoint = await ReadCheckpointAsync(options.CheckpointPath, ct).ConfigureAwait(false);

        var files = Directory.EnumerateFiles(
            resolvedDir,
            NormalizeFilter(options.Filter),
            new EnumerationOptions
            {
                RecurseSubdirectories = options.Recursive,
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.PlatformDefault
            }).OrderBy(f => f).ToList();

        _logger.LogInformation(
            "Backfill: discovered {FileCount} files in {SourceDir} with filter {Filter}",
            files.Count, resolvedDir, options.Filter);

        var (completed, skipped, failed) = await ProcessFilesWithCheckpointAsync(
            files, effectiveTags, options.MaxConcurrency, options.CheckpointPath, options.DryRun, lastCheckpoint, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Backfill complete: {Completed} imported, {Skipped} skipped (dedupe), {Failed} failed in {SourceDir}",
            completed, skipped, failed, resolvedDir);

        await TryClearCheckpointAsync(options.CheckpointPath, failed, ct);

        return completed;
    }

    private sealed class MutableInt
    {
        public int Value;
        public MutableInt(int value) { Value = value; }
    }

    private async Task<(int Completed, int Skipped, int Failed)> ProcessFilesWithCheckpointAsync(
        List<string> files,
        List<string> effectiveTags,
        int maxConcurrency,
        string? checkpointPath,
        bool dryRun,
        string? lastCheckpoint,
        CancellationToken ct)
    {
        var concurrency = Math.Max(1, maxConcurrency);
        var semaphore = new SemaphoreSlim(concurrency);
        var completedFlags = new ConcurrentDictionary<int, byte>();
        var nextCheckpointIndex = new MutableInt(lastCheckpoint is not null
            ? files.FindIndex(f => string.Equals(f, lastCheckpoint, StringComparison.Ordinal)) + 1
            : 0);
        var inFlight = new List<Task>();
        var completed = 0;
        var skipped = 0;
        var failed = 0;

        try
        {
            for (var fileIndex = nextCheckpointIndex.Value; fileIndex < files.Count; fileIndex++)
            {
                var file = files[fileIndex];
                if (ct.IsCancellationRequested) break;

                var result = await ProcessSingleFileAsync(
                    file, dryRun, effectiveTags, semaphore, ct).ConfigureAwait(false);

                switch (result.Action)
                {
                    case FileAction.SkippedDuplicate:
                        skipped++;
                        continue;
                    case FileAction.DryRunCompleted:
                        completed++;
                        continue;
                    case FileAction.Scheduled:
                        var task = result.Task!;
                        var index = fileIndex;
                        _ = task.ContinueWith(t =>
                            HandleFileCompletion(t, index, completedFlags, nextCheckpointIndex,
                                checkpointPath, files, ct),
                            ct, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                        inFlight.Add(task);
                        inFlight.RemoveAll(t => t.IsCompleted);
                        break;
                }
            }

            if (inFlight.Count > 0)
            {
                await Task.WhenAll(inFlight).ConfigureAwait(false);
            }

            TryAdvanceCheckpoint(checkpointPath, completedFlags, nextCheckpointIndex, files, ct);
        }
        finally
        {
            semaphore.Dispose();
        }

        return (completed, skipped, failed);
    }

    private enum FileAction { SkippedDuplicate, DryRunCompleted, Scheduled }

    private sealed class FileActionResult
    {
        public FileAction Action { get; }
        public Task? Task { get; }

        private FileActionResult(FileAction action, Task? task = null)
        {
            Action = action;
            Task = task;
        }

        public static readonly FileActionResult SkippedDuplicate = new(FileAction.SkippedDuplicate);
        public static readonly FileActionResult DryRunCompleted = new(FileAction.DryRunCompleted);
        public static FileActionResult Scheduled(Task task) => new(FileAction.Scheduled, task);
    }

    private async Task<FileActionResult> ProcessSingleFileAsync(
        string file,
        bool dryRun,
        List<string> effectiveTags,
        SemaphoreSlim semaphore,
        CancellationToken ct)
    {
        var fingerprint = _fingerprintService.ComputeFingerprint(file);
        var isDuplicate = false;
        try
        {
            isDuplicate = await _fingerprintService.IsKnownFingerprintAsync(fingerprint, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fingerprint check failed for {Path}, proceeding", file);
        }

        if (isDuplicate)
        {
            _logger.LogDebug("Backfill: skipped unchanged file {Path}", file);
            return FileActionResult.SkippedDuplicate;
        }

        if (dryRun)
        {
            _logger.LogInformation("Backfill (dry-run): would import {Path}", file);
            return FileActionResult.DryRunCompleted;
        }

        await semaphore.WaitAsync(ct).ConfigureAwait(false);

        var task = IngestFileAsync(file, fingerprint, effectiveTags, semaphore, ct);

        return FileActionResult.Scheduled(task);
    }

    private static void HandleFileCompletion(
        Task task,
        int index,
        ConcurrentDictionary<int, byte> completedFlags,
        MutableInt nextCheckpointIndex,
        string? checkpointPath,
        IReadOnlyList<string> files,
        CancellationToken ct)
    {
        if (task.IsCompletedSuccessfully)
        {
            completedFlags.TryAdd(index, 0);
            TryAdvanceCheckpoint(checkpointPath, completedFlags, nextCheckpointIndex, files, ct);
        }
    }

    private static void TryAdvanceCheckpoint(
        string? checkpointPath,
        ConcurrentDictionary<int, byte> completedFlags,
        MutableInt nextCheckpointIndex,
        IReadOnlyList<string> files,
        CancellationToken ct)
    {
        if (checkpointPath is null) return;

        while (completedFlags.TryRemove(nextCheckpointIndex.Value, out _))
        {
            nextCheckpointIndex.Value++;
        }

        if (nextCheckpointIndex.Value > 0)
        {
            WriteCheckpointAsync(checkpointPath, files[nextCheckpointIndex.Value - 1], ct)
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }
    }

    private static async Task TryClearCheckpointAsync(string? checkpointPath, int failed, CancellationToken ct)
    {
        if (checkpointPath is null || failed != 0 || ct.IsCancellationRequested)
        {
            return;
        }

        try
        {
            if (File.Exists(checkpointPath))
            {
                File.Delete(checkpointPath);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to clear checkpoint: {ex.Message}");
        }
    }

    private async Task IngestFileAsync(
        string filePath,
        string fingerprint,
        List<string> tags,
        SemaphoreSlim semaphore,
        CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Backfill: ingesting {Path}", filePath);

            var result = await _libraryService.IngestExistingDocumentAsync(
                filePath,
                title: null,
                tags,
                ct).ConfigureAwait(false);

            // Record fingerprint for future dedupe
            try
            {
                var fileInfo = new FileInfo(filePath);
                await _fingerprintService.RecordFingerprintAsync(fingerprint, filePath, fileInfo.Length, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to record fingerprint for {Path}", filePath);
            }

            _logger.LogInformation("Backfill: completed {Path} → {PageSlug}", filePath, result.PageSlug);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("Backfill: cancelled for {Path}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backfill: failed for {Path}", filePath);
            throw; // Let the caller handle failure counting
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static async Task<string?> ReadCheckpointAsync(string? checkpointPath, CancellationToken ct)
    {
        if (checkpointPath is null || !File.Exists(checkpointPath))
        {
            return null;
        }

        try
        {
            var content = await File.ReadAllTextAsync(checkpointPath, ct).ConfigureAwait(false);
            return content?.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static async Task WriteCheckpointAsync(string checkpointPath, string filePath, CancellationToken ct)
    {
        try
        {
            var dir = Path.GetDirectoryName(checkpointPath);
            if (dir is not null)
            {
                Directory.CreateDirectory(dir);
            }
            await File.WriteAllTextAsync(checkpointPath, filePath, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Checkpoint write failures are non-fatal
            System.Diagnostics.Debug.WriteLine($"Failed to write checkpoint: {ex.Message}");
        }
    }

    private static string NormalizeFilter(string? filter)
        => string.IsNullOrWhiteSpace(filter) ? "*.*" : filter;
}