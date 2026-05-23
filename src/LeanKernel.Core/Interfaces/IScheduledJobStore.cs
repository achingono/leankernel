using LeanKernel.Core.Models;

namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Durable storage contract for scheduled job definitions and execution state.
/// </summary>
public interface IScheduledJobStore
{
    /// <summary>
    /// Loads persisted scheduler snapshot.
    /// </summary>
    Task<ScheduledJobStoreSnapshot> LoadAsync(CancellationToken ct);

    /// <summary>
    /// Persists scheduler snapshot atomically.
    /// </summary>
    Task SaveAsync(ScheduledJobStoreSnapshot snapshot, CancellationToken ct);
}
