# PRD: Deterministic TypingIndicatorKeepAlive Tests

## Context
`TypingIndicatorKeepAliveTests` currently use `Task.Delay(...)` to wait for async work before assertions. This introduces timing flakiness in CI and under load.

## Problem
Delay-based synchronization is non-deterministic for fire-and-forget/background work. Assertions like typing-start counters can race and fail intermittently.

## Goals
- Replace time-based waits with explicit synchronization in tests.
- Preserve existing test intent and coverage.
- Keep test-only changes isolated to unit test code.

## Reviewed Plan
1. Add a one-shot start signal in `TestChannel` using `TaskCompletionSource<bool>` with `RunContinuationsAsynchronously`.
2. Signal at the beginning of `StartTypingAsync`, before cancellation/exception branches.
3. Add a helper `WaitForStartTypingAsync(TimeSpan timeout, CancellationToken ct = default)` so tests can deterministically await first start invocation.
4. Replace `Task.Delay(...)` calls in affected tests with the helper.
5. Make typing counters thread-safe via `Interlocked.Increment` and `Volatile.Read`.
6. Run targeted unit tests for `TypingIndicatorKeepAliveTests`.

## Risks
- Multiple start invocations from keepalive loop: mitigated with `TrySetResult` on TCS.
- Timeout tuning: use a bounded timeout (1s) and explicit assertion messages.

## Acceptance Criteria
- No `Task.Delay(...)` remains in `TypingIndicatorKeepAliveTests` for start-wait synchronization.
- Tests assert readiness using explicit channel signaling.
- `TypingIndicatorKeepAliveTests` pass consistently in local targeted runs.
