namespace LeanKernel.Core.Models;

/// <summary>
/// Defines the visibility and ownership scope for a scheduled job.
/// </summary>
public enum ScheduledJobScope
{
    /// <summary>
    /// Job is scoped to a specific user/channel owner.
    /// </summary>
    Scoped,

    /// <summary>
    /// Job is globally visible/manageable by administrators.
    /// </summary>
    Global
}

/// <summary>
/// Defines the supported schedule types.
/// </summary>
public enum ScheduledJobScheduleKind
{
    /// <summary>
    /// Cron-based recurring schedule.
    /// </summary>
    Cron,

    /// <summary>
    /// One-time execution at a specific UTC time.
    /// </summary>
    At
}

/// <summary>
/// Defines overlap behavior when a job is due while a prior run is still executing.
/// </summary>
public enum ScheduledJobOverlapPolicy
{
    /// <summary>
    /// Skip the overlapping occurrence.
    /// </summary>
    Skip,

    /// <summary>
    /// Allow concurrent overlapping execution.
    /// </summary>
    Concurrent
}

/// <summary>
/// Durable scheduled job definition.
/// </summary>
public sealed class ScheduledJobDefinition
{
    /// <summary>
    /// Gets or sets the stable job identifier.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Gets or sets the human-readable job name.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the job is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the schedule kind.
    /// </summary>
    public ScheduledJobScheduleKind ScheduleKind { get; set; } = ScheduledJobScheduleKind.Cron;

    /// <summary>
    /// Gets or sets the cron expression when <see cref="ScheduleKind"/> is <see cref="ScheduledJobScheduleKind.Cron"/>.
    /// </summary>
    public string? CronExpression { get; set; }

    /// <summary>
    /// Gets or sets the one-time UTC execution timestamp when <see cref="ScheduleKind"/> is <see cref="ScheduledJobScheduleKind.At"/>.
    /// </summary>
    public DateTimeOffset? RunAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the IANA/system timezone identifier used for cron evaluation.
    /// </summary>
    public string TimeZoneId { get; set; } = "UTC";

    /// <summary>
    /// Gets or sets the execution timeout in seconds.
    /// </summary>
    public int ExecutionTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Gets or sets overlap handling behavior.
    /// </summary>
    public ScheduledJobOverlapPolicy OverlapPolicy { get; set; } = ScheduledJobOverlapPolicy.Skip;

    /// <summary>
    /// Gets or sets the target agent identifier.
    /// </summary>
    public string AgentId { get; set; } = "main";

    /// <summary>
    /// Gets or sets the target session key.
    /// </summary>
    public string? SessionKey { get; set; }

    /// <summary>
    /// Gets or sets session targeting mode.
    /// </summary>
    public string SessionTarget { get; set; } = "isolated";

    /// <summary>
    /// Gets or sets wake mode.
    /// </summary>
    public string WakeMode { get; set; } = "now";

    /// <summary>
    /// Gets or sets the prompt/message payload executed by the scheduler.
    /// </summary>
    public required string PayloadMessage { get; set; }

    /// <summary>
    /// Gets or sets delivery channel identifier.
    /// </summary>
    public required string DeliveryChannel { get; set; }

    /// <summary>
    /// Gets or sets delivery recipient identifier.
    /// </summary>
    public required string DeliveryRecipient { get; set; }

    /// <summary>
    /// Gets or sets delivery mode.
    /// </summary>
    public string DeliveryMode { get; set; } = "announce";

    /// <summary>
    /// Gets or sets scope.
    /// </summary>
    public ScheduledJobScope Scope { get; set; } = ScheduledJobScope.Scoped;

    /// <summary>
    /// Gets or sets owner user identifier.
    /// </summary>
    public required string OwnerUserId { get; set; }

    /// <summary>
    /// Gets or sets owner channel identifier.
    /// </summary>
    public required string OwnerChannelId { get; set; }

    /// <summary>
    /// Gets or sets owner session identifier.
    /// </summary>
    public string? OwnerSessionId { get; set; }

    /// <summary>
    /// Gets or sets creation timestamp in UTC.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets last update timestamp in UTC.
    /// </summary>
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Durable scheduler runtime state for a job.
/// </summary>
public sealed class ScheduledJobState
{
    /// <summary>
    /// Gets or sets when the job is next due.
    /// </summary>
    public DateTimeOffset? NextRunAtUtc { get; set; }

    /// <summary>
    /// Gets or sets when the job last started executing.
    /// </summary>
    public DateTimeOffset? LastRunAtUtc { get; set; }

    /// <summary>
    /// Gets or sets last run status string.
    /// </summary>
    public string LastStatus { get; set; } = "unknown";

    /// <summary>
    /// Gets or sets last run duration in milliseconds.
    /// </summary>
    public long LastDurationMs { get; set; }

    /// <summary>
    /// Gets or sets last delivery status.
    /// </summary>
    public string? LastDeliveryStatus { get; set; }

    /// <summary>
    /// Gets or sets last error details.
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Gets or sets last error reason code.
    /// </summary>
    public string? LastErrorReason { get; set; }

    /// <summary>
    /// Gets or sets consecutive error count.
    /// </summary>
    public int ConsecutiveErrors { get; set; }

    /// <summary>
    /// Gets or sets consecutive skipped run count.
    /// </summary>
    public int ConsecutiveSkips { get; set; }
}

/// <summary>
/// View model combining job definition and current state.
/// </summary>
public sealed class ScheduledJobView
{
    /// <summary>
    /// Gets or sets job definition.
    /// </summary>
    public required ScheduledJobDefinition Definition { get; set; }

    /// <summary>
    /// Gets or sets runtime state.
    /// </summary>
    public required ScheduledJobState State { get; set; }
}

/// <summary>
/// Actor context for scheduler management authorization.
/// </summary>
public sealed class ScheduledJobActor
{
    /// <summary>
    /// Gets or sets current user identifier.
    /// </summary>
    public required string UserId { get; set; }

    /// <summary>
    /// Gets or sets current channel identifier.
    /// </summary>
    public required string ChannelId { get; set; }

    /// <summary>
    /// Gets or sets current session identifier.
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether actor is admin.
    /// </summary>
    public bool IsAdmin { get; set; }
}

/// <summary>
/// Input model for creating a scheduled job.
/// </summary>
public sealed class ScheduledJobCreateRequest
{
    /// <summary>
    /// Gets or sets optional job identifier.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets job name.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets schedule kind.
    /// </summary>
    public ScheduledJobScheduleKind ScheduleKind { get; set; } = ScheduledJobScheduleKind.Cron;

    /// <summary>
    /// Gets or sets cron expression.
    /// </summary>
    public string? CronExpression { get; set; }

    /// <summary>
    /// Gets or sets one-time run timestamp in UTC.
    /// </summary>
    public DateTimeOffset? RunAtUtc { get; set; }

    /// <summary>
    /// Gets or sets timezone identifier.
    /// </summary>
    public string? TimeZoneId { get; set; }

    /// <summary>
    /// Gets or sets execution timeout in seconds.
    /// </summary>
    public int? ExecutionTimeoutSeconds { get; set; }

    /// <summary>
    /// Gets or sets overlap policy.
    /// </summary>
    public ScheduledJobOverlapPolicy? OverlapPolicy { get; set; }

    /// <summary>
    /// Gets or sets target agent identifier.
    /// </summary>
    public string? AgentId { get; set; }

    /// <summary>
    /// Gets or sets session key.
    /// </summary>
    public string? SessionKey { get; set; }

    /// <summary>
    /// Gets or sets session target mode.
    /// </summary>
    public string? SessionTarget { get; set; }

    /// <summary>
    /// Gets or sets wake mode.
    /// </summary>
    public string? WakeMode { get; set; }

    /// <summary>
    /// Gets or sets payload message.
    /// </summary>
    public required string PayloadMessage { get; set; }

    /// <summary>
    /// Gets or sets delivery channel.
    /// </summary>
    public string? DeliveryChannel { get; set; }

    /// <summary>
    /// Gets or sets delivery recipient.
    /// </summary>
    public string? DeliveryRecipient { get; set; }

    /// <summary>
    /// Gets or sets delivery mode.
    /// </summary>
    public string? DeliveryMode { get; set; }

    /// <summary>
    /// Gets or sets scope.
    /// </summary>
    public ScheduledJobScope? Scope { get; set; }

    /// <summary>
    /// Gets or sets rationale for elevation to global scope.
    /// </summary>
    public string? ScopeReason { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the job is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Input model for updating a scheduled job.
/// </summary>
public sealed class ScheduledJobUpdateRequest
{
    /// <summary>
    /// Gets or sets optional job name update.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets optional enabled flag update.
    /// </summary>
    public bool? Enabled { get; set; }

    /// <summary>
    /// Gets or sets optional schedule kind update.
    /// </summary>
    public ScheduledJobScheduleKind? ScheduleKind { get; set; }

    /// <summary>
    /// Gets or sets optional cron expression update.
    /// </summary>
    public string? CronExpression { get; set; }

    /// <summary>
    /// Gets or sets optional one-time run timestamp update.
    /// </summary>
    public DateTimeOffset? RunAtUtc { get; set; }

    /// <summary>
    /// Gets or sets optional timezone update.
    /// </summary>
    public string? TimeZoneId { get; set; }

    /// <summary>
    /// Gets or sets optional execution timeout update.
    /// </summary>
    public int? ExecutionTimeoutSeconds { get; set; }

    /// <summary>
    /// Gets or sets optional overlap policy update.
    /// </summary>
    public ScheduledJobOverlapPolicy? OverlapPolicy { get; set; }

    /// <summary>
    /// Gets or sets optional agent identifier update.
    /// </summary>
    public string? AgentId { get; set; }

    /// <summary>
    /// Gets or sets optional session key update.
    /// </summary>
    public string? SessionKey { get; set; }

    /// <summary>
    /// Gets or sets optional session target update.
    /// </summary>
    public string? SessionTarget { get; set; }

    /// <summary>
    /// Gets or sets optional wake mode update.
    /// </summary>
    public string? WakeMode { get; set; }

    /// <summary>
    /// Gets or sets optional payload message update.
    /// </summary>
    public string? PayloadMessage { get; set; }

    /// <summary>
    /// Gets or sets optional delivery channel update.
    /// </summary>
    public string? DeliveryChannel { get; set; }

    /// <summary>
    /// Gets or sets optional delivery recipient update.
    /// </summary>
    public string? DeliveryRecipient { get; set; }

    /// <summary>
    /// Gets or sets optional delivery mode update.
    /// </summary>
    public string? DeliveryMode { get; set; }

    /// <summary>
    /// Gets or sets optional scope update.
    /// </summary>
    public ScheduledJobScope? Scope { get; set; }

    /// <summary>
    /// Gets or sets rationale for elevation to global scope.
    /// </summary>
    public string? ScopeReason { get; set; }
}

/// <summary>
/// Options for listing scheduled jobs.
/// </summary>
public sealed class ScheduledJobListOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether disabled jobs are included.
    /// </summary>
    public bool IncludeDisabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether all jobs should be returned.
    /// Admin-only behavior.
    /// </summary>
    public bool IncludeAllJobs { get; set; }
}

/// <summary>
/// Execution result returned by a proactive job executor.
/// </summary>
public sealed class ScheduledJobExecutionResult
{
    /// <summary>
    /// Gets or sets a value indicating whether execution succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets delivery status for diagnostics.
    /// </summary>
    public string? DeliveryStatus { get; set; }

    /// <summary>
    /// Gets or sets optional delivery reference.
    /// </summary>
    public string? DeliveryReference { get; set; }

    /// <summary>
    /// Gets or sets optional error message.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Gets or sets optional error reason code.
    /// </summary>
    public string? ErrorReason { get; set; }

    /// <summary>
    /// Creates a successful execution result.
    /// </summary>
    public static ScheduledJobExecutionResult Successful(string? deliveryStatus = null, string? deliveryReference = null) =>
        new()
        {
            Success = true,
            DeliveryStatus = deliveryStatus,
            DeliveryReference = deliveryReference
        };

    /// <summary>
    /// Creates a failed execution result.
    /// </summary>
    public static ScheduledJobExecutionResult Failed(string error, string? reason = null, string? deliveryStatus = null) =>
        new()
        {
            Success = false,
            Error = error,
            ErrorReason = reason,
            DeliveryStatus = deliveryStatus
        };
}

/// <summary>
/// Snapshot persisted by scheduled job stores.
/// </summary>
public sealed class ScheduledJobStoreSnapshot
{
    /// <summary>
    /// Gets or sets schema version.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Gets or sets persisted job definitions.
    /// </summary>
    public List<ScheduledJobDefinition> Jobs { get; set; } = [];

    /// <summary>
    /// Gets or sets persisted job state keyed by job id.
    /// </summary>
    public Dictionary<string, ScheduledJobState> States { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Current chat turn execution context used by tools for scoped defaults.
/// </summary>
public sealed class ChatExecutionContext
{
    /// <summary>
    /// Gets or sets current sender/user identifier.
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Gets or sets current channel identifier.
    /// </summary>
    public required string ChannelId { get; init; }

    /// <summary>
    /// Gets or sets current session identifier.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether current actor is admin.
    /// </summary>
    public bool IsAdmin { get; init; }
}
