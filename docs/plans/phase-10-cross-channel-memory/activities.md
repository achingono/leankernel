# Phase 10 Activities

## Step-By-Step Activities
1. Confirm the current scoping: `GBrainMemoryClient` builds `memory/{tenantId}/{userId}/{channelId}/{key}` and `IdentityResolver` creates channel-specific identities; document the exact isolation dimensions used by memory, history, and agent state.
2. Decide the identity model (record in `risk-register.md`): a new `PersonEntity` parent, or promote `UserEntity` to the person with linked channel-identity rows. Define a `ChannelIdentity` record mapping `(tenant, channel, native-identifier) -> personId`.
3. Add entities/migrations for the person and channel-identity mapping; keep tenant as the top isolation boundary.
4. Implement identity resolution changes so a request yields both a `personId` and a `channelId`; new channel identities auto-provision an unlinked person, existing linked identities resolve to the shared person.
5. Implement a verified linking flow: initiate from an authenticated surface, deliver a one-time code to the target channel, and merge the channel identity into the existing person on confirmation. Support unlink.
6. Change memory scope to person-scoped: update `MemoryScope` and `GBrainMemoryClient` to build `memory/{tenantId}/{personId}/{key}`; retain channel id in page metadata for provenance, not isolation.
7. Add a person-scoped preference profile and surface it to context assembly on every channel.
8. Keep `IdentityIsolationKeyProvider` on the agent-session path unless a separate history/session-sharing design is approved. Limit this phase's scope change to `IPermit`, `MemoryScope`, `MemoryProvider`, `GBrainMemoryClient`, and the new person/profile resolution path.
9. Implement a reversible, dry-run-capable migration that rewrites existing channel-scoped memory keys to person-scoped keys, merging duplicates deterministically.
10. Add configuration (person-scoped memory toggle, history-sharing policy, linking requirements) and startup validation.
11. Add tests: same person across two channels shares memory + preferences; unlinked identities isolated; tenant never crossed; migration correctness and reversibility.
12. Update `docs/features/identity-partitioning.md` and `memory-pipeline.md` to document person-scoped memory and linking.

## Review Focus
- Tenant isolation is never weakened by the person layer.
- Memory truly follows the person across channels after linking.
- Unlinked/anonymous identities remain isolated (no accidental merging).
- Linking requires verification and cannot be spoofed to hijack another person's memory.
- Migration is loss-free, deterministic, and reversible.
- Scope-relative keys still passed to `GBrainMemoryClient` (no double-prefixing).
