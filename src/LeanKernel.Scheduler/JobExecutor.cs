using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Persistence;
using LeanKernel.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Scheduler;

/// <summary>
/// Executes scheduled jobs and persists their outcomes.
/// </summary>
public sealed class JobExecutor(
    IAgentRuntime agentRuntime,
    IKnowledgeService knowledgeService,
    IDbContextFactory<LeanKernelDbContext> dbFactory,
    TimeBoundaryService timeBoundaryService,
    TimeProvider timeProvider,
    ILogger<JobExecutor> logger)
{
    private readonly IAgentRuntime _agentRuntime = agentRuntime ?? throw new ArgumentNullException(nameof(agentRuntime));
    private readonly IKnowledgeService _knowledgeService = knowledgeService ?? throw new ArgumentNullException(nameof(knowledgeService));
    private readonly IDbContextFactory<LeanKernelDbContext> _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly TimeBoundaryService _timeBoundaryService = timeBoundaryService ?? throw new ArgumentNullException(nameof(timeBoundaryService));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly ILogger<JobExecutor> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Executes a scheduled job occurrence.
    /// </summary>
    /// <param name="job">The scheduled job definition.</param>
    /// <param name="scheduledAt">The scheduled occurrence being executed.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The recorded execution result.</returns>
    public async Task<ScheduledJobExecution> ExecuteAsync(
        ScheduledJobDefinition job,
        DateTimeOffset scheduledAt,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        var startedAt = _timeProvider.GetUtcNow();
        ScheduledJobExecution execution;

        try
        {
            var boundarySkipResult = TryGetBoundarySkipResult(job, startedAt);
            var result = boundarySkipResult ?? await ExecuteCoreAsync(job, scheduledAt, startedAt, ct).ConfigureAwait(false);

            execution = new ScheduledJobExecution
            {
                JobName = job.Name,
                ScheduledAt = scheduledAt,
                StartedAt = startedAt,
                CompletedAt = _timeProvider.GetUtcNow(),
                Success = true,
                Result = result,
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            execution = new ScheduledJobExecution
            {
                JobName = job.Name,
                ScheduledAt = scheduledAt,
                StartedAt = startedAt,
                CompletedAt = _timeProvider.GetUtcNow(),
                Success = false,
                Error = "The scheduled job was cancelled.",
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Scheduled job {JobName} failed", job.Name);
            execution = new ScheduledJobExecution
            {
                JobName = job.Name,
                ScheduledAt = scheduledAt,
                StartedAt = startedAt,
                CompletedAt = _timeProvider.GetUtcNow(),
                Success = false,
                Error = ex.Message,
            };
        }

        await PersistExecutionAsync(execution).ConfigureAwait(false);
        return execution;
    }

    private async Task<string> ExecuteCoreAsync(
        ScheduledJobDefinition job,
        DateTimeOffset scheduledAt,
        DateTimeOffset startedAt,
        CancellationToken ct)
        => job.JobType switch
        {
            "agent-prompt" => await ExecuteAgentPromptAsync(job, scheduledAt, startedAt, ct).ConfigureAwait(false),
            "knowledge-refresh" => await ExecuteKnowledgeRefreshAsync(job, ct).ConfigureAwait(false),
            "maintenance" => await ExecuteMaintenanceAsync(job, startedAt, ct).ConfigureAwait(false),
            _ => throw new ArgumentException($"Unsupported scheduled job type '{job.JobType}'.", nameof(job)),
        };

    private async Task<string> ExecuteAgentPromptAsync(
        ScheduledJobDefinition job,
        DateTimeOffset scheduledAt,
        DateTimeOffset startedAt,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(job.Prompt);

        var currentBoundary = _timeBoundaryService.GetCurrentBoundary(startedAt, GetTimeZoneId(job));
        var message = new LeanKernelMessage
        {
            Content = job.Prompt,
            SenderId = string.IsNullOrWhiteSpace(job.UserId) ? "system" : job.UserId,
            ChannelId = string.IsNullOrWhiteSpace(job.ChannelId) ? "scheduler" : job.ChannelId,
            Timestamp = startedAt,
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["scheduler_job_name"] = job.Name,
                ["scheduler_job_type"] = job.JobType,
                ["scheduler_scheduled_at"] = scheduledAt.ToString("O"),
                ["scheduler_boundary"] = currentBoundary.ToString(),
            },
        };

        return await _agentRuntime.RunTurnAsync(message, ct).ConfigureAwait(false);
    }

    private async Task<string> ExecuteKnowledgeRefreshAsync(ScheduledJobDefinition job, CancellationToken ct)
    {
        if (TryGetParameter(job, "key", out var key))
        {
            var page = await _knowledgeService.GetPageAsync(key, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Knowledge page '{key}' was not found.");

            await _knowledgeService.PutPageAsync(page.Key, page.Content, ct).ConfigureAwait(false);
            return $"Refreshed knowledge page '{page.Key}'.";
        }

        var query = TryGetParameter(job, "query", out var configuredQuery)
            ? configuredQuery
            : job.Prompt;
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var maxResults = GetIntParameter(job, "max_results", 5, minimumValue: 1);
        var candidates = await _knowledgeService.SearchAsync(query, maxResults, ct).ConfigureAwait(false);
        if (candidates.Count == 0)
        {
            return $"No knowledge pages matched query '{query}'.";
        }

        var refreshedKeys = new List<string>();
        foreach (var candidate in candidates)
        {
            var page = await _knowledgeService.GetPageAsync(candidate.Key, ct).ConfigureAwait(false);
            if (page is null)
            {
                continue;
            }

            await _knowledgeService.PutPageAsync(page.Key, page.Content, ct).ConfigureAwait(false);
            refreshedKeys.Add(page.Key);
        }

        return refreshedKeys.Count == 0
            ? $"Found {candidates.Count} candidate knowledge pages for '{query}', but none could be refreshed."
            : $"Refreshed knowledge pages: {string.Join(", ", refreshedKeys.Distinct(StringComparer.Ordinal))}.";
    }

    private async Task<string> ExecuteMaintenanceAsync(
        ScheduledJobDefinition job,
        DateTimeOffset startedAt,
        CancellationToken ct)
    {
        var retentionDays = GetIntParameter(job, "retention_days", 30, minimumValue: 1);
        var task = TryGetParameter(job, "task", out var configuredTask)
            ? configuredTask
            : "cleanup-old-diagnostics";
        var cutoff = startedAt.AddDays(-retentionDays);

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var removedDiagnostics = 0;
        var removedMarkers = 0;

        switch (task)
        {
            case "cleanup-old-diagnostics":
                removedDiagnostics = await RemoveDiagnosticsAsync(db, cutoff, ct).ConfigureAwait(false);
                break;
            case "cleanup-compaction-markers":
                removedMarkers = await RemoveCompactionMarkersAsync(db, cutoff, ct).ConfigureAwait(false);
                break;
            case "cleanup-all":
                removedDiagnostics = await RemoveDiagnosticsAsync(db, cutoff, ct).ConfigureAwait(false);
                removedMarkers = await RemoveCompactionMarkersAsync(db, cutoff, ct).ConfigureAwait(false);
                break;
            default:
                throw new ArgumentException($"Unsupported maintenance task '{task}'.", nameof(job));
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return $"Maintenance task '{task}' removed {removedDiagnostics} diagnostic entries and {removedMarkers} compaction markers older than {retentionDays} days.";
    }

    private async Task PersistExecutionAsync(ScheduledJobExecution execution)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(CancellationToken.None).ConfigureAwait(false);
        db.ScheduledJobExecutions.Add(new ScheduledJobEntity
        {
            JobName = execution.JobName,
            ScheduledAt = execution.ScheduledAt,
            StartedAt = execution.StartedAt,
            CompletedAt = execution.CompletedAt,
            Success = execution.Success,
            Result = execution.Result,
            Error = execution.Error,
        });

        await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private string? TryGetBoundarySkipResult(ScheduledJobDefinition job, DateTimeOffset startedAt)
    {
        if (!TryGetParameter(job, "required_boundary", out var requiredBoundary) &&
            !TryGetParameter(job, "time_boundary", out requiredBoundary))
        {
            return null;
        }

        var currentBoundary = _timeBoundaryService.GetCurrentBoundary(startedAt, GetTimeZoneId(job));
        return currentBoundary.ToString().Equals(requiredBoundary, StringComparison.OrdinalIgnoreCase)
            ? null
            : $"Skipped job '{job.Name}' because the current boundary '{currentBoundary}' does not match required boundary '{requiredBoundary}'.";
    }

    private static async Task<int> RemoveDiagnosticsAsync(
        LeanKernelDbContext db,
        DateTimeOffset cutoff,
        CancellationToken ct)
    {
        var entities = await db.DiagnosticEntries
            .Where(entry => entry.Timestamp < cutoff)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        db.DiagnosticEntries.RemoveRange(entities);
        return entities.Count;
    }

    private static async Task<int> RemoveCompactionMarkersAsync(
        LeanKernelDbContext db,
        DateTimeOffset cutoff,
        CancellationToken ct)
    {
        var entities = await db.CompactionMarkers
            .Where(marker => marker.CompactedAt < cutoff)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        db.CompactionMarkers.RemoveRange(entities);
        return entities.Count;
    }

    private static int GetIntParameter(
        ScheduledJobDefinition job,
        string key,
        int defaultValue,
        int minimumValue)
    {
        if (!TryGetParameter(job, key, out var rawValue) || !int.TryParse(rawValue, out var parsedValue))
        {
            return defaultValue;
        }

        return Math.Max(minimumValue, parsedValue);
    }

    private static string? GetTimeZoneId(ScheduledJobDefinition job)
        => TryGetParameter(job, "timezone", out var timeZoneId) ? timeZoneId : null;

    private static bool TryGetParameter(ScheduledJobDefinition job, string key, out string value)
    {
        if (job.Parameters.TryGetValue(key, out var candidate) && !string.IsNullOrWhiteSpace(candidate))
        {
            value = candidate;
            return true;
        }

        value = string.Empty;
        return false;
    }
}
