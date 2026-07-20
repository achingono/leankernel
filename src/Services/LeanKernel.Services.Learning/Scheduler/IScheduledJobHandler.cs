using System.Text.Json;

using LeanKernel.Services.Common.Scheduler;

namespace LeanKernel.Services.Learning.Scheduler;

/// <summary>
/// Handles execution for one scheduled job type.
/// </summary>
public interface IScheduledJobHandler
{
    /// <summary>
    /// Gets the job type this handler supports.
    /// </summary>
    string JobType { get; }

    /// <summary>
    /// Executes a scheduled job with an optional JSON payload.
    /// </summary>
    Task ExecuteAsync(ScheduledJobDefinition job, JsonElement? payload, CancellationToken cancellationToken = default);
}
