namespace LeanKernel.Abstractions.Interfaces;

/// <summary>
/// Coordinates serialized turn execution and preemption by session.
/// </summary>
public interface ISessionTurnCoordinator
{
    /// <summary>
    /// Begins a turn for the specified session and acquires the session lock.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A lease that must be disposed when turn processing completes.</returns>
    ValueTask<ITurnLease> BeginTurnAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Signals that a new inbound message has arrived for the session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    void NotifyInbound(string sessionId);
}

/// <summary>
/// Represents an active turn lease.
/// </summary>
public interface ITurnLease : IAsyncDisposable
{
    /// <summary>
    /// Gets a value indicating whether preemption was requested by a queued inbound message.
    /// </summary>
    bool PreemptionRequested { get; }
}
