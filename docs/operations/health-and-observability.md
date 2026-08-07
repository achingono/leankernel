# Health and Observability

The current implementation has a small but useful operational surface.

## Health Endpoint

The gateway exposes:

- `GET /health`

This endpoint is used both for manual checks and for the Docker Compose health check.

Reference: [`../../src/Services/LeanKernel.Services.Gateway/Program.cs`](../../src/Services/LeanKernel.Services.Gateway/Program.cs)

## Signal Terminal Health

The Signal terminal process exposes `GET /health` with `database`, `gateway`, `signal-api`, and
`socket-worker` checks. The `socket-worker` check detects stuck, faulted, or dead per-account
receive workers and worker manager crashes that the component-reachability checks cannot catch.

Operational guidance:

- When `socket-worker` reports `Degraded`, an account worker has stalled past
  `Signal:WorkerDegradedThresholdSeconds` (default 60s) or accumulated
  `WorkerConsecutiveErrorThreshold` (default 3) consecutive errors.
- When it reports `Unhealthy`, a worker is `Faulted`, stalled past
  `WorkerUnhealthyThresholdSeconds` (default 180s), stuck in `Starting`, or the worker manager
  task has crashed.
- The check result `data` payload lists each account's `state`, progress timestamps, and
  `consecutiveErrors`, plus `transportStarted`, `managerRunning`, and
  `initialDiscoveryCompleted` flags.
- Configure the thresholds under the `Signal` section; startup validation rejects
  `WorkerUnhealthyThresholdSeconds <= WorkerDegradedThresholdSeconds`,
  `WorkerDegradedThresholdSeconds <= (EffectiveReceiveDeadlineSeconds + ReconnectDelaySeconds)`,
  `WorkerUnhealthyErrorThreshold <= WorkerConsecutiveErrorThreshold`, and
  `WorkerConsecutiveErrorThreshold < 1`.
- If the terminal is newly started and account discovery is still in flight, the check reports
  Healthy within startup grace (`max(30s, 3 x AccountRefreshSeconds)`) and then Degraded if
  discovery still has not completed.

Full evaluation rules: [Health checks](../features/health-checks.md).

## Integration Coverage

Integration and Playwright tests exercise the gateway endpoints under `test/`.

Relevant projects:

- [`../../test/LeanKernel.Tests.Integration`](../../test/LeanKernel.Tests.Integration)
- [`../../test/LeanKernel.Tests.Playwright`](../../test/LeanKernel.Tests.Playwright)

## Quality Tooling

Operational quality scripts live under `scripts/quality/` and cover:

- test coverage
- SonarQube scan orchestration
- SonarQube result summaries
- quality-gate polling (scanner waits for gate result)

## Current Observability Boundary

The runtime does not yet expose a separate diagnostics service or rich production telemetry surface. The current observability story is centered on:

- health checks
- build and test workflows
- direct code-level inspection of persisted state and provider behavior
