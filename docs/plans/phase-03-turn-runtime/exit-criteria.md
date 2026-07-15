# Phase 03 Exit Criteria

## Gate Checklist
- [x] Turns execute through the explicit `TurnPipeline` with ordered, observable stages.
- [x] Context admission is deny-by-default and rejects items that exceed the configured budget.
- [x] History shaping/compaction is deterministic and partition-safe (verified by tests).
- [x] Scoped retrieval merges knowledge/memory candidates without breaking tenant/user/channel isolation.
- [x] Long-running turns emit progress directives and can continue across multiple model calls.
- [x] `MapOpenAIResponses()` remains on the current no-argument path and existing tests pass.
- [x] Unit + integration tests cover admission, budget, compaction, scoping, and continuation.
- [x] Configuration validated at startup; invalid budgets/thresholds fail fast with clear errors.

## Approval Table

| Role | Name | Status | Notes |
| --- | --- | --- | --- |
| Owner | | Pending | |
| Reviewer | | Pending | |
| Approver | | Pending | |
