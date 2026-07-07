# PRD: LeanKernel Document Ingestion Congestion and Backpressure Remediation

## Status
- **Completed** — all phases implemented and deployed
- Owner: Platform/Tools
- Scope: `LeanKernel.Tools` ingestion queue, folder watcher, and deployment defaults
- See [Implementation Refinements](#implementation-refinements) for changes applied after initial implementation

## Context
Production ingestion is experiencing sustained congestion and retry storms when folder watching is enabled against large recursive document trees. The current behavior can flood the in-memory queue, generate repeated enqueue failures, and create noisy logs without making proportional ingestion progress.

## Problem Statement
Document ingestion must handle bursty and large backlogs without dropping work, repeatedly re-scheduling the same files, or producing excessive operational noise. Current queue and watcher semantics do not provide robust producer backpressure and cause repeated retries under pressure.

## Root Causes
1. **Watcher scope is too broad by default**
   - Recursive watch (`WatchIncludeSubdirectories=true`) plus wide filter (`*.*`) captures very large historical corpora.
2. **Startup/backfill flood in live mode**
   - Startup scan can queue all existing files at once when enabled.
3. **Queue and worker mismatch under load**
   - Queue capacity defaults to `MaxQueuedDocuments=100` while ingestion concurrency is `MaxConcurrentJobs=3`.
4. **Fail-fast enqueue behavior on full queue**
   - `TryWrite` failure throws immediately instead of applying bounded producer wait/backpressure.
5. **Polling amplifies repeated retries**
   - Paths are removed from scheduled state after failure and retried every polling cycle (default 30s), causing log spam and repeated failures.
6. **Missing SourcePath persistence and repository integration**
   - Path-based ingestion jobs are saved to the database without their `SourcePath` because the database entity (`DocumentIngestionJobEntity`) lacks this field. When retrieved for retry, they are instantiated as the base `DocumentIngestionJob` which fails due to missing file streams. Additionally, the existing `IDocumentIngestionJobRepository` is not currently integrated into the hosted services.

## Goals
- Eliminate queue-full retry storms and reduce ingestion-related error logs by at least 90% during backlog pressure.
- Guarantee bounded producer behavior (wait/backoff/cancel) rather than fail-fast enqueue exceptions.
- Prevent repeated ingestion attempts for unchanged files across polls and restarts.
- Separate one-time bulk backfill from steady-state live watcher ingestion.

## Non-Goals
- Replacing the entire ingestion architecture with an external queue service.
- Redesigning document parsing or embedding pipelines.

## Product Requirements

### R1. Immediate Runtime Stabilization (Config-Only)
- Default normal runtime to:
  - `WatchStartupScanEnabled=false`
  - `WatchPollingIntervalSeconds=0` (explicitly disables polling; use a positive value only when polling is intentionally enabled)
- Narrow watch scope/path so live watcher targets only intended drop zones.
- Expose and tune:
  - `MaxQueuedDocuments`
  - `MaxConcurrentJobs`

### R2. Queue Backpressure Semantics
- Queue API must support asynchronous enqueue with cancellation and timeout. Use `ChannelWriter.WriteAsync` with a linked cancellation token source for timeouts.
- Full queue must not throw a fail-fast operational exception in normal producer path.
- Enqueue result should be explicit (`Queued`, `TimedOut`, `Cancelled`, `Duplicate`), returning an `EnqueueResult` record containing status and job details.

### R3. Path Scheduling and Retry Discipline
- Maintain per-path lifecycle state (`Pending`, `Queued`, `Processing`, `Completed`, `Failed`, `RetryDue`).
- Fix `IDocumentIngestionJobRepository` integration: add `SourcePath` to `DocumentIngestionJobEntity` and update `DocumentIngestionJobRepository` mapping to preserve `PathDocumentIngestionJob` details during database-backed retries.
- Avoid dropping scheduled state after transient enqueue failures.
- Apply exponential backoff with jitter for retryable enqueue failures.
- Poller (if enabled) should only process newly discovered paths or retry-due paths.
- Define state transitions explicitly: discovered -> pending, accepted by queue -> queued, worker started -> inflight, retryable failure -> retry-due, duplicate/unchanged/force-complete -> terminal.
- Persist retry-due and terminal outcomes so restarts do not re-hammer paths that already failed transiently or were already processed.

### R4. Dedupe and Idempotency
- Track file fingerprints (minimum: normalized path + mtime + size), where normalized path is defined per platform and stable across separator/case differences on the same volume.
- Persist fingerprint state in a new database table `engine."DocumentFingerprints"` so restarts do not re-ingest unchanged files.
- Treat renames and moves as new fingerprints unless an explicit move-aware identity is introduced later.
- Support explicit force-reingest override path for operational use.

### R5. Bulk Backfill Operational Mode
- Provide controlled batch importer for historical corpus.
- Keep live watcher focused on new arrivals only.
- Support checkpoints/resume for long backfill runs.

### R6. Observability and Guardrails
- Emit metrics:
  - Queue depth
  - Enqueue wait time
  - Enqueue timeouts
  - Retry count
  - Duplicate skip count
  - Ingestion success/failure rate and latency
- Emit structured logs with reason codes (`queue_full`, `retry_scheduled`, `duplicate_skipped`, `not_stable`).
- Alert on sustained high queue depth, retry explosions, and ingestion latency SLO breaches.

## Implementation Plan

### Phase 0: Stabilize Production (No Code)
1. Update deployment configuration to disable startup scan and polling in normal runtime.
2. Reduce watch scope to intended drop folder(s), excluding archival trees.
3. Increase queue/concurrency conservatively based on CPU/IO headroom.
4. Verify log noise and queue-full errors decline immediately.

### Phase 1: Queue API and Backpressure
1. Add async enqueue semantics (`QueueAsync` and `QueuePathAsync` returning `EnqueueResult`) to `IDocumentIngestionQueue`.
2. Implement `WriteAsync` with a linked cancellation/timeout token source in `DocumentIngestionQueue`.
3. Return explicit enqueue outcome instead of throwing on full queue in watcher path.
4. Update `DocumentIngestionJobEntity` to store `SourcePath` and fix `DocumentIngestionJobRepository` deserialization mapping for `PathDocumentIngestionJob`. Integrate the repository with `DocumentIngestionHostedService`.

### Phase 2: Watcher State Machine + Backoff
1. Replace byte marker sets with per-path scheduling metadata tracking.
2. Keep path state through transient failures in the hosted service.
3. Add exponential backoff + jitter and retry eligibility timestamps.
4. Ensure polling does not re-hammer non-due paths.

### Phase 3: Dedupe Persistence
1. Create `DocumentFingerprintEntity` and register it in `LeanKernelDbContext`.
2. Add dynamic schema extension helper `EnsureFingerprintSchemaAsync` in `LeanKernelDbContextSchemaExtensions` to create the `DocumentFingerprints` table on startup.
3. Implement fingerprint computation (size + mtime + normalized path) and persisted check during watcher startup scan and poll.
4. Skip unchanged files across restart/poll cycles by querying the database.
5. Add operator override to force re-ingest.

### Phase 4: Bulk Backfill Separation
1. Add a controlled batch backfill workflow.
2. Document operational runbook for bulk import vs live mode.
3. Validate no live-ingestion starvation during backfill windows.

## Rollout Strategy
1. Deploy Phase 0 configuration first for immediate relief.
2. Ship Phases 1-2 behind feature flags where practical; enable gradually.
3. Deploy Phase 3 dedupe persistence and validate restart behavior.
4. Introduce Phase 4 bulk mode and update operations SOP.

## Acceptance Criteria
- Under large backlog conditions, no repeated queue-full exception storms are observed.
- Enqueue under pressure shows bounded wait/backoff behavior with controlled retries.
- Duplicate re-ingestion of unchanged files is prevented across restart and polling cycles.
- Operational logs are informative and low-noise; alerts fire on true degradation.
- Bulk corpus ingestion can be completed independently of live watcher mode.

## Risks and Mitigations
- **Risk:** Higher queue/concurrency increases resource contention.
  - **Mitigation:** Incremental tuning with metrics and rollback thresholds.
- **Risk:** Dedupe fingerprints may miss edge cases (renames/content changes).
  - **Mitigation:** Include size+mtime baseline and optional content hash mode.
- **Risk:** Retry policy may delay legitimate urgent ingestion.
  - **Mitigation:** Backoff caps, priority lanes for explicit/manual imports.

## Implementation Refinements

The following refinements were applied during code review after the initial implementation to address correctness gaps identified in production-readiness review:

### R4a. Fingerprint race handling (`FingerprintService.cs`)
**Issue:** `RecordFingerprintAsync` has a TOCTOU race between `AnyAsync` (check) and `SaveChangesAsync` (insert). Two concurrent writers can both pass the check, then one succeeds and the other fails on the PK constraint.

**Fix:** Wrap `SaveChangesAsync` in a try-catch for `DbUpdateException`. The losing writer treats the collision as a no-op (idempotent). The `AnyAsync` guard remains as an optimization for the common (non-racing) case.

### R4b. Fingerprint timing in watcher pipeline (`DocumentFolderIngestionHostedService.cs`)
**Issue:** Fingerprint was computed via `ComputeFingerprint(path)` *before* `WaitForStableFileAsync`. If the file was still being written, the fingerprint captured pre-stable size/mtime. This caused incorrect dedupe decisions and recording a fingerprint that didn't match the final content.

**Fix:** Reordered the pipeline so fingerprint computation happens *after* the file is confirmed stable. The same fingerprint value is used for both the dedupe check and the recording, ensuring consistency.

### R5a. Sequential checkpoint advancement (`DocumentBackfillService.cs`)
**Issue:** Checkpoint was written at *scheduling* time (immediately after `IngestFileAsync` was launched via `ContinueWith`), not at *completion* time. If the process crashed between scheduling and ingestion, the checkpoint file stored a path that was never ingested. On resume, the file was silently skipped.

**Fix:** Checkpoint is now written only after successful completion. Shared state (`ConcurrentDictionary<int, byte>` for completed file indices, a sequential `nextCheckpointIndex`, and a lock) ensures checkpoint only advances through consecutively completed files in sorted order. Out-of-order completions cannot skip uncompleted files.

### R5b. Graceful shutdown drain (`DocumentFolderIngestionHostedService.cs`)
**Issue:** `StopAsync` did not wait for per-path inflight tasks tracked in `_inflight`. If shutdown raced with active path processing, the inflight task could be orphaned or interrupted mid-operation.

**Fix:** `StopAsync` snapshots `_inflight.Values` and waits for all pending tasks with a 10-second timeout before completing shutdown.

### R2a. Distinct error messages for channel states (`DocumentIngestionQueue.cs`)
**Issue:** `EnqueueSync` threw "queue is full" for all `TryWrite` failures, even when the channel writer was completed/faulted. This made shutdown-related failures indistinguishable from capacity pressure in diagnostics.

**Fix:** Check `_channel.Reader.Completion.IsCompleted` before falling through to the full-queue message. A closed/faulted channel emits `"Document ingestion queue is closed: {reason}"` instead.

## Operational Notes
- Use unique image tags for Swarm deployments (avoid `:latest` cache divergence).
- Prefer two-mode operation:
  - **Live mode:** watcher only for new files
  - **Backfill mode:** controlled batch importer for legacy trees

## Initial File Targets
- `src/LeanKernel.Abstractions/Configuration/DocumentIngestionConfig.cs`
- `src/LeanKernel.Abstractions/Models/DocumentIngestionModels.cs`
- `src/LeanKernel.Abstractions/Interfaces/IDocumentIngestionJobRepository.cs`
- `src/LeanKernel.Persistence/Entities/DocumentIngestionJobEntity.cs`
- `src/LeanKernel.Persistence/DocumentIngestionJobRepository.cs`
- `src/LeanKernel.Persistence/LeanKernelDbContext.cs`
- `src/LeanKernel.Persistence/LeanKernelDbContextSchemaExtensions.cs`
- `src/LeanKernel.Persistence/FingerprintService.cs` (added)
- `src/LeanKernel.Abstractions/Interfaces/IDocumentFingerprintService.cs` (added)
- `src/LeanKernel.Tools/IDocumentIngestionQueue.cs`
- `src/LeanKernel.Tools/DocumentIngestionQueue.cs`
- `src/LeanKernel.Tools/DocumentFolderIngestionHostedService.cs`
- `src/LeanKernel.Tools/DocumentIngestionHostedService.cs`
- `src/LeanKernel.Tools/Ingestion/DocumentBackfillService.cs` (added)
- `docker-compose.yml`
- `swarm/deploy/leankernel/docker-stack.yml` (in `swarm` repo)
