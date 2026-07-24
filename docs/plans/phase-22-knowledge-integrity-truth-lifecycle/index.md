# Phase 22 Knowledge Integrity And Truth Lifecycle

## Companion Documents
- [Inputs](inputs.md)
- [Activities](activities.md)
- [Outputs](outputs.md)
- [Exit Criteria](exit-criteria.md)
- [Risk Register](risk-register.md)
- [Evidence](evidence.md)

## Objective
Add a durable truth-management layer above ingested and synthesized memory so LeanKernel can detect contradictions, represent temporal validity, reconcile competing claims with deterministic policy, and keep confidence fresh over time without weakening tenant/user/channel partitioning.

## Scope
This phase introduces canonical claim/evidence lifecycle primitives and conflict-resolution workflows that operate across ingestion, Dream synthesis, and retrieval surfaces. It does not replace current ingestion, scheduler, or UI phases; it adds integrity controls and deterministic behavior on top.

## In Scope
- Canonical claim model with confidence, provenance, validity window, and supersession links.
- Contradiction detection between claims within the same authorized scope.
- Deterministic resolution policy (`auto-resolve`, `flag-for-review`, `retain-with-uncertainty`).
- Confidence decay/refresh policy driven by recency and repeated confirmation.
- Integrity hooks for document ingestion and Dream outputs so derived facts enter the same lifecycle.
- APIs/services for querying conflict sets and current canonical truth views.
- Tests for conflict detection, temporal precedence, supersession chains, and scope safety.

## Out of Scope
- UI moderation consoles (Phase 09 consumer).
- New ingestion sources or channel adapters.
- Model-routing policy changes (Phase 04 consumer).

## Entry Criteria
- Phase 21 ingestion pipeline and search/list surfaces are operational (Partial — core ingestion complete).
- Phase 20 identity and policy contracts are in place for scope-safe writes (Complete).

### Dream-Dependent Scope (blocked on Phase 07)
Steps 5 (Dream output integration) and related exit-criteria item 8 (`Ingestion and Dream-derived facts enter the same truth lifecycle path`) require Phase 07 Dream orchestration to be operational. Steps 1–4 and 6–7 proceed independently of Phase 07. See `activities.md` for the dependency split.

## Exit Criteria
Canonical claims are represented with provenance and validity metadata, contradictions are detectable and resolvable deterministically, confidence evolves over time, and retrieval can consume a stable canonical truth view per authorized scope. See `exit-criteria.md`.

## Status
**Planned** — Core claim model steps (1–4, 6–7) are unblocked; Dream integration (step 5) is blocked on Phase 07.

## Roles
- Owner: Rebuild maintainer
- Reviewer: Separate agent session / model review
- Approver: Repository owner
