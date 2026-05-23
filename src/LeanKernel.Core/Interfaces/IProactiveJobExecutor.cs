using LeanKernel.Core.Models;

namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Executes scheduled proactive jobs by invoking reasoning and delivery pipelines.
/// </summary>
public interface IProactiveJobExecutor
{
    /// <summary>
    /// Executes a scheduled job payload.
    /// </summary>
    Task<ScheduledJobExecutionResult> ExecuteAsync(ScheduledJobDefinition job, CancellationToken ct);
}
