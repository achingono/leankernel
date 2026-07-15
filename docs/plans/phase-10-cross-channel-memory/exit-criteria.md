# Phase 10 Exit Criteria

## Gate Checklist
- [ ] A canonical person spans multiple channel-native identities within a tenant.
- [ ] After linking, the same person's memory and preferences are shared across all their channels.
- [ ] Unlinked and anonymous identities remain fully isolated (no accidental merges).
- [ ] Identity linking requires verification and cannot hijack another person's memory.
- [ ] Memory scope is person-scoped (`memory/{tenantId}/{personId}/{key}`) with channel retained only as provenance.
- [ ] Agent-session isolation remains a separate concern; this phase does not repurpose `IdentityIsolationKeyProvider` for memory scoping.
- [ ] Tenant isolation is preserved everywhere; cross-tenant linking is impossible.
- [ ] Existing channel-scoped memory is migrated without loss and the migration is reversible.
- [ ] Scope-relative keys are still passed to `GBrainMemoryClient` (no double-prefixing).
- [ ] Unit + integration tests cover cross-channel sharing, isolation, tenant safety, and migration.

## Approval Table

| Role | Name | Status | Notes |
| --- | --- | --- | --- |
| Owner | | Pending | |
| Reviewer | | Pending | |
| Approver | | Pending | |
