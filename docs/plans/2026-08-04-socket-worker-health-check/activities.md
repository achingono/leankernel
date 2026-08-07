# Phase 01 Activities

## Step-By-Step Activities

### 1. Extend SignalSettings with Health Check Thresholds and Options Validation
Add configuration properties to `SignalSettings` for health check thresholds:
- `WorkerDegradedThresholdSeconds` (default: 60, progress stall threshold for Degraded status; ~3x `ReceiveTimeoutSeconds` of 20s)
- `WorkerUnhealthyThresholdSeconds` (default: 180, progress stall threshold for Unhealthy status)
- `WorkerConsecutiveErrorThreshold` (default: 3, consecutive loop error limit before escalating worker to Degraded status)
- `WorkerUnhealthyErrorThreshold` (default: 10, consecutive loop error limit before escalating worker to Faulted / Unhealthy status)
- `EnableWorkerHealthCheck` (default: true, feature flag)

Configuration validation via `IValidateOptions<SignalSettings>` or `Validate<SignalSettings>`:
- Enforce `WorkerUnhealthyThresholdSeconds > WorkerDegradedThresholdSeconds`.
- Enforce `WorkerDegradedThresholdSeconds > ReceiveClientDeadlineSeconds` (or default deadline when `<= 0`, which is `ReceiveTimeoutSeconds + 5`).
- Enforce `WorkerUnhealthyErrorThreshold > WorkerConsecutiveErrorThreshold`.
- Throw `OptionsValidationException` / `InvalidOperationException` at options validation time if violated.

### 2. Define Worker State DTOs and Provider Interface
Create new files:
- `src/Terminals/LeanKernel.Channels.Signal/HealthChecks/SocketWorkerHealthState.cs` — Immutable public `record` for per-account worker state: `Account` (string), `State` (enum: `Starting`/`Running`/`Stopped`/`Faulted`), `LastSuccessfulReceiveUtc` (DateTime?), `LastWorkerLoopTickUtc` (DateTime?), `StartedUtc` (DateTime), `ConsecutiveErrors` (int), `LastErrorUtc` (DateTime?). Use `record` type to enforce snapshot immutability. `LastSuccessfulReceiveUtc` is `null` when the worker has never received a message (idle account); `LastWorkerLoopTickUtc` is `null` before the first loop iteration.
- `src/Terminals/LeanKernel.Channels.Signal/HealthChecks/ISocketWorkerHealthProvider.cs` — Interface exposing:
  - `IReadOnlyDictionary<string, SocketWorkerHealthState> GetWorkerStates()`
  - `bool IsInitialDiscoveryCompleted { get; }`
  - `bool IsManagerRunning { get; }` (returns `_started && _managerTask != null && !_managerTask.IsCompleted`)

### 3. Instrument SocketTransportClient with Progress Tracking and Manager Liveness
Modify `SocketTransportClient`:
- Create a private mutable class `AccountWorkerHealthTracker` to hold per-account tracking fields:
  - `private long _lastWorkerLoopTickTicks`
  - `private long _lastSuccessfulReceiveTicks`
  - `private int _consecutiveErrors`
  - `private int _workerState`
  - `private long _lastErrorTicks`
  - `public DateTime StartedUtc { get; }`
  - Implement lock-free updates using `Interlocked.Exchange` and `Volatile.Read` on these primitive fields. (Note: Using primitive `long`/`int` fields in a mutable `class` overcomes the C# `ref` parameter restriction on C# `record` types).
- Use a `ConcurrentDictionary<string, AccountWorkerHealthTracker>` keyed by account number.
- Update `RunAccountWorkerAsync` loop:
  - Transition state from `Starting` to `Running` upon loop tick / WebSocket connection attempt.
  - Update `_lastWorkerLoopTickTicks` via `Interlocked.Exchange` at each iteration start.
  - On successful `EnqueueInboundIfValidAsync`, update `_lastSuccessfulReceiveTicks` via `Interlocked.Exchange`, reset `_consecutiveErrors` to 0, and set `_workerState = Running`.
  - On exception, increment `_consecutiveErrors` atomically via `Interlocked.Increment`, set `_lastErrorTicks`, and set `_workerState = Faulted` if `_consecutiveErrors >= WorkerUnhealthyErrorThreshold`.
  - On successful reconnection after a transient failure, reset `_consecutiveErrors` to 0 and revert `_workerState` to `Running`.
- Refactor `DiscoverConfiguredAccountsAsync` & `RefreshWorkersAsync`:
  - Modify `DiscoverConfiguredAccountsAsync` to return `(bool Success, IReadOnlyList<string> Accounts)`. On non-2xx status code or REST exception, return `(false, Array.Empty<string>())`.
  - In `RefreshWorkersAsync`: If `Success` is `false`, log warning and skip stopping/removing workers to prevent tearing down active connections during transient REST API glitches.
  - Set `_initialDiscoveryCompleted = true` **only when** `Success` is `true`.
  - Set `workerState = Starting` for new workers. When accounts are deprovisioned, remove entry from tracking dictionary entirely (removed workers are no longer monitored).
- Implement `ISocketWorkerHealthProvider`:
  - `GetWorkerStates()`: Returns immutable `SocketWorkerHealthState` record copies projected from `AccountWorkerHealthTracker` instances.
  - `IsManagerRunning`: Checks `_started && _managerTask != null && !_managerTask.IsCompleted`.

### 4. Implement SocketWorkerHealthCheck
Create `src/Terminals/LeanKernel.Channels.Signal/HealthChecks/SocketWorkerHealthCheck.cs`:
- Inject `ISocketWorkerHealthProvider`, `IOptions<SignalSettings>`, `ILogger<SocketWorkerHealthCheck>`.
- In `CheckHealthAsync`:
  - If `EnableWorkerHealthCheck` is false → Healthy ("Worker health check disabled").
  - If provider indicates transport is started (`_started`) but `IsManagerRunning` is false → Unhealthy ("Socket worker manager task is not running or crashed").
  - If `provider.IsInitialDiscoveryCompleted` is false → Healthy ("Initial account discovery in progress").
  - Get worker states snapshot from provider.
  - If no configured accounts exist (empty snapshot) → Healthy ("No configured accounts to monitor").
  - For each account worker, compute `progressAge`:
    - `progressAge = DateTime.UtcNow - max(LastSuccessfulReceiveUtc ?? DateTime.MinValue, LastWorkerLoopTickUtc ?? DateTime.MinValue)`.
  - Determine health status based on **worst-case account state**:
    - If ANY account is `Faulted` OR has `consecutiveErrors >= WorkerUnhealthyErrorThreshold` OR has `progressAge > WorkerUnhealthyThresholdSeconds` → Unhealthy
    - If ANY account is in `Starting` state and `DateTime.UtcNow - StartedUtc > WorkerDegradedThresholdSeconds` (startup timeout) → Degraded (or Unhealthy if `> WorkerUnhealthyThresholdSeconds`)
    - If ANY account is in `Running` state and (`progressAge > WorkerDegradedThresholdSeconds` OR `consecutiveErrors >= WorkerConsecutiveErrorThreshold`) → Degraded
    - Otherwise → Healthy
  - Include per-account state and manager status details in `HealthCheckResult.Data` for operational diagnostics.

### 5. Register Options Validation and Health Check in Program.cs
- Configure options validation:
  `builder.Services.AddOptions<SignalSettings>().BindSection("Signal").Validate(s => s.WorkerUnhealthyThresholdSeconds > s.WorkerDegradedThresholdSeconds, "WorkerUnhealthyThresholdSeconds must be greater than WorkerDegradedThresholdSeconds").ValidateOnStart();`
- Add `builder.Services.AddSingleton<ISocketWorkerHealthProvider>(sp => sp.GetRequiredService<SocketTransportClient>());`
- Add `.AddCheck<SocketWorkerHealthCheck>(Constants.Healthchecks.SocketWorker, tags: [Constants.Healthchecks.SocketWorker])` to health checks.
- Add `SocketWorker = "socket-worker"` constant to `Constants.Healthchecks`.

### 6. Write Unit Tests
Create `test/LeanKernel.Tests.Unit/Signal/SocketWorkerHealthCheckTests.cs`:
- Test `SocketWorkerHealthCheck` logic with mocked `ISocketWorkerHealthProvider`:
  - No accounts → Healthy
  - Worker progressing within threshold → Healthy
  - Worker stalled beyond degraded threshold → Degraded
  - Worker stalled beyond unhealthy threshold → Unhealthy
  - Worker faulted or accumulated max consecutive errors → Unhealthy
  - Worker stuck in `Starting` state past startup timeout → Degraded / Unhealthy
  - Manager task crashed / not running → Unhealthy
  - Feature flag disabled → Healthy
  - Discovery in progress (`IsInitialDiscoveryCompleted = false`) → Healthy
  - Multiple accounts, worst-case status aggregation
- Test `SocketTransportClient` progress tracking:
  - `lastSuccessfulReceiveUtc` updates on valid payload
  - `lastWorkerLoopTickUtc` updates each loop iteration
  - `consecutiveErrors` increments on exception
  - Worker state transitions correctly (`Starting` → `Running` → `Faulted` → `Running` on recovery)
  - Account discovery failure does not drop active workers or set `IsInitialDiscoveryCompleted = true`
- Test `SignalSettings` validation:
  - `WorkerUnhealthyThresholdSeconds <= WorkerDegradedThresholdSeconds` fails validation
  - `WorkerDegradedThresholdSeconds <= ReceiveClientDeadlineSeconds` fails validation

### 7. Integration Test (Optional)
- Spin up signal-channel with test Signal API mock
- Verify `/health` endpoint includes `socket-worker` check with expected states

### 8. Documentation Updates
- Update `docs/architecture/solution-structure.md` if new files added
- Update `docs/features/health-checks.md` documenting the new socket worker check
- Update `docs/operations/index.md` with operational guidance

## Review Focus
- Thread safety of progress tracking via private mutable class (`AccountWorkerHealthTracker`) using `Interlocked` / `Volatile`
- Manager loop liveness monitoring (`IsManagerRunning`) to detect manager task crashes
- Account discovery error handling to prevent worker tear-down on transient REST glitches
- Health check threshold logic correctness (edge cases: startup timeout, clock skew, worker restart, worst-case aggregation)
- Options validation enforcing `WorkerUnhealthyThresholdSeconds > WorkerDegradedThresholdSeconds > ReceiveClientDeadlineSeconds`
- Health check does not block or slow down worker loop
- DI registration uses `AddSingleton` correctly for shared `SocketTransportClient` instance
- `SocketWorkerHealthState` is an immutable `record` type
- Test coverage meets 80% threshold for new code