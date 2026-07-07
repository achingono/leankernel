using System.Collections.Concurrent;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tools.Ingestion;

/// <summary>
/// Provides controlled batch backfill of documents into the knowledge base.
/// Designed for one-time bulk imports of legacy corpora, separate from live watcher mode.
/// </summary>
public sealed class DocumentBackfillService
{
    private readonly DocumentLibraryService _libraryService;
    private readonly IDocumentFingerprintService _fingerprintService;
    private readonly IOptions<LeanKernelConfig> _config;
    private readonly ILogger<DocumentBackfillService> _logger;

    public DocumentBackfillService(
        DocumentLibraryService libraryService,
        IDocumentFingerprintService fingerprintService,
        IOptions<LeanKernelConfig> config,
        ILogger<DocumentBackfillService> logger)
    {
        _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
        _fingerprintService = fingerprintService ?? throw new ArgumentNullException(nameof(fingerprintService));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Runs a controlled backfill from the specified source directory.
    /// </summary>
    /// <param name="sourceDirectory">Directory to scan for documents.</param>
    /// <param name="filter">File glob pattern (e.g. "*.md", "*.*").</param>
    /// <param name="recursive">Whether to recurse into subdirectories.</param>
    /// <param name="tags">Tags to apply to imported documents.</param>
    /// <param name="maxConcurrency">Maximum concurrent ingestion tasks.</param>
    /// <param name="checkpointPath">Optional path to a checkpoint file for resumability.</param>
    /// <param name="dryRun">If true, only enumerate and log would-be imports without ingesting.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of documents successfully ingested.</returns>
    public async Task<int> RunBackfillAsync(
        string sourceDirectory,
        string filter = "*.*",
        bool recursive = true,
        List<string>? tags = null,
        int maxConcurrency = 2,
        string? checkpointPath = null,
        bool dryRun = false,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);

        var resolvedDir = Path.GetFullPath(sourceDirectory);
        if (!Directory.Exists(resolvedDir))
        {
            throw new DirectoryNotFoundException($"Backfill source directory not found: {resolvedDir}");
        }

        var effectiveTags = tags ?? [];
        var concurrency = Math.Max(1, maxConcurrency);
        var semaphore = new SemaphoreSlim(concurrency);
        var completed = 0;
        var skipped = 0;
        var failed = 0;
        var lastCheckpoint = await ReadCheckpointAsync(checkpointPath, ct).ConfigureAwait(false);
        var checkpointReached = lastCheckpoint is null;

        var files = Directory.EnumerateFiles(
            resolvedDir,
            NormalizeFilter(filter),
            new EnumerationOptions
            {
                RecurseSubdirectories = recursive,
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.PlatformDefault
            }).OrderBy(f => f).ToList();

        _logger.LogInformation(
            "Backfill: discovered {FileCount} files in {SourceDir} with filter {Filter}",
            files.Count, resolvedDir, filter);

        var inFlight = new List<Task>();

        try
        {
            // Shared state for sequential checkpoint advancement:
            //   completedFlags — set of file indices (in sorted order) that completed ingestion
            //   nextCheckpointIndex — the index of the next file to checkpoint; only advances
            //     through files that completed in sorted order (see TryAdvanceCheckpoint).
            //   checkpointLock — serializes checkpoint writes so only one thread writes at a time.
            var completedFlags = new ConcurrentDictionary<int, byte>();
            var nextCheckpointIndex = lastCheckpoint is not null
                ? files.FindIndex(f => string.Equals(f, lastCheckpoint, StringComparison.Ordinal)) + 1
                : 0;
            var checkpointLock = new object();

            for (var fileIndex = 0; fileIndex < files.Count; fileIndex++)
            {
                var file = files[fileIndex];

                if (ct.IsCancellationRequested) break;

                // Skip ahead to last checkpoint if resuming
                if (!checkpointReached)
                {
                    if (string.Equals(file, lastCheckpoint, StringComparison.Ordinal))
                    {
                        checkpointReached = true;
                    }
                    continue;
                }

                // Check fingerprint for dedupe
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
                    skipped++;
                    _logger.LogDebug("Backfill: skipped unchanged file {Path}", file);
                    continue;
                }

                if (dryRun)
                {
                    _logger.LogInformation("Backfill (dry-run): would import {Path}", file);
                    completed++;
                    continue;
                }

                await semaphore.WaitAsync(ct).ConfigureAwait(false);

                var index = fileIndex;

                var task = IngestFileAsync(file, fingerprint, effectiveTags, semaphore, ct);

                // On completion, mark this file's index in completedFlags and try to advance
                // the sequential checkpoint (see TryAdvanceCheckpoint).
                // Checkpoint is NOT written at scheduling time — only after the file
                // has been ingested and fingerprint recorded. This prevents silently
                // losing files on crash between scheduling and completion.
                _ = task.ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully)
                    {
                        Interlocked.Increment(ref completed);
                        completedFlags.TryAdd(index, 0);
                        TryAdvanceCheckpoint();
                    }
                    else
                    {
                        Interlocked.Increment(ref failed);
                    }
                }, ct, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

                inFlight.Add(task);

                // Clean completed tasks
                inFlight.RemoveAll(t => t.IsCompleted);
            }

            if (inFlight.Count > 0)
            {
                await Task.WhenAll(inFlight).ConfigureAwait(false);
            }

            // Advance checkpoint through any remaining sequentially completed files
            TryAdvanceCheckpoint();

            /// <summary>
            /// Advances the checkpoint through consecutively completed files in sorted order.
            /// Out-of-order completions are safe: if index 7 completes before index 6,
            /// TryRemove(6) fails and the checkpoint stays at 5. Once 6 completes,
            /// the while loop drains 6 then 7 and advances past both.
            /// Checkpoint is written only for the last consecutively completed file.
            /// </summary>
            void TryAdvanceCheckpoint()
            {
                if (checkpointPath is null) return;
                lock (checkpointLock)
                {
                    while (completedFlags.TryRemove(nextCheckpointIndex, out _))
                    {
                        nextCheckpointIndex++;
                    }

                    if (nextCheckpointIndex > 0)
                    {
                        WriteCheckpointAsync(checkpointPath, files[nextCheckpointIndex - 1], ct)
                            .ConfigureAwait(false).GetAwaiter().GetResult();
                    }
                }
            }
        }
        finally
        {
            semaphore.Dispose();
        }

        _logger.LogInformation(
            "Backfill complete: {Completed} imported, {Skipped} skipped (dedupe), {Failed} failed in {SourceDir}",
            completed, skipped, failed, resolvedDir);

        // Clear checkpoint on successful completion
        if (checkpointPath is not null && failed == 0 && !ct.IsCancellationRequested)
        {
            try
            {
                if (File.Exists(checkpointPath))
                {
                    File.Delete(checkpointPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clear checkpoint file {Path}", checkpointPath);
            }
        }

        return completed;
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