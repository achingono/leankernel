# Phase 23 Memory Evaluation And Replay Harness

## Companion Documents
- [Inputs](inputs.md)
- [Activities](activities.md)
- [Outputs](outputs.md)
- [Exit Criteria](exit-criteria.md)
- [Risk Register](risk-register.md)
- [Evidence](evidence.md)

## Objective
Establish a repeatable evaluation and replay harness for memory quality so ingestion, Dream, retrieval, and truth-lifecycle changes can be measured, gated, and promoted safely using deterministic datasets and regression thresholds.

## Scope
This phase delivers offline and continuous evaluation infrastructure for memory/retrieval behavior, including replay of historical scenarios and quality gates for rollout. It does not add new runtime retrieval algorithms by itself; it validates and guards the behavior delivered by other phases.

## In Scope
- Golden datasets and scenario fixtures for ingestion, retrieval, contradiction, and grounded response checks.
- Replay runner that can execute deterministic test turns against stored snapshots and compare outputs.
- Evaluation metrics: recall@k, grounded-answer rate, freshness, contradiction leakage, and false-positive conflict rate.
- Promotion gates for memory-affecting changes (Dream config changes, retrieval ranking updates, truth-lifecycle policies).
- CI integration for eval execution and threshold enforcement.
- Diagnostics exports to support trend analysis over time.

## Out of Scope
- Human annotation tooling UI.
- New provider integrations.
- Runtime orchestration/scheduling changes outside eval triggers.

## Entry Criteria
- Phase 21 ingestion and query surfaces are available.
- Phase 22 truth lifecycle contracts are defined.
- Phase 17 telemetry exports are available for labeling and replay attribution.

## Exit Criteria
Memory quality can be measured deterministically, regressions are detectable before release, and promotion gates block unsafe changes to ingestion/Dream/retrieval/truth-lifecycle behavior. See `exit-criteria.md`.

## Status
**Planned** — Blocked on Phase 22 truth-lifecycle contracts.

## Roles
- Owner: Rebuild maintainer
- Reviewer: Separate agent session / model review
- Approver: Repository owner
