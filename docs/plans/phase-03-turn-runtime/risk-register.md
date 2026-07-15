# Phase 03 Risk Register

## Risks

| ID | Risk | Impact | Mitigation | Status |
| --- | --- | --- | --- | --- |
| R1 | Explicit pipeline conflicts with MAF's own middleware ordering | Duplicate or missing context injection | Keep pipeline stages above MAF invocation; assert single injection point in tests | Closed |
| R2 | Non-deterministic compaction breaks reproducibility | Flaky output/tests | Embedding-based extractive compaction (deterministic); stable ordering by cosine similarity; sentence-order preservation | Closed |
| R3 | Retrieval scoping regression leaks cross-partition data | Privacy/isolation breach | Reuse `IdentityIsolationKeyProvider`; add isolation tests | Closed |
| R4 | Continuation double-persists turns or corrupts agent state | Data corruption | Idempotent persistence keyed by turn id; state-store tests | Open |
| R5 | Budget accounting diverges from real token usage | Prompt overflow at provider | Conservative estimator (chars/4 heuristic) + configurable safety margin; Tier 2 extractive compaction reduces token count before prompt assembly | Closed |

## Open Decisions
- Whether continuation state lives in agent-state store or a dedicated turn-progress table.
