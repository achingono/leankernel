using System.Collections.Concurrent;
using LeanKernel.Abstractions.Interfaces;

namespace LeanKernel.Agents;

/// <summary>
/// Serializes turn execution per session and exposes preemption signaling.
/// </summary>
public sealed class SessionTurnCoordinator(TimeProvider? timeProvider = null) : ISessionTurnCoordinator
{
    private static readonly TimeSpan CleanupAfterIdle = TimeSpan.FromMinutes(10);

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, SessionState> _sessions = new(StringComparer.Ordinal);

    public async ValueTask<ITurnLease> BeginTurnAsync(string sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var state = _sessions.GetOrAdd(sessionId, _ => new SessionState());
        state.Touch(_timeProvider.GetUtcNow());
        await state.Semaphore.WaitAsync(ct).ConfigureAwait(false);

        var lease = new TurnLease(sessionId, state, this);
        state.SetActiveLease(lease);
        return lease;
    }

    public void NotifyInbound(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (_sessions.TryGetValue(sessionId, out var state))
        {
            state.Touch(_timeProvider.GetUtcNow());
            state.RequestPreemption();
        }
    }

    private void CompleteLease(string sessionId, SessionState state, TurnLease lease)
    {
        state.ClearActiveLease(lease);
        state.Semaphore.Release();
        state.Touch(_timeProvider.GetUtcNow());
        CleanupIdleSessions();
    }

    private void CleanupIdleSessions()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var (sessionId, state) in _sessions)
        {
            if (!state.IsIdle(now, CleanupAfterIdle))
            {
                continue;
            }

            _sessions.TryRemove(sessionId, out _);
        }
    }

    private sealed class TurnLease(string sessionId, SessionState state, SessionTurnCoordinator owner) : ITurnLease
    {
        private int _disposed;

        public bool PreemptionRequested => Volatile.Read(ref state.PreemptionRequested) == 1;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return ValueTask.CompletedTask;
            }

            owner.CompleteLease(sessionId, state, this);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SessionState
    {
        private readonly object _gate = new();
        private TurnLease? _activeLease;

        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int PreemptionRequested;

        public DateTimeOffset LastTouched { get; private set; } = DateTimeOffset.UtcNow;

        public void Touch(DateTimeOffset now)
            => LastTouched = now;

        public void SetActiveLease(TurnLease lease)
        {
            lock (_gate)
            {
                _activeLease = lease;
                Volatile.Write(ref PreemptionRequested, 0);
            }
        }

        public void RequestPreemption()
        {
            lock (_gate)
            {
                if (_activeLease is not null)
                {
                    Volatile.Write(ref PreemptionRequested, 1);
                }
            }
        }

        public void ClearActiveLease(TurnLease lease)
        {
            lock (_gate)
            {
                if (ReferenceEquals(_activeLease, lease))
                {
                    _activeLease = null;
                }

                Volatile.Write(ref PreemptionRequested, 0);
            }
        }

        public bool IsIdle(DateTimeOffset now, TimeSpan idleThreshold)
        {
            lock (_gate)
            {
                if (_activeLease is not null)
                {
                    return false;
                }

                if (Semaphore.CurrentCount != 1)
                {
                    return false;
                }

                return now - LastTouched >= idleThreshold;
            }
        }
    }
}
