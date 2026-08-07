# Phase 01 Socket Worker Health Check

## Companion Documents
- [Inputs](inputs.md)
- [Activities](activities.md)
- [Outputs](outputs.md)
- [Exit Criteria](exit-criteria.md)
- [Risk Register](risk-register.md)
- [Evidence](evidence.md)

## Objective
Add a dedicated health check for the Signal socket worker loop (`SocketTransportClient`) that tracks per-account progress (last successful receive, worker loop tick, consecutive errors) and transport manager state, evaluating health based on worst-case worker and manager status. Reports Degraded or Unhealthy when any account worker stalls beyond configured thresholds, accumulates consecutive errors, enters a faulted state, stays stuck in `Starting` state past startup timeout, or when the worker manager task itself crashes or fails account discovery. This enables operators to detect stuck, crashed, or dead socket workers that existing health checks (database, gateway, signal-api) cannot catch.

## Scope

### In Scope
- Add progress-tracking fields to `SocketTransportClient` via a private mutable tracking class (`AccountWorkerHealthTracker`), maintaining per-account worker metrics (last successful receive, last loop tick, consecutive errors, worker state, start timestamp).
- Instrument worker manager loop (`ManageWorkersAsync`) and account discovery (`DiscoverConfiguredAccountsAsync`) to report manager health and prevent false positives on discovery HTTP failures.
- Expose worker and manager state via a new `ISocketWorkerHealthProvider` interface implemented by `SocketTransportClient`.
- Create `SocketWorkerHealthCheck` implementing `IHealthCheck` that evaluates worker progress, startup timeouts, consecutive errors, and manager task status.
- Add options validation for `SignalSettings` enforcing threshold bounds (`WorkerUnhealthyThresholdSeconds > WorkerDegradedThresholdSeconds > ReceiveClientDeadlineSeconds`).
- Register the health check and options validation in `Program.cs` alongside existing health checks.
- Unit tests for the health check logic, progress tracking, startup timeout handling, manager failure detection, and options validation.

### Out of Scope
- Changes to signal-cli or external Signal API
- Health checks for other transports (Teams, etc.)
- Metrics/observability exports beyond health check endpoint
- Auto-recovery/restart of faulted workers (health check only reports)

## Entry Criteria
- Current `SocketTransportClient` implementation is stable and understood
- Existing health checks (database, gateway, signal-api) are passing in CI
- `SignalSettings` configuration class exists and is used for transport settings

## Exit Criteria
- New health check endpoint returns worker and manager state details for each account
- Health check fails (Degraded then Unhealthy) when workers stall per configured thresholds or exceed error limits
- Worker manager loop crashes or unexpected terminations report `Unhealthy`
- Deprovisioned account workers are removed from tracking (no false `Unhealthy` for removed accounts)
- Workers stuck in `Starting` state past `WorkerDegradedThresholdSeconds` transition to `Degraded` / `Unhealthy`
- Configuration validation enforces `WorkerUnhealthyThresholdSeconds > WorkerDegradedThresholdSeconds > ReceiveClientDeadlineSeconds`
- Code coverage ≥ 80% for new health check, provider, and settings validation
- SonarQube scan passes with no Blocker/Critical/Major issues
- Deep review completed and issues addressed

## Roles
- Owner: [Assignee]
- Reviewer: [TBD]
- Approver: [TBD]