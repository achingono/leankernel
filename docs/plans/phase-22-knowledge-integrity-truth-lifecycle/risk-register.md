# Phase 22 Risk Register

## Risks

| ID | Risk | Impact | Mitigation | Status |
| --- | --- | --- | --- | --- |
| R1 | Over-aggressive contradiction merging | Loss of valid nuanced facts | Start with conservative policy and require deterministic provenance retention | Open |
| R2 | Temporal precedence edge cases | Incorrect canonical truth selection | Add explicit validity model and replay tests for historical scenarios | Open |
| R3 | Integrity processing adds latency | Slower write/read paths | Keep lifecycle processing async where possible; add bounded budgets and queueing | Open |
| R4 | Scope/policy leak in reconciliation | Cross-channel or cross-user data exposure | Enforce permit checks at repository/query layers and add isolation tests | Open |
| R5 | Unclear storage home for canonical claims | DB vs GBrain contention; retrieval ambiguity | Store canonical claims in EF Core DB (same as ingestion jobs); GBrain stores only source documents, not claim metadata; retrieval queries canonical claims from DB and joins source references by `SourceId` | Open |
| R6 | Dream output hook fragility | Dream phase-completion events may not carry sufficient claim extraction metadata | Require Dream run reports to include structured fact extraction payloads; add fallback extraction from Dream output text when structured payload is absent | Open |
| R7 | Confidence decay drift across claim types | Uniform decay may over-expire long-lived facts or under-expire volatile ones | Start with uniform decay; add type-specific decay multiplier in v2 based on `SourceType` and claim-category heuristics | Open |

## Open Decisions
- Whether all contradiction classes should auto-resolve or some should always require operator review.
- Whether confidence decay should be uniform or type-specific by claim category.
