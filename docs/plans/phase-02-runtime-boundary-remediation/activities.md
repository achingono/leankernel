# Phase 02 Activities

## Step-By-Step Activities
1. Reproduce each finding with focused unit/integration tests before changing behavior so the remediation work has concrete failing or gap-detecting coverage.
2. Harden tenant resolution at the Gateway boundary by restricting forwarded-header trust to known proxies/networks, normalizing the trusted host source, and rejecting requests when no active tenant is resolved instead of assigning `Guid.Empty`.
3. Resolve the API protection model for `/v1/responses` and `/v1/conversations` with the repository owner.
Decision path:
Require authenticated access by default, or explicitly support anonymous access with documented rate limiting, storage controls, and tenant-safe identity behavior.
4. Align anonymous identity resolution with ADR 0002 by making guest-user lookup and creation tenant-scoped and by keeping anonymous isolation keyed by tenant, channel, persisted guest user, and ASP.NET session id.
5. Correct memory isolation by deriving a single canonical scope/namespace from `TenantId`, `UserId`, and `ChannelId`, using it for both memory search and memory save operations, and adding regression tests that prove cross-scope memory cannot be retrieved.
6. Preserve transcript semantics by rehydrating stored `tool` turns as `ChatRole.Tool`, centralizing state-bag keys shared across transcript and session-state code, and removing any remaining fail-open role fallback where the original meaning matters.
7. Make request-side durable writes replay-safe by introducing an idempotency mechanism for transcript and memory persistence.
Implementation target:
Use a stable request operation key recorded in turn/session metadata so retries do not duplicate turns or memory facts.
8. Control transcript growth by adding bounded chat-history retrieval plus compaction/summarization for long-lived sessions, and define the retention/compaction trigger that activates `TurnEntity.IsCompacted` and `CompactionSourceId`.
9. Fix durable agent-state concurrency by treating `RowVersion` conflicts as a real conflict path instead of last-write-wins overwrite.
Implementation target:
Retry only after reload-and-merge or fail the save path with a handled concurrency result that the caller can retry safely.
10. Remove persistence-model drift by correcting the `SessionEntity` to `TenantEntity` mapping, generating the follow-up EF migration, and validating that no duplicate tenant FK remains in the schema.
11. Run end-to-end verification across unit, integration, and any necessary manual request flows for trusted host resolution, authenticated vs anonymous behavior, memory isolation, replay safety, transcript compaction, and agent-state concurrency.
12. Capture implementation evidence, review notes from a separate model/session, and any residual package constraints or product decisions that remain open.

## Review Focus
- Whether tenant/user/channel isolation is enforced consistently across Gateway, Logic, Data, and memory transport boundaries.
- Whether the chosen `/v1/*` authorization model is explicit, tested, and operationally supportable.
- Whether replay protection and concurrency handling preserve correctness without introducing hidden data loss.
- Whether transcript compaction and EF model cleanup align with ADR 0003 and do not leak new abstractions across layers.
