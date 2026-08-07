# Health Checks

The runtime exposes health checks that let operators detect stuck, crashed, or unreachable components.

## Signal Channel

The Signal terminal process exposes `GET /health` on the terminal port. The registered checks are:

| Check | Name | Detects |
|---|---|---|
| Database | `database` | EF Core database connectivity |
| Gateway | `gateway` | Reachability of the LeanKernel gateway HTTP endpoint |
| Signal API | `signal-api` | Reachability of the signal-cli REST sidecar (`/v1/about`) |
| Socket worker | `socket-worker` | Stuck, faulted, or dead per-account socket workers and worker manager crashes |

### Socket Worker Health Check

The `socket-worker` check monitors the `SocketTransportClient` worker loops that consume the
signal-cli WebSocket receive endpoint. It cannot be replaced by the other checks: the database,
gateway, and signal-api checks only prove those components are reachable — they say nothing about
whether the per-account receive workers are still making progress.

Per-account state is tracked with `Interlocked`/`Volatile` primitive fields in a private mutable
tracker class and projected into immutable `SocketWorkerHealthState` records via
`ISocketWorkerHealthProvider`:

- `StartedUtc` — when the worker was created
- `State` — `Starting`, `Running`, or `Faulted`
- `LastWorkerLoopTickUtc` — liveness heartbeat; updated every loop iteration
- `LastSuccessfulReceiveUtc` — updated when a valid inbound message is enqueued (`null` for idle accounts)
- `ConsecutiveErrors` / `LastErrorUtc` — accumulated loop failures

Evaluation uses the **worst-case account** rule: if ANY account worker is stalled, faulted, or
startup-stuck, the whole check reports Degraded or Unhealthy. Status transitions:

- Feature flag disabled or no accounts configured → Healthy
- Worker manager task not running while the transport is started → Unhealthy
- Initial account discovery in progress within startup grace (`max(30s, 3 x AccountRefreshSeconds)`) → Healthy
- Initial account discovery in progress beyond startup grace → Degraded
- Any worker `Faulted`, `consecutiveErrors >= WorkerUnhealthyErrorThreshold`, stalled past
  `WorkerUnhealthyThresholdSeconds`, or stuck in `Starting` past the unhealthy timeout → Unhealthy
- Any worker stalled past `WorkerDegradedThresholdSeconds`, stuck in `Starting` past the degraded
  timeout, or `consecutiveErrors >= WorkerConsecutiveErrorThreshold` → Degraded

Per-account state and manager liveness are included in the check result `data` payload for
operational diagnostics.

Discovery behavior notes:

- The `socket-worker` check reports Healthy while the first account discovery is still in flight,
  and only marks discovery completed after a successful `/v1/accounts` response, avoiding false
  Unhealthy during startup warm-up.
- A failed or non-2xx account discovery refreshes nothing: existing workers are preserved rather
  than torn down, so transient signal-cli REST glitches do not take down active connections.
- A single empty `/v1/accounts` result while workers are active is treated as transient and
  preserves workers; two consecutive empty refreshes apply full deprovisioning.
- Deprovisioned accounts are removed from tracking entirely, so intentional deprovisioning does
  not report stale Unhealthy.

Configuration is enforced at startup by options validation:

- `WorkerUnhealthyThresholdSeconds > WorkerDegradedThresholdSeconds`
- `WorkerDegradedThresholdSeconds > (EffectiveReceiveDeadlineSeconds + ReconnectDelaySeconds)`
- `EffectiveReceiveDeadlineSeconds = ReceiveClientDeadlineSeconds` when positive, otherwise
  `ReceiveTimeoutSeconds + 5`
- `WorkerUnhealthyErrorThreshold > WorkerConsecutiveErrorThreshold`
- `WorkerConsecutiveErrorThreshold >= 1`

Defaults: `EnableWorkerHealthCheck=true`, `WorkerDegradedThresholdSeconds=60`,
`WorkerUnhealthyThresholdSeconds=180`, `WorkerConsecutiveErrorThreshold=3`,
`WorkerUnhealthyErrorThreshold=10`.

## Gateway

The gateway exposes `GET /health` for container and manual probes. See
[Health and observability](../operations/health-and-observability.md) for the operational view.
