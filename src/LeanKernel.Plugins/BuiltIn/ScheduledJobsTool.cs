using System.Diagnostics;
using System.Text.Json;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Plugins.Sdk;

namespace LeanKernel.Plugins.BuiltIn;

/// <summary>
/// Chat-facing CRUD and control operations for scheduled jobs.
/// </summary>
[ToolMetadata(
    Name = "scheduled_jobs",
    Description = "Create, list, update, and manage scheduled jobs.",
    Category = ToolCategory.Scheduling)]
public sealed class ScheduledJobsTool : ITool, IOperationsTool
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly IScheduledJobManager _jobManager;
    private readonly IChatExecutionContextAccessor _executionContextAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduledJobsTool" /> class.
    /// </summary>
    public ScheduledJobsTool(
        IScheduledJobManager jobManager,
        IChatExecutionContextAccessor executionContextAccessor)
    {
        _jobManager = jobManager;
        _executionContextAccessor = executionContextAccessor;
    }

    /// <inheritdoc />
    public string Name => "scheduled_jobs";

    /// <inheritdoc />
    public string Description => "Manage scheduled jobs (CRUD, enable/disable, trigger).";

    /// <inheritdoc />
    public string Category => ToolCategory.Scheduling.ToString().ToLowerInvariant();

    /// <inheritdoc />
    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "operation": {
              "type": "string",
              "enum": ["create_job", "get_job", "list_jobs", "update_job", "delete_job", "enable_job", "disable_job", "trigger_job"]
            }
          },
          "required": ["operation"]
        }
        """;

    /// <inheritdoc />
    public IReadOnlyList<ToolOperationDescriptor> Operations =>
    [
        new("create_job", "Create a scheduled job scoped to the current user/channel by default.", CreateSchema),
        new("get_job", "Get one scheduled job by id.", GetSchema),
        new("list_jobs", "List scheduled jobs visible to the current user.", ListSchema),
        new("update_job", "Update a scheduled job.", UpdateSchema),
        new("delete_job", "Delete a scheduled job.", DeleteSchema),
        new("enable_job", "Enable a scheduled job.", ToggleSchema),
        new("disable_job", "Disable a scheduled job.", ToggleSchema),
        new("trigger_job", "Trigger immediate execution of a scheduled job.", TriggerSchema)
    ];

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteAsync(string parametersJson, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var actor = GetActor();
            using var doc = JsonDocument.Parse(parametersJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("operation", out var operationElement))
                return Fail("Missing required 'operation' field.");

            var operation = operationElement.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(operation))
                return Fail("Operation is required.");

            var output = operation switch
            {
                "create_job" => await CreateJobAsync(root, actor, ct),
                "get_job" => await GetJobAsync(root, actor, ct),
                "list_jobs" => await ListJobsAsync(root, actor, ct),
                "update_job" => await UpdateJobAsync(root, actor, ct),
                "delete_job" => await DeleteJobAsync(root, actor, ct),
                "enable_job" => await SetEnabledAsync(root, actor, enabled: true, ct),
                "disable_job" => await SetEnabledAsync(root, actor, enabled: false, ct),
                "trigger_job" => await TriggerJobAsync(root, actor, ct),
                _ => throw new InvalidOperationException($"Unsupported operation '{operation}'.")
            };

            return new ToolResult
            {
                ToolName = Name,
                Success = true,
                Output = output,
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            return new ToolResult
            {
                ToolName = Name,
                Success = false,
                Error = ex.Message,
                Duration = sw.Elapsed
            };
        }

        ToolResult Fail(string error) => new()
        {
            ToolName = Name,
            Success = false,
            Error = error,
            Duration = sw.Elapsed
        };
    }

    private ScheduledJobActor GetActor()
    {
        var context = _executionContextAccessor.Current
                      ?? throw new InvalidOperationException(
                          "No active chat execution context. scheduled_jobs can only be run from a chat turn.");

        return new ScheduledJobActor
        {
            UserId = context.UserId,
            ChannelId = context.ChannelId,
            SessionId = context.SessionId,
            IsAdmin = context.IsAdmin
        };
    }

    private async Task<string> CreateJobAsync(JsonElement root, ScheduledJobActor actor, CancellationToken ct)
    {
        var name = RequireString(root, "name");
        var message = RequireString(root, "message");
        var runAtUtc = ReadDateTimeOffset(root, "runAtUtc");
        var cron = ReadString(root, "cron");

        var request = new ScheduledJobCreateRequest
        {
            Id = ReadString(root, "id"),
            Name = name,
            PayloadMessage = message,
            ScheduleKind = runAtUtc.HasValue ? ScheduledJobScheduleKind.At : ScheduledJobScheduleKind.Cron,
            RunAtUtc = runAtUtc,
            CronExpression = runAtUtc.HasValue ? null : cron,
            TimeZoneId = ReadString(root, "timezone") ?? "UTC",
            Enabled = ReadBool(root, "enabled") ?? true,
            AgentId = ReadString(root, "agentId"),
            SessionKey = ReadString(root, "sessionKey"),
            SessionTarget = ReadString(root, "sessionTarget"),
            WakeMode = ReadString(root, "wakeMode"),
            DeliveryChannel = ReadString(root, "channel"),
            DeliveryRecipient = ReadString(root, "recipient"),
            DeliveryMode = ReadString(root, "deliveryMode"),
            Scope = ParseScope(ReadString(root, "scope")),
            ScopeReason = ReadString(root, "scopeReason"),
            ExecutionTimeoutSeconds = ReadInt(root, "executionTimeoutSeconds"),
            OverlapPolicy = ParseOverlapPolicy(ReadString(root, "overlapPolicy"))
        };

        if (request.ScheduleKind == ScheduledJobScheduleKind.Cron && string.IsNullOrWhiteSpace(request.CronExpression))
            throw new InvalidOperationException("Create job requires either 'cron' or 'runAtUtc'.");

        var created = await _jobManager.CreateAsync(request, actor, ct);
        return Serialize(new
        {
            message = $"Created job '{created.Definition.Id}'.",
            job = created
        });
    }

    private async Task<string> GetJobAsync(JsonElement root, ScheduledJobActor actor, CancellationToken ct)
    {
        var id = RequireString(root, "jobId");
        var job = await _jobManager.GetAsync(id, actor, ct);
        if (job is null)
            return Serialize(new { message = $"Job '{id}' not found or not visible." });

        return Serialize(new { job });
    }

    private async Task<string> ListJobsAsync(JsonElement root, ScheduledJobActor actor, CancellationToken ct)
    {
        var list = await _jobManager.ListAsync(new ScheduledJobListOptions
        {
            IncludeDisabled = ReadBool(root, "includeDisabled") ?? true,
            IncludeAllJobs = ReadBool(root, "includeAll") ?? false
        }, actor, ct);

        return Serialize(new
        {
            count = list.Count,
            jobs = list
        });
    }

    private async Task<string> UpdateJobAsync(JsonElement root, ScheduledJobActor actor, CancellationToken ct)
    {
        var id = RequireString(root, "jobId");
        var runAtUtc = ReadDateTimeOffset(root, "runAtUtc");
        var update = new ScheduledJobUpdateRequest
        {
            Name = ReadString(root, "name"),
            Enabled = ReadBool(root, "enabled"),
            ScheduleKind = ParseScheduleKind(ReadString(root, "scheduleKind")),
            CronExpression = ReadString(root, "cron"),
            RunAtUtc = runAtUtc,
            TimeZoneId = ReadString(root, "timezone"),
            ExecutionTimeoutSeconds = ReadInt(root, "executionTimeoutSeconds"),
            OverlapPolicy = ParseOverlapPolicy(ReadString(root, "overlapPolicy")),
            AgentId = ReadString(root, "agentId"),
            SessionKey = ReadString(root, "sessionKey"),
            SessionTarget = ReadString(root, "sessionTarget"),
            WakeMode = ReadString(root, "wakeMode"),
            PayloadMessage = ReadString(root, "message"),
            DeliveryChannel = ReadString(root, "channel"),
            DeliveryRecipient = ReadString(root, "recipient"),
            DeliveryMode = ReadString(root, "deliveryMode"),
            Scope = ParseScope(ReadString(root, "scope")),
            ScopeReason = ReadString(root, "scopeReason")
        };

        var updated = await _jobManager.UpdateAsync(id, update, actor, ct);
        return Serialize(new
        {
            message = $"Updated job '{updated.Definition.Id}'.",
            job = updated
        });
    }

    private async Task<string> DeleteJobAsync(JsonElement root, ScheduledJobActor actor, CancellationToken ct)
    {
        var id = RequireString(root, "jobId");
        await _jobManager.DeleteAsync(id, actor, ct);
        return Serialize(new { message = $"Deleted job '{id}'." });
    }

    private async Task<string> SetEnabledAsync(JsonElement root, ScheduledJobActor actor, bool enabled, CancellationToken ct)
    {
        var id = RequireString(root, "jobId");
        var updated = await _jobManager.SetEnabledAsync(id, enabled, actor, ct);
        return Serialize(new
        {
            message = $"{(enabled ? "Enabled" : "Disabled")} job '{id}'.",
            job = updated
        });
    }

    private async Task<string> TriggerJobAsync(JsonElement root, ScheduledJobActor actor, CancellationToken ct)
    {
        var id = RequireString(root, "jobId");
        var triggered = await _jobManager.TriggerAsync(id, actor, ct);
        return Serialize(new
        {
            message = $"Triggered job '{id}'.",
            job = triggered
        });
    }

    private static string Serialize(object value) => JsonSerializer.Serialize(value, JsonOptions);

    private static string RequireString(JsonElement root, string propertyName)
    {
        var value = ReadString(root, propertyName);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"'{propertyName}' is required.");
        return value;
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
            return null;
        return value.GetString()?.Trim();
    }

    private static bool? ReadBool(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static int? ReadInt(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Number)
            return null;
        return value.TryGetInt32(out var parsed) ? parsed : null;
    }

    private static DateTimeOffset? ReadDateTimeOffset(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
            return null;

        var raw = value.GetString();
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (!DateTimeOffset.TryParse(raw, out var parsed))
            throw new InvalidOperationException($"'{propertyName}' must be a valid ISO datetime value.");

        return parsed.ToUniversalTime();
    }

    private static ScheduledJobScope? ParseScope(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
            return null;

        return scope.ToLowerInvariant() switch
        {
            "scoped" => ScheduledJobScope.Scoped,
            "global" => ScheduledJobScope.Global,
            _ => throw new InvalidOperationException("scope must be either 'scoped' or 'global'.")
        };
    }

    private static ScheduledJobOverlapPolicy? ParseOverlapPolicy(string? policy)
    {
        if (string.IsNullOrWhiteSpace(policy))
            return null;

        return policy.ToLowerInvariant() switch
        {
            "skip" => ScheduledJobOverlapPolicy.Skip,
            "concurrent" => ScheduledJobOverlapPolicy.Concurrent,
            _ => throw new InvalidOperationException("overlapPolicy must be either 'skip' or 'concurrent'.")
        };
    }

    private static ScheduledJobScheduleKind? ParseScheduleKind(string? scheduleKind)
    {
        if (string.IsNullOrWhiteSpace(scheduleKind))
            return null;

        return scheduleKind.ToLowerInvariant() switch
        {
            "cron" => ScheduledJobScheduleKind.Cron,
            "at" => ScheduledJobScheduleKind.At,
            _ => throw new InvalidOperationException("scheduleKind must be either 'cron' or 'at'.")
        };
    }

    private const string CreateSchema = """
        {
          "type": "object",
          "properties": {
            "name": { "type": "string" },
            "message": { "type": "string" },
            "id": { "type": "string" },
            "cron": { "type": "string", "description": "Cron expression when scheduleKind=cron" },
            "runAtUtc": { "type": "string", "description": "One-time ISO timestamp when scheduleKind=at" },
            "timezone": { "type": "string", "description": "IANA timezone for cron evaluation" },
            "enabled": { "type": "boolean" },
            "agentId": { "type": "string" },
            "sessionKey": { "type": "string" },
            "sessionTarget": { "type": "string" },
            "wakeMode": { "type": "string" },
            "channel": { "type": "string" },
            "recipient": { "type": "string" },
            "deliveryMode": { "type": "string" },
            "scope": { "type": "string", "enum": ["scoped", "global"] },
            "scopeReason": { "type": "string" },
            "executionTimeoutSeconds": { "type": "number" },
            "overlapPolicy": { "type": "string", "enum": ["skip", "concurrent"] }
          },
          "required": ["name", "message"]
        }
        """;

    private const string GetSchema = """
        {
          "type": "object",
          "properties": {
            "jobId": { "type": "string" }
          },
          "required": ["jobId"]
        }
        """;

    private const string ListSchema = """
        {
          "type": "object",
          "properties": {
            "includeDisabled": { "type": "boolean" },
            "includeAll": { "type": "boolean", "description": "Admin only" }
          }
        }
        """;

    private const string UpdateSchema = """
        {
          "type": "object",
          "properties": {
            "jobId": { "type": "string" },
            "name": { "type": "string" },
            "enabled": { "type": "boolean" },
            "scheduleKind": { "type": "string", "enum": ["cron", "at"] },
            "cron": { "type": "string" },
            "runAtUtc": { "type": "string" },
            "timezone": { "type": "string" },
            "agentId": { "type": "string" },
            "sessionKey": { "type": "string" },
            "sessionTarget": { "type": "string" },
            "wakeMode": { "type": "string" },
            "message": { "type": "string" },
            "channel": { "type": "string" },
            "recipient": { "type": "string" },
            "deliveryMode": { "type": "string" },
            "scope": { "type": "string", "enum": ["scoped", "global"] },
            "scopeReason": { "type": "string" },
            "executionTimeoutSeconds": { "type": "number" },
            "overlapPolicy": { "type": "string", "enum": ["skip", "concurrent"] }
          },
          "required": ["jobId"]
        }
        """;

    private const string DeleteSchema = """
        {
          "type": "object",
          "properties": {
            "jobId": { "type": "string" }
          },
          "required": ["jobId"]
        }
        """;

    private const string ToggleSchema = """
        {
          "type": "object",
          "properties": {
            "jobId": { "type": "string" }
          },
          "required": ["jobId"]
        }
        """;

    private const string TriggerSchema = """
        {
          "type": "object",
          "properties": {
            "jobId": { "type": "string" }
          },
          "required": ["jobId"]
        }
        """;
}
