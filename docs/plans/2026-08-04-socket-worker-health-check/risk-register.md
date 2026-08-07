# Phase 01 Risk Register

## Risks

| ID | Risk | Impact | Mitigation | Status |
|---|---|---|---|---|
| R1 | Progress tracking adds overhead to hot worker loop | Medium | Use lock-free `Interlocked.Exchange` for primitive timestamp fields (stored as `long` ticks); avoid allocations in loop | Closed |
| R2 | Health check reports false positives during worker startup/warmup | High | `ISocketWorkerHealthProvider` exposes `IsInitialDiscoveryCompleted`; health check returns Healthy during initial warmup | Closed |
| R3 | Clock skew between health check and worker timestamps | Low | Use `DateTime.UtcNow` consistently; thresholds are seconds-scale, skew typically <1s | Closed |
| R4 | `SocketTransportClient` disposed but health check still called | Medium | Health check reads snapshot under lock; disposed client returns empty states → Healthy (no accounts) | Closed |
| R5 | Configuration thresholds too aggressive/conservative for production | Medium | Default `WorkerDegradedThresholdSeconds` to 60s (3x receive timeout); make overridable via config; validate thresholds at startup | Closed |
| R6 | Concurrent modification of tracking dictionary during health check snapshot | High | Take snapshot under same `_sync` lock used by worker management; return immutable `record` copies | Closed |
| R7 | Health check blocks worker loop if lock contention | Low | Health check holds lock briefly (dict copy only); worker loop uses lock-free `Interlocked` for timestamps | Closed |
| R8 | Active account masks a dead/faulted secondary account in multi-account deployments | Critical | Use worst-case account evaluation rule: health check returns Degraded/Unhealthy if ANY account worker is stalled or faulted | Closed |
| R9 | `_initialDiscoveryCompleted` set to `true` on failed discovery masks startup errors | Medium | Only set `_initialDiscoveryCompleted = true` after successful `DiscoverConfiguredAccountsAsync` call | Closed |
| R10 | `Stopped` state triggers false Unhealthy for intentionally deprovisioned accounts | Medium | Remove deprovisioned workers from tracking dictionary entirely; `Stopped` is not a monitored state | Closed |
| R11 | Invalid config where `UnhealthyThreshold <= DegradedThreshold` or `<= ReceiveClientDeadline` produces nonsensical results | Low | Validate options at startup (`AddOptions<SignalSettings>().Validate(...)`); throw options exception | Closed |
| R12 | Manager background task (`ManageWorkersAsync`) crashes, clearing workers and causing health check to falsely report Healthy | Critical | Expose `IsManagerRunning` on `ISocketWorkerHealthProvider`; report Unhealthy if transport is started but manager task stopped | Closed |
| R13 | Account discovery REST API glitch returns empty list, tearing down active workers | High | Return `(bool Success, Accounts)` from `DiscoverConfiguredAccountsAsync`; preserve workers and skip update on failure | Closed |
| R14 | Worker thread deadlocks during setup and stays in `Starting` state indefinitely | High | Monitor `StartedUtc` in `Starting` state; escalate to Degraded/Unhealthy if startup exceeds `WorkerDegradedThresholdSeconds` | Closed |
| R15 | C# `Interlocked.Exchange` fails on C# `record` fields due to `ref` constraints | Medium | Use private mutable `AccountWorkerHealthTracker` class with `long`/`int` fields for atomic updates, projecting to `record` snapshot | Closed |

## Open Decisions
- Should `SocketWorkerHealthCheck` be a separate class or a method on `SocketTransportClient`? (Decision: separate class for separation of concerns and testability)
- Should we expose `ISocketWorkerHealthProvider` publicly or keep internal? (Decision: internal to signal-channel project, registered in DI)
- Do we need a separate "degraded" constant in `Constants.Healthchecks` or reuse existing? (Decision: use existing health check status enums, no new constant needed)
- Should health check include `lastSuccessfulSignalApiProbeUtc` as requested? (Decision: Dropped — `lastWorkerLoopTickUtc` serves as the primary liveness signal; adding a REST API probe timestamp is redundant with the existing `SignalApiHealthCheck`)