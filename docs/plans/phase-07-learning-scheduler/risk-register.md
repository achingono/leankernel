# Phase 07 Risk Register

## Risks

| ID | Risk | Impact | Mitigation | Status |
| --- | --- | --- | --- | --- |
| R1 | Learning write-back double-prefixes memory scope keys | Corrupted memory scope | Pass scope-relative keys; reuse memory conventions; tests | Open |
| R2 | Unbounded queue growth under load | Memory pressure/OOM | Bounded queue + backpressure/drop policy | Open |
| R3 | Non-idempotent steps duplicate facts on retry | Knowledge noise | Idempotent step design + dedupe keys | Open |
| R4 | Cron evaluation wrong across DST/time zones | Missed/duplicated jobs | Explicit time-zone handling + boundary tests | Open |
| R5 | Job execution errors crash the scheduler loop | Automation outage | Per-job isolation + retry policy + logging | Open |
| R6 | Learning consumes model budget unexpectedly | Cost | Gate steps behind config + rate limits | Open |
| R7 | Dream lock contention starves scheduled enrichment | Stale memory and backlog growth | Lock-aware skip/retry policy and bounded drain windows | Open |
| R8 | Dream model config drifts from LiteLLM-supported aliases | Dream failures or degraded synthesis quality | Bootstrap defaults for `models.dream.*` and startup validation checks | Open |

## Open Decisions
- Queue durability (in-memory vs persisted) and restart-replay semantics.
- Whether onboarding directives inject into the sync turn (Phase 03) or only asynchronously.
- Dream trigger policy: pure cadence vs hybrid cadence + backlog threshold.
