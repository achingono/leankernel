using System.Text.Json;

using LeanKernel.Services.Common.Scheduler;

namespace LeanKernel.Services.Learning.Scheduler;

/// <summary>
/// Emits scheduler heartbeat log events.
/// </summary>
/// <param name="logger">Logger used for heartbeat messages.</param>
public sealed class PingScheduledJobHandler(ILogger<PingScheduledJobHandler> logger) : IScheduledJobHandler
{
    /// <inheritdoc />
    public string JobType => ScheduledJobTypes.LearningPing;

    /// <inheritdoc />
    public Task ExecuteAsync(ScheduledJobDefinition job, JsonElement? payload, CancellationToken cancellationToken = default)
    {
        var message = payload.HasValue
            && payload.Value.ValueKind == JsonValueKind.Object
            && payload.Value.TryGetProperty("message", out var messageElement)
            && messageElement.ValueKind == JsonValueKind.String
                ? messageElement.GetString()
                : null;

        logger.LogInformation(
            "Learning scheduler heartbeat for job {JobName}. {Message}",
            job.Name,
            string.IsNullOrWhiteSpace(message) ? string.Empty : message);

        return Task.CompletedTask;
    }
}
