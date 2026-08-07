namespace LeanKernel.Channels.Signal.HealthChecks;

/// <summary>
/// Exposes account socket worker and manager health state to the health check pipeline.
/// </summary>
public interface ISocketWorkerHealthProvider
{
    /// <summary>
    /// Returns an immutable snapshot of per-account worker health state.
    /// </summary>
    /// <returns>A dictionary keyed by account number.</returns>
    IReadOnlyDictionary<string, SocketWorkerHealthState> GetWorkerStates();

    /// <summary>
    /// Gets a value indicating whether the initial account discovery completed successfully.
    /// </summary>
    bool IsInitialDiscoveryCompleted { get; }

    /// <summary>
    /// Gets the UTC timestamp when the transport start sequence began.
    /// </summary>
    DateTime StartedUtc { get; }

    /// <summary>
    /// Returns a manager lifecycle snapshot captured atomically.
    /// </summary>
    /// <returns>
    /// A tuple containing whether the transport is started and whether the manager task is currently running.
    /// </returns>
    (bool Started, bool Running) GetManagerState();
}
