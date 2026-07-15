# Phase 10 Exit Criteria

## Gate Checklist
- [ ] A canonical person spans multiple channel-native identities within a tenant.
- [ ] With the default wildcard (`*`/`*`) policy, the same person's memory and preferences are unified across all their channels.
- [ ] Narrowing the `Share`/`Access` policy correctly partitions or isolates channels using directional AND (X readable in C iff X shares to C and C accesses X); a channel can be made fully memory-isolated.
- [ ] Reads never cross a channel boundary the policy forbids; writes always land only in the current channel's own scope.
- [ ] Channels that share memory never hold divergent 5W1H facts about the same entity: a fact updated in one channel supersedes/reconciles the same-entity fact across the mutually visible set, converging to one canonical current version with authoring provenance retained.
- [ ] Cross-channel conflict resolution is deterministic (newest/explicit supersession wins) and irreconcilable conflicts are flagged, not silently overwritten.
- [ ] Reconciliation runs when sharing is enabled/widened, converges already-divergent facts across the newly-shared set, and is dry-run-capable and reversible; isolated channels are allowed to keep divergent facts.
- [ ] Asymmetric (one-way) sharing is handled by non-destructive read-time overlay with deterministic precedence and provenance — never by cross-boundary merge/supersession — so writes in the shared-into channel never propagate back to the shared-from channel and revoking the grant leaves no residual state.
- [ ] Unlinked and anonymous identities remain fully isolated (no accidental merges).
- [ ] Identity linking requires verification and cannot hijack another person's memory.
- [ ] Memory scope is person-keyed and channel-retaining (`memory/{tenantId}/{personId}/{channelId}/{key}`); channelId remains a governed dimension, not dropped.
- [ ] The Phase 06 memory sharing policy is enforced here; this phase does not redefine the policy schema/config.
- [ ] Agent-session isolation remains a separate concern; this phase does not repurpose `IdentityIsolationKeyProvider` for memory scoping.
- [ ] Tenant isolation is preserved everywhere; cross-tenant linking is impossible.
- [ ] Existing memory is migrated to person-keyed, channel-retaining keys without loss and the migration is reversible.
- [ ] Scope-relative keys are still passed to `GBrainMemoryClient` (no double-prefixing).
- [ ] Unit + integration tests cover wildcard-default sharing, policy-narrowed isolation/partial sharing, cross-channel 5W1H reconciliation (no divergence; isolated channels diverge), conflict flagging, unlinked isolation, tenant safety, and migration.

## Approval Table

| Role | Name | Status | Notes |
| --- | --- | --- | --- |
| Owner | | Pending | |
| Reviewer | | Pending | |
| Approver | | Pending | |
