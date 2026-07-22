# Phase 20 Exit Criteria

## Gate Checklist
- [x] Canonical identity boundaries are documented and reflected in code contracts, including the current split between person-scoped memory, user-scoped transcript/session ownership, and anonymous session isolation.
- [x] `IPolicyContext`, `IPolicy<TEntity>`, and `IPolicyEvaluator` exist in a shared library.
- [x] Policy evaluation covers identity, authorization, memory, and budget decisions without pushing business rules into the Gateway or bypassing the existing `IPermit<TEntity>` / `IFilter<TEntity>` / `IRepository<TEntity>` enforcement path.
- [x] Event contracts for turns, tool calls, and telemetry are append-only, include envelope metadata required for partitioning/correlation, and support derived reads.
- [x] Migration/coexistence rules explain how the event spine relates to `SessionEntity`, `TurnEntity`, and `TurnTelemetryEntity`, including retry dedupe, compaction, soft-delete, and tool-call representation.
- [x] At least one real consumer path uses the new policy core and event spine.
- [x] Gateway host code remains thin and limited to composition/transport concerns.
- [x] Unit + integration tests cover policy evaluation, event emission, and first-adopter migration.
- [x] Documentation explains how to extend the policy core, why it remains in-process, and which implementation choices were deliberately deferred.

## Approval Table

| Role | Name | Status | Notes |
| --- | --- | --- | --- |
| Owner | Coding agent | Complete | Implementation delivered with tests and docs updates |
| Reviewer | Deep review sub-agent | Complete | High/medium findings addressed in follow-up changes |
| Approver | | Pending | |
