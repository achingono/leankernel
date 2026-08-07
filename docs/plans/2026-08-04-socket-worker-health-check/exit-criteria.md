# Phase 01 Exit Criteria

## Gate Checklist
- [x] `SignalSettings` includes `WorkerDegradedThresholdSeconds`, `WorkerUnhealthyThresholdSeconds`, `WorkerConsecutiveErrorThreshold`, `WorkerUnhealthyErrorThreshold`, `EnableWorkerHealthCheck` with sensible defaults
- [x] Options validation enforces `WorkerUnhealthyThresholdSeconds > WorkerDegradedThresholdSeconds > ReceiveClientDeadlineSeconds` and `WorkerUnhealthyErrorThreshold > WorkerConsecutiveErrorThreshold`
- [x] `SocketWorkerHealthState` is an immutable `record` capturing all required per-account fields (`StartedUtc`, `LastSuccessfulReceiveUtc`, `LastWorkerLoopTickUtc`, `ConsecutiveErrors`, `LastErrorUtc`, `State`)
- [x] `ISocketWorkerHealthProvider` interface defined with `GetWorkerStates()`, `IsInitialDiscoveryCompleted`, and `IsManagerRunning`, implemented by `SocketTransportClient`
- [x] `SocketTransportClient` tracks per-account metrics thread-safely via internal `AccountWorkerHealthTracker` class using `Interlocked` / `Volatile`
- [x] `_initialDiscoveryCompleted` set to `true` only after successful `DiscoverConfiguredAccountsAsync` call; discovery HTTP errors do not tear down active workers or set discovery completed flag
- [x] Deprovisioned account workers are removed from tracking dictionary upon removal (no false alerts for intentional deprovisioning)
- [x] Manager task failure (`IsManagerRunning == false` when transport started) returns `Unhealthy`
- [x] `SocketWorkerHealthCheck` evaluates progress correctly based on worst-case account state:
  - Feature flag disabled or initial discovery in progress → Healthy
  - No configured accounts → Healthy
  - Worker stuck in `Starting` state past startup timeout (`WorkerDegradedThresholdSeconds`) → Degraded / Unhealthy
  - ALL configured account workers progressing within `WorkerDegradedThresholdSeconds` → Healthy
  - ANY worker stalled beyond `WorkerDegradedThresholdSeconds` or `consecutiveErrors >= WorkerConsecutiveErrorThreshold` → Degraded
  - ANY worker stalled beyond `WorkerUnhealthyThresholdSeconds`, in `Faulted` state, or `consecutiveErrors >= WorkerUnhealthyErrorThreshold` → Unhealthy
- [x] Health check and options validation registered in `Program.cs` with constant `Constants.Healthchecks.SocketWorker`
- [x] Unit tests cover all health check decision paths, progress tracking behavior, startup timeout, manager task crash detection, discovery error tolerance, and config validation
- [x] Code coverage ≥ 80% for new/modified files (repo 81.12%; `ToolDefinitionAIToolAdapter.cs` 100%)
- [x] `scripts/quality/sonarqube-scan.sh` passes with zero Blocker, Critical, Major issues (gate PASSED 2026-08-07)
- [x] Deep review sub-agent executed and all reported issues addressed
- [x] Documentation updated to reflect new health check

## Approval Table

| Role | Name | Status | Notes |
|---|---|---|---|
| Owner | | Pending | |
| Reviewer | | Pending | |
| Approver | | Pending | |