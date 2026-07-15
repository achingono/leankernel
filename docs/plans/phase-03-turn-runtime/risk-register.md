# Phase 03 Risk Register

## Risks

| ID | Risk | Impact | Mitigation | Status |
| --- | --- | --- | --- | --- |
| R1 | Explicit pipeline conflicts with MAF's own middleware ordering | Duplicate or missing context injection | Keep pipeline stages above MAF invocation; assert single injection point in tests | Open |
| R2 | Non-deterministic compaction breaks reproducibility | Flaky output/tests | Enforce stable ordering; avoid time/random inputs; snapshot tests | Open |
| R3 | Retrieval scoping regression leaks cross-partition data | Privacy/isolation breach | Reuse `IdentityIsolationKeyProvider`; add isolation tests | Open |
| R4 | Continuation double-persists turns or corrupts agent state | Data corruption | Idempotent persistence keyed by turn id; state-store tests | Open |
| R5 | Budget accounting diverges from real token usage | Prompt overflow at provider | Conservative estimator + configurable safety margin | Open |

## Open Decisions
- Token estimation approach (heuristic char/token ratio vs tokenizer dependency).
- Whether continuation state lives in agent-state store or a dedicated turn-progress table.
