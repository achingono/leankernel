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

## Review Focus
- Learning never blocks or slows the synchronous turn response.
- Write-back uses scope-relative keys and preserves partitioning (no double-prefixing).
- Steps are idempotent and safe to retry on worker restart.
- Cron evaluation is correct across DST/time-boundary edge cases.
- Job execution failures are isolated and logged with actionable context.
