# Phase 20 Risk Register

## Risks

| ID | Risk | Impact | Mitigation | Status |
| --- | --- | --- | --- | --- |
| R1 | The policy core becomes a dumping ground for unrelated rules | Tight coupling and hard-to-test logic | Keep policy evaluation focused on stable identity, auth, memory, and budget contracts | Open |
| R2 | Event contracts are too low-level or too high-level | Poor reuse or excessive schema churn | Start with a minimal append-only spine and derive read models separately | Open |
| R3 | Gateway logic leaks back into core policy code | Harder deployment and test complexity | Enforce thin-host rules and review Gateway changes for transport-only concerns | Open |
| R4 | First-adopter migration reveals missing abstractions | Rework and delayed adoption | Pick one narrow consumer path and iterate before broad rollout | Open |
| R5 | A separate micro-service is requested too early | Latency, failure modes, and duplicated policy state | Keep the policy core in-process until operational scale justifies a split | Open |
| R6 | Canonical identity is interpreted as one partition key instead of a set of explicit runtime invariants | Cross-channel memory, transcript ownership, or anonymous isolation regress | Document where `PersonId`, `UserId`, `ChannelId`, and `SessionId` each remain authoritative and test those boundaries | Open |
| R7 | The new policy core creates a second authorization/data-partition path beside Phase 19 repositories | Fail-closed guarantees erode and query bypasses return | Require the policy core to compose with `IPermit<TEntity>` / `IFilter<TEntity>` / `IRepository<TEntity>` and reject parallel enforcement paths | Open |
| R8 | The event spine is treated as a drop-in replacement for current turn persistence without a migration model | Lossy history semantics around retries, compaction, soft-delete, and tool turns | Define envelope/projection rules and explicit coexistence/backfill boundaries before adoption | Open |

## Open Decisions
- Keep the policy core as a shared library until there is a measurable scaling or reuse need for a service split.
- Represent events as append-only domain records first, then add read models where needed.
- Preserve current identity partitioning invariants even while introducing a more explicit canonical identity contract.
