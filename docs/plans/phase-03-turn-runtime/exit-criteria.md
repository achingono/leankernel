# Phase 03 Exit Criteria

## Gate Checklist
- [ ] Turns execute through the explicit `TurnPipeline` with ordered, observable stages.
- [ ] Context admission is deny-by-default and rejects items that exceed the configured budget.
- [ ] History shaping/compaction is deterministic and partition-safe (verified by tests).
- [ ] Scoped retrieval merges knowledge/memory candidates without breaking tenant/user/channel isolation.
- [ ] Long-running turns emit progress directives and can continue across multiple model calls.
- [ ] `MapOpenAIResponses()` remains on the current no-argument path and existing tests pass.
- [ ] Unit + integration tests cover admission, budget, compaction, scoping, and continuation.
- [ ] Configuration validated at startup; invalid budgets/thresholds fail fast with clear errors.

## Approval Table

| Role | Name | Status | Notes |
| --- | --- | --- | --- |
| Owner | | Pending | |
| Reviewer | | Pending | |
| Approver | | Pending | |
