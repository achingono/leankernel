namespace LeanKernel.Channels.Signal.HealthChecks;

/// <summary>
/// Immutable snapshot of per-account socket worker health used by the health check pipeline.
/// </summary>
/// <param name="Account">The configured account number.</param>
/// <param name="State">The current worker lifecycle state.</param>
/// <param name="LastSuccessfulReceiveUtc">The last time a valid inbound message was enqueued, or <c>null</c> when none was ever received.</param>
/// <param name="LastWorkerLoopTickUtc">The last time the worker loop iterated, or <c>null</c> before the first iteration.</param>
/// <param name="StartedUtc">The time the worker was created.</param>
/// <param name="ConsecutiveErrors">The current consecutive error count.</param>
/// <param name="LastErrorUtc">The last time a worker error occurred, or <c>null</c> when no error occurred.</param>
public sealed record SocketWorkerHealthState(
    string Account,
    SocketWorkerState State,
    DateTime? LastSuccessfulReceiveUtc,
    DateTime? LastWorkerLoopTickUtc,
    DateTime StartedUtc,
    int ConsecutiveErrors,
    DateTime? LastErrorUtc);
