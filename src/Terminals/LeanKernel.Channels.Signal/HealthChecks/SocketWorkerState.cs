namespace LeanKernel.Channels.Signal.HealthChecks;

/// <summary>
/// The lifecycle state of an account socket worker.
/// </summary>
public enum SocketWorkerState
{
    /// <summary>
    /// The worker has been created but has not yet completed its first loop iteration.
    /// </summary>
    Starting,

    /// <summary>
    /// The worker is actively progressing or connected.
    /// </summary>
    Running,

    /// <summary>
    /// The worker accumulated consecutive errors beyond the unhealthy threshold.
    /// </summary>
    Faulted
}
