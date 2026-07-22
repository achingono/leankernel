# Phase 20 Exit Criteria

## Gate Checklist
- [ ] Canonical identity boundaries are documented and reflected in code contracts, including the current split between person-scoped memory, user-scoped transcript/session ownership, and anonymous session isolation.
- [ ] `IPolicyContext`, `IPolicy<TEntity>`, and `IPolicyEvaluator` exist in a shared library.
- [ ] Policy evaluation covers identity, authorization, memory, and budget decisions without pushing business rules into the Gateway or bypassing the existing `IPermit<TEntity>` / `IFilter<TEntity>` / `IRepository<TEntity>` enforcement path.
- [ ] Event contracts for turns, tool calls, and telemetry are append-only, include envelope metadata required for partitioning/correlation, and support derived reads.
- [ ] Migration/coexistence rules explain how the event spine relates to `SessionEntity`, `TurnEntity`, and `TurnTelemetryEntity`, including retry dedupe, compaction, soft-delete, and tool-call representation.
- [ ] At least one real consumer path uses the new policy core and event spine.
- [ ] Gateway host code remains thin and limited to composition/transport concerns.
- [ ] Unit + integration tests cover policy evaluation, event emission, and first-adopter migration.
- [ ] Documentation explains how to extend the policy core, why it remains in-process, and which implementation choices were deliberately deferred.

## Approval Table

| Role | Name | Status | Notes |
| --- | --- | --- | --- |
| Owner | | Pending | |
| Reviewer | | Pending | |
| Approver | | Pending | |
