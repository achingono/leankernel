# Phase 07 Activities

## Step-By-Step Activities
1. Implement a bounded turn-event queue that captures completed turns (from the Phase 03 pipeline event) without blocking the response path.
2. Implement a learning background worker that drains the queue and runs an ordered self-improvement pipeline.
3. Implement pipeline steps: fact extraction (reuse memory extraction), identity-intent extraction, capability-gap detection, and engagement tracking — each idempotent and scope-aware.
4. Implement a knowledge-page update coordinator that writes learned facts/knowledge back to wiki/knowledge and memory under the correct tenant/user/channel scope (scope-relative keys per memory conventions).
5. Implement onboarding intelligence: an onboarding gap detector and directive builder that consume learned identity intent to inject onboarding prompts when identity data is missing.
6. Implement the scheduler: a cron evaluator, a time-boundary service, a job executor invoking runtime services, and a scheduler hosted service; add scheduled-job entities/repository and migrations.
7. Add configuration (learning enable + step toggles + queue bounds; scheduler jobs + cron expressions) and startup validation.
8. Add tests: queue backpressure/bounding, step ordering and idempotency, scope-correct write-back, cron parsing/evaluation, time-boundary logic, and job execution.
9. Document the learning pipeline, onboarding intelligence, and scheduler in `docs/features/`.

### Intelligent Brain Delta Activities
10. Add scheduler-owned `DreamCycleJob` that invokes native `gbrain dream` phases with source scoping.
11. Add bounded Dream run windows and lock-aware retries (`skip` on active lock, retry policy with jitter).
    - **Lock ownership**: The Dream run lock is an in-memory `SemaphoreSlim(1,1)` per Dream source scope, held by the scheduler worker that owns the current `DreamCycleJob` execution. Lock is released on completion, failure, or cancellation.
    - **Lock timeout**: `DreamLockTimeoutSeconds` (default 300s) — if a Dream run exceeds this, the worker abandons the lock (marks the run as `TimedOut`) and the scheduler requeues with backoff. This prevents stale locks from blocking subsequent windows.
    - **Retry on active lock**: When a new `DreamCycleJob` fires but the source scope lock is already held (another Dream run in progress), the job is skipped (`DreamSkippedDueToLock` status) and rescheduled at next cron interval with jitter (±10% of cron period).
12. Persist Dream run reports (phase status, totals, failures) for diagnostics and replay.
13. Add queue-depth/time-based triggers that schedule Dream windows from ingestion backlog signals.
14. Add tests for Dream orchestration idempotency, lock handling, and source attribution.

## Review Focus
- Learning never blocks or slows the synchronous turn response.
- Write-back uses scope-relative keys and preserves partitioning (no double-prefixing).
- Steps are idempotent and safe to retry on worker restart.
- Cron evaluation is correct across DST/time-boundary edge cases.
- Job execution failures are isolated and logged with actionable context.
