using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NCrontab;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Scheduler;

/// <summary>
/// Durable scheduler manager for CRUD and runtime execution of scheduled jobs.
/// </summary>
public sealed class ScheduledJobManager : IScheduledJobManager
{
    private readonly IScheduledJobStore _store;
    private readonly IProactiveJobExecutor _executor;
    private readonly ILogger<ScheduledJobManager> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, ScheduledJobDefinition> _jobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ScheduledJobState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _running = new(StringComparer.OrdinalIgnoreCase);
    private bool _initialized;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduledJobManager" /> class.
    /// </summary>
    public ScheduledJobManager(
        IScheduledJobStore store,
        IProactiveJobExecutor executor,
        ILogger<ScheduledJobManager> logger)
    {
        _store = store;
        _executor = executor;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_initialized)
                return;

            var snapshot = await _store.LoadAsync(ct);
            _jobs.Clear();
            _states.Clear();

            foreach (var job in snapshot.Jobs)
            {
                _jobs[job.Id] = job;
                _states[job.Id] = snapshot.States.GetValueOrDefault(job.Id) ?? new ScheduledJobState();
            }

            foreach (var kvp in _jobs)
            {
                var state = _states[kvp.Key];
                state.NextRunAtUtc = ComputeNextRunAtUtc(kvp.Value, state, DateTimeOffset.UtcNow);
            }

            _initialized = true;
            await SaveUnsafeAsync(ct);
            _logger.LogInformation("ScheduledJobManager initialized with {Count} jobs", _jobs.Count);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScheduledJobView>> ListAsync(
        ScheduledJobListOptions options,
        ScheduledJobActor actor,
        CancellationToken ct)
    {
        await InitializeAsync(ct);
        await _gate.WaitAsync(ct);
        try
        {
            return _jobs.Values
                .Where(job => options.IncludeDisabled || job.Enabled)
                .Where(job => options.IncludeAllJobs ? actor.IsAdmin : IsVisibleToActor(job, actor))
                .OrderBy(job => job.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(job => job.Id, StringComparer.OrdinalIgnoreCase)
                .Select(ToViewUnsafe)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ScheduledJobView?> GetAsync(string jobId, ScheduledJobActor actor, CancellationToken ct)
    {
        await InitializeAsync(ct);
        await _gate.WaitAsync(ct);
        try
        {
            if (!_jobs.TryGetValue(jobId, out var job) || !IsVisibleToActor(job, actor))
                return null;

            return ToViewUnsafe(job);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ScheduledJobView> CreateAsync(
        ScheduledJobCreateRequest request,
        ScheduledJobActor actor,
        CancellationToken ct)
    {
        await InitializeAsync(ct);
        await _gate.WaitAsync(ct);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var scope = request.Scope ?? ScheduledJobScope.Scoped;
            if (scope == ScheduledJobScope.Global)
                EnsureGlobalAllowed(actor, request.ScopeReason);

            var id = string.IsNullOrWhiteSpace(request.Id)
                ? GenerateJobId(request.Name)
                : NormalizeJobId(request.Id);
            if (_jobs.ContainsKey(id))
                throw new InvalidOperationException($"Job '{id}' already exists.");

            var job = new ScheduledJobDefinition
            {
                Id = id,
                Name = request.Name.Trim(),
                Enabled = request.Enabled,
                ScheduleKind = request.ScheduleKind,
                CronExpression = request.CronExpression?.Trim(),
                RunAtUtc = request.RunAtUtc,
                TimeZoneId = NormalizeTimeZoneId(request.TimeZoneId ?? "UTC"),
                ExecutionTimeoutSeconds = request.ExecutionTimeoutSeconds ?? 300,
                OverlapPolicy = request.OverlapPolicy ?? ScheduledJobOverlapPolicy.Skip,
                AgentId = string.IsNullOrWhiteSpace(request.AgentId) ? "main" : request.AgentId.Trim(),
                SessionKey = request.SessionKey?.Trim(),
                SessionTarget = string.IsNullOrWhiteSpace(request.SessionTarget) ? "isolated" : request.SessionTarget.Trim(),
                WakeMode = string.IsNullOrWhiteSpace(request.WakeMode) ? "now" : request.WakeMode.Trim(),
                PayloadMessage = request.PayloadMessage.Trim(),
                DeliveryChannel = string.IsNullOrWhiteSpace(request.DeliveryChannel) ? actor.ChannelId : request.DeliveryChannel.Trim(),
                DeliveryRecipient = string.IsNullOrWhiteSpace(request.DeliveryRecipient) ? actor.UserId : request.DeliveryRecipient.Trim(),
                DeliveryMode = string.IsNullOrWhiteSpace(request.DeliveryMode) ? "announce" : request.DeliveryMode.Trim(),
                Scope = scope,
                OwnerUserId = actor.UserId,
                OwnerChannelId = actor.ChannelId,
                OwnerSessionId = actor.SessionId,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            ValidateJobUnsafe(job);

            var state = new ScheduledJobState
            {
                NextRunAtUtc = ComputeNextRunAtUtc(job, null, now)
            };

            _jobs[id] = job;
            _states[id] = state;
            await SaveUnsafeAsync(ct);

            _logger.LogInformation("Created scheduled job {JobId} ({Scope})", id, scope);
            return ToViewUnsafe(job);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ScheduledJobView> UpdateAsync(
        string jobId,
        ScheduledJobUpdateRequest request,
        ScheduledJobActor actor,
        CancellationToken ct)
    {
        await InitializeAsync(ct);
        await _gate.WaitAsync(ct);
        try
        {
            var job = GetJobForUpdateUnsafe(jobId, actor);

            if (!string.IsNullOrWhiteSpace(request.Name))
                job.Name = request.Name.Trim();
            if (request.Enabled.HasValue)
                job.Enabled = request.Enabled.Value;
            if (request.ScheduleKind.HasValue)
                job.ScheduleKind = request.ScheduleKind.Value;
            if (request.CronExpression is not null)
                job.CronExpression = request.CronExpression.Trim();
            if (request.RunAtUtc.HasValue)
                job.RunAtUtc = request.RunAtUtc;
            if (!string.IsNullOrWhiteSpace(request.TimeZoneId))
                job.TimeZoneId = NormalizeTimeZoneId(request.TimeZoneId);
            if (request.ExecutionTimeoutSeconds.HasValue)
                job.ExecutionTimeoutSeconds = request.ExecutionTimeoutSeconds.Value;
            if (request.OverlapPolicy.HasValue)
                job.OverlapPolicy = request.OverlapPolicy.Value;
            if (!string.IsNullOrWhiteSpace(request.AgentId))
                job.AgentId = request.AgentId.Trim();
            if (request.SessionKey is not null)
                job.SessionKey = request.SessionKey.Trim();
            if (!string.IsNullOrWhiteSpace(request.SessionTarget))
                job.SessionTarget = request.SessionTarget.Trim();
            if (!string.IsNullOrWhiteSpace(request.WakeMode))
                job.WakeMode = request.WakeMode.Trim();
            if (!string.IsNullOrWhiteSpace(request.PayloadMessage))
                job.PayloadMessage = request.PayloadMessage.Trim();
            if (!string.IsNullOrWhiteSpace(request.DeliveryChannel))
                job.DeliveryChannel = request.DeliveryChannel.Trim();
            if (!string.IsNullOrWhiteSpace(request.DeliveryRecipient))
                job.DeliveryRecipient = request.DeliveryRecipient.Trim();
            if (!string.IsNullOrWhiteSpace(request.DeliveryMode))
                job.DeliveryMode = request.DeliveryMode.Trim();
            if (request.Scope.HasValue)
            {
                if (request.Scope.Value == ScheduledJobScope.Global)
                    EnsureGlobalAllowed(actor, request.ScopeReason);
                job.Scope = request.Scope.Value;
            }

            job.UpdatedAtUtc = DateTimeOffset.UtcNow;
            ValidateJobUnsafe(job);

            var state = _states[job.Id];
            state.NextRunAtUtc = ComputeNextRunAtUtc(job, state, DateTimeOffset.UtcNow);

            await SaveUnsafeAsync(ct);
            _logger.LogInformation("Updated scheduled job {JobId}", job.Id);
            return ToViewUnsafe(job);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string jobId, ScheduledJobActor actor, CancellationToken ct)
    {
        await InitializeAsync(ct);
        await _gate.WaitAsync(ct);
        try
        {
            var job = GetJobForUpdateUnsafe(jobId, actor);
            _jobs.Remove(job.Id);
            _states.Remove(job.Id);
            _running.Remove(job.Id);
            await SaveUnsafeAsync(ct);
            _logger.LogInformation("Deleted scheduled job {JobId}", job.Id);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ScheduledJobView> SetEnabledAsync(
        string jobId,
        bool enabled,
        ScheduledJobActor actor,
        CancellationToken ct)
    {
        await InitializeAsync(ct);
        await _gate.WaitAsync(ct);
        try
        {
            var job = GetJobForUpdateUnsafe(jobId, actor);
            job.Enabled = enabled;
            job.UpdatedAtUtc = DateTimeOffset.UtcNow;
            var state = _states[job.Id];
            state.NextRunAtUtc = ComputeNextRunAtUtc(job, state, DateTimeOffset.UtcNow);
            await SaveUnsafeAsync(ct);
            _logger.LogInformation("{Action} scheduled job {JobId}", enabled ? "Enabled" : "Disabled", job.Id);
            return ToViewUnsafe(job);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ScheduledJobView> TriggerAsync(string jobId, ScheduledJobActor actor, CancellationToken ct)
    {
        await InitializeAsync(ct);
        ScheduledJobDefinition job;
        await _gate.WaitAsync(ct);
        try
        {
            job = GetJobForUpdateUnsafe(jobId, actor);
        }
        finally
        {
            _gate.Release();
        }

        await RunJobAsync(job.Id, force: true, ct);

        await _gate.WaitAsync(ct);
        try
        {
            return ToViewUnsafe(_jobs[job.Id]);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task ProcessDueJobsAsync(CancellationToken ct)
    {
        await InitializeAsync(ct);

        List<string> dueJobIds;
        await _gate.WaitAsync(ct);
        try
        {
            var now = DateTimeOffset.UtcNow;
            dueJobIds = _jobs.Values
                .Where(job => job.Enabled)
                .Where(job => _states.TryGetValue(job.Id, out var state) && state.NextRunAtUtc.HasValue && state.NextRunAtUtc.Value <= now)
                .Select(job => job.Id)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }

        foreach (var jobId in dueJobIds)
        {
            await RunJobAsync(jobId, force: false, ct);
        }
    }

    private async Task RunJobAsync(string jobId, bool force, CancellationToken ct)
    {
        ScheduledJobDefinition job;
        ScheduledJobState state;
        await _gate.WaitAsync(ct);
        try
        {
            if (!_jobs.TryGetValue(jobId, out job!))
                return;

            state = _states.GetValueOrDefault(jobId) ?? new ScheduledJobState();
            _states[jobId] = state;

            if (!force && !job.Enabled)
                return;

            if (_running.Contains(job.Id) && job.OverlapPolicy == ScheduledJobOverlapPolicy.Skip)
            {
                state.LastStatus = "skipped";
                state.LastErrorReason = "overlap";
                state.LastError = "Skipped due to overlap policy";
                state.ConsecutiveSkips++;
                state.NextRunAtUtc = ComputeNextRunAtUtc(job, state, DateTimeOffset.UtcNow);
                await SaveUnsafeAsync(ct);
                return;
            }

            _running.Add(job.Id);
        }
        finally
        {
            _gate.Release();
        }

        var startedAt = DateTimeOffset.UtcNow;
        var sw = Stopwatch.StartNew();

        ScheduledJobExecutionResult executionResult;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (job.ExecutionTimeoutSeconds > 0)
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(job.ExecutionTimeoutSeconds));

        try
        {
            executionResult = await _executor.ExecuteAsync(job, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            executionResult = ScheduledJobExecutionResult.Failed(
                "Scheduled job execution timed out.",
                reason: "timeout",
                deliveryStatus: "timeout");
        }
        catch (Exception ex)
        {
            executionResult = ScheduledJobExecutionResult.Failed(
                ex.Message,
                reason: "execution_error",
                deliveryStatus: "error");
        }
        finally
        {
            sw.Stop();
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (!_jobs.TryGetValue(jobId, out job!))
                return;

            state = _states[jobId];
            state.LastRunAtUtc = startedAt;
            state.LastDurationMs = sw.ElapsedMilliseconds;
            state.LastDeliveryStatus = executionResult.DeliveryStatus;
            state.LastError = executionResult.Error;
            state.LastErrorReason = executionResult.ErrorReason;

            if (executionResult.Success)
            {
                state.LastStatus = "ok";
                state.ConsecutiveErrors = 0;
            }
            else
            {
                state.LastStatus = "error";
                state.ConsecutiveErrors++;
            }

            state.NextRunAtUtc = ComputeNextRunAtUtc(job, state, DateTimeOffset.UtcNow);
            _running.Remove(jobId);

            if (job.ScheduleKind == ScheduledJobScheduleKind.At &&
                state.LastRunAtUtc.HasValue &&
                job.RunAtUtc.HasValue &&
                state.LastRunAtUtc.Value >= job.RunAtUtc.Value)
            {
                job.Enabled = false;
                job.UpdatedAtUtc = DateTimeOffset.UtcNow;
                state.NextRunAtUtc = null;
            }

            await SaveUnsafeAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private ScheduledJobView ToViewUnsafe(ScheduledJobDefinition job)
    {
        var state = _states.GetValueOrDefault(job.Id) ?? new ScheduledJobState();
        return new ScheduledJobView
        {
            Definition = Clone(job),
            State = Clone(state)
        };
    }

    private ScheduledJobDefinition GetJobForUpdateUnsafe(string jobId, ScheduledJobActor actor)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
            throw new KeyNotFoundException($"Job '{jobId}' not found.");

        if (!CanManage(job, actor))
            throw new UnauthorizedAccessException("You are not allowed to manage this scheduled job.");

        return job;
    }

    private static bool IsVisibleToActor(ScheduledJobDefinition job, ScheduledJobActor actor)
    {
        if (actor.IsAdmin)
            return true;

        return string.Equals(job.OwnerUserId, actor.UserId, StringComparison.OrdinalIgnoreCase)
               && string.Equals(job.OwnerChannelId, actor.ChannelId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanManage(ScheduledJobDefinition job, ScheduledJobActor actor) =>
        IsVisibleToActor(job, actor);

    private static void EnsureGlobalAllowed(ScheduledJobActor actor, string? scopeReason)
    {
        if (!actor.IsAdmin)
            throw new UnauthorizedAccessException("Global scope requires administrator privileges.");

        if (string.IsNullOrWhiteSpace(scopeReason))
            throw new InvalidOperationException("Global scope requires an explicit scopeReason.");
    }

    private static ScheduledJobDefinition Clone(ScheduledJobDefinition source) => new()
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

    private static ScheduledJobState Clone(ScheduledJobState source) => new()
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

    private void ValidateJobUnsafe(ScheduledJobDefinition job)
    {
        if (string.IsNullOrWhiteSpace(job.Id))
            throw new InvalidOperationException("Job id is required.");

        if (string.IsNullOrWhiteSpace(job.Name))
            throw new InvalidOperationException("Job name is required.");

        if (string.IsNullOrWhiteSpace(job.PayloadMessage))
            throw new InvalidOperationException("Payload message is required.");

        if (string.IsNullOrWhiteSpace(job.DeliveryChannel))
            throw new InvalidOperationException("Delivery channel is required.");

        if (string.IsNullOrWhiteSpace(job.DeliveryRecipient))
            throw new InvalidOperationException("Delivery recipient is required.");

        if (job.ExecutionTimeoutSeconds <= 0)
            throw new InvalidOperationException("Execution timeout must be greater than zero.");

        job.TimeZoneId = NormalizeTimeZoneId(job.TimeZoneId);
        _ = ResolveTimeZone(job.TimeZoneId);

        switch (job.ScheduleKind)
        {
            case ScheduledJobScheduleKind.Cron:
                if (string.IsNullOrWhiteSpace(job.CronExpression))
                    throw new InvalidOperationException("CronExpression is required for cron schedules.");
                _ = CrontabSchedule.Parse(job.CronExpression);
                break;
            case ScheduledJobScheduleKind.At:
                if (!job.RunAtUtc.HasValue)
                    throw new InvalidOperationException("RunAtUtc is required for one-time schedules.");
                break;
            default:
                throw new InvalidOperationException($"Unsupported schedule kind: {job.ScheduleKind}");
        }
    }

    private static DateTimeOffset? ComputeNextRunAtUtc(
        ScheduledJobDefinition job,
        ScheduledJobState? state,
        DateTimeOffset nowUtc)
    {
        if (!job.Enabled)
            return null;

        if (job.ScheduleKind == ScheduledJobScheduleKind.At)
        {
            if (!job.RunAtUtc.HasValue)
                return null;

            var runAt = job.RunAtUtc.Value;
            if (state?.LastRunAtUtc.HasValue == true && state.LastRunAtUtc.Value >= runAt)
                return null;
            return runAt <= nowUtc ? nowUtc : runAt;
        }

        if (string.IsNullOrWhiteSpace(job.CronExpression))
            return null;

        var schedule = CrontabSchedule.Parse(job.CronExpression);
        var tz = ResolveTimeZone(job.TimeZoneId);
        var nowLocal = TimeZoneInfo.ConvertTime(nowUtc, tz);
        var nextLocal = schedule.GetNextOccurrence(nowLocal.DateTime);
        var nextUnspecified = DateTime.SpecifyKind(nextLocal, DateTimeKind.Unspecified);
        var nextUtc = TimeZoneInfo.ConvertTimeToUtc(nextUnspecified, tz);
        return new DateTimeOffset(nextUtc, TimeSpan.Zero);
    }

    private static string GenerateJobId(string name)
    {
        var normalized = NormalizeJobId(name);
        var suffix = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        return $"{normalized}-{suffix}";
    }

    private static string NormalizeJobId(string value)
    {
        var chars = value.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        var normalized = new string(chars);
        while (normalized.Contains("--", StringComparison.Ordinal))
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        return normalized.Trim('-');
    }

    private async Task SaveUnsafeAsync(CancellationToken ct)
    {
        var snapshot = new ScheduledJobStoreSnapshot
        {
            Jobs = _jobs.Values.Select(Clone).ToList(),
            States = _states.ToDictionary(
                kvp => kvp.Key,
                kvp => Clone(kvp.Value),
                StringComparer.OrdinalIgnoreCase)
        };
        await _store.SaveAsync(snapshot, ct);
    }

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        var normalized = NormalizeTimeZoneId(timeZoneId);
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(normalized);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static string NormalizeTimeZoneId(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            return "Etc/UTC";

        return timeZoneId.Trim() switch
        {
            "UTC" => "Etc/UTC",
            "GMT" => "Etc/UTC",
            "Eastern" => "America/New_York",
            "Central" => "America/Chicago",
            "Mountain" => "America/Denver",
            "Pacific" => "America/Los_Angeles",
            _ => timeZoneId.Trim()
        };
    }
}
