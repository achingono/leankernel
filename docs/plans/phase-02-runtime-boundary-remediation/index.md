# Phase 02 Runtime Boundary Remediation

## Companion Documents
- [Inputs](inputs.md)
- [Activities](activities.md)
- [Outputs](outputs.md)
- [Exit Criteria](exit-criteria.md)
- [Risk Register](risk-register.md)
- [Evidence](evidence.md)

## Objective
Remediate the contextual runtime issues identified in the post-Sonar code review by restoring consistent tenant/user/channel isolation, hardening trust boundaries at the HTTP edge, correcting state and transcript lifecycle behavior, and reducing persistence-model drift. This phase is intended to close the gap between the accepted architecture decisions and the current implementation so that memory, conversation history, and durable agent state behave safely under real multi-tenant and concurrent production traffic.

## Scope
This phase covers the corrective work for the review findings in the Gateway, Logic, Data, and Core layers. It includes trust-boundary hardening, tenant-scoped anonymous identity fixes, memory-scope enforcement, transcript/state correctness fixes, conversation-growth controls, EF model cleanup, and verification updates. It does not broaden the product surface or introduce unrelated feature work.

## In Scope
- Fail closed when tenant resolution is missing or untrusted instead of continuing with `Guid.Empty` ownership.
- Rework forwarded-host trust so tenant resolution depends only on trusted proxy inputs.
- Decide and implement the intended protection model for `/v1/responses` and `/v1/conversations`.
- Make anonymous user resolution tenant-scoped and align it with the ADR.
- Make memory retrieval use the same tenant/user/channel scope as memory persistence.
- Preserve tool-message semantics when transcript history is rehydrated.
- Add replay/idempotency protection for transcript and memory writes.
- Add bounded history retrieval and compaction/summarization behavior for long-running conversations.
- Restore real optimistic concurrency behavior for durable agent session state.
- Fix the accidental duplicate tenant relationship in the EF model and migration set.
- Enforce real JWT signature/issuer/audience/lifetime validation so the persisted `sub`-based identity cannot be forged (finding C4).
- Stabilize anonymous identity so it is not keyed on an ephemeral, non-persisted ASP.NET session id, and stop unbounded guest-row growth (finding M6).
- Make request-path identity resolution async to remove sync-over-async blocking under load (finding M7).
- Add regression tests covering each reviewed finding.

## Findings Reference
The detailed contextual/architectural review (5 Critical, 7 Major, 3 Suggestion) is captured in
[`findings.md`](findings.md), formatted per finding with root cause, why static analysis missed it,
production impact, and recommended fix. Each finding is traced to a scope item above and an
exit-criteria gate.

## Out of Scope
- SonarQube cleanup, code-style work, or standard static-analysis items already covered elsewhere.
- New end-user features unrelated to runtime isolation and state correctness.
- Broad redesign of the OpenAI-compatible surface beyond what is required to close the identified trust-boundary and persistence gaps.
- Replacing GBrain or Microsoft Agent Framework packages unless a blocking package limitation is confirmed during implementation.

## Entry Criteria
- The layer-by-layer review findings from 2026-07-13 are accepted as the remediation input set for this phase.
- The current ADRs for identity partitioning and transcript/runtime separation remain the source of truth unless explicitly superseded.
- A separate model/session is available to review this plan before code implementation begins.

## Exit Criteria
All review findings are either remediated in code or closed by an explicit approved decision, with regression coverage and verification evidence captured for each area. See `exit-criteria.md`.

## Roles
- Owner: OpenCode
- Reviewer: separate agent session / model review
- Approver: repository owner
