using LeanKernel.Core.Models;

namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Management surface for durable scheduled job CRUD and execution control.
/// </summary>
public interface IScheduledJobManager
{
    /// <summary>
    /// Loads persisted jobs and initializes runtime state.
    /// </summary>
    Task InitializeAsync(CancellationToken ct);

    /// <summary>
    /// Lists scheduled jobs visible to the supplied actor.
    /// </summary>
    Task<IReadOnlyList<ScheduledJobView>> ListAsync(
        ScheduledJobListOptions options,
        ScheduledJobActor actor,
        CancellationToken ct);

    /// <summary>
    /// Gets a scheduled job by id when visible to the actor.
    /// </summary>
    Task<ScheduledJobView?> GetAsync(string jobId, ScheduledJobActor actor, CancellationToken ct);

    /// <summary>
    /// Creates a new scheduled job.
    /// </summary>
    Task<ScheduledJobView> CreateAsync(
        ScheduledJobCreateRequest request,
        ScheduledJobActor actor,
        CancellationToken ct);

    /// <summary>
    /// Updates an existing scheduled job.
    /// </summary>
    Task<ScheduledJobView> UpdateAsync(
        string jobId,
        ScheduledJobUpdateRequest request,
        ScheduledJobActor actor,
        CancellationToken ct);

    /// <summary>
    /// Deletes an existing scheduled job.
    /// </summary>
    Task DeleteAsync(string jobId, ScheduledJobActor actor, CancellationToken ct);

    /// <summary>
    /// Enables or disables an existing scheduled job.
    /// </summary>
    Task<ScheduledJobView> SetEnabledAsync(
        string jobId,
        bool enabled,
        ScheduledJobActor actor,
        CancellationToken ct);

    /// <summary>
    /// Triggers immediate execution of an existing scheduled job.
    /// </summary>
    Task<ScheduledJobView> TriggerAsync(string jobId, ScheduledJobActor actor, CancellationToken ct);

    /// <summary>
    /// Executes any due scheduled jobs.
    /// </summary>
    Task ProcessDueJobsAsync(CancellationToken ct);
}
