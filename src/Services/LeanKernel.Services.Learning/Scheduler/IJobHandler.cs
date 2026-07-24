namespace LeanKernel.Services.Learning.Scheduler;

/// <summary>
/// Handles execution of a scheduled job.
/// </summary>
public interface IJobHandler
{
    /// <summary>
    /// Executes the job handler logic for the given job.
    /// </summary>
    /// <param name="jobId">The unique identifier of the job.</param>
    /// <param name="configurationJson">The JSON configuration for the job.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    Task ExecuteAsync(Guid jobId, string configurationJson, CancellationToken ct = default);
}