# Phase 10 Unified Identity And Cross-Channel Memory

## Companion Documents
- [Inputs](inputs.md)
- [Activities](activities.md)
- [Outputs](outputs.md)
- [Exit Criteria](exit-criteria.md)
- [Risk Register](risk-register.md)
- [Evidence](evidence.md)

## Objective
Make a user's memory, facts, and preferences follow them across every channel so the assistant is the same assistant whether reached via the web UI, Signal, email, or any future transport. Today identity is resolved per channel and memory is scoped `memory/{tenantId}/{userId}/{channelId}/{key}`, so the same human on two channels gets two disconnected memories. This phase introduces a canonical person identity above channel-native identities, an identity-linking (account-merge) flow, and a shift of memory/preference scope from channel-scoped to person-scoped — while preserving tenant isolation and per-channel audit.

## Scope
This phase changes the identity and memory-scoping model and adds a verified linking flow. It covers memory, preferences, and long-term facts (which should be shared across channels). It deliberately keeps transcript history and MAF agent-session state configurable (shared vs per-channel-session) because conversation continuity is a distinct concern from durable memory. It does not add new channels, connectors, or tools.

## In Scope
- A canonical person concept: either a `PersonEntity` above `UserEntity`, or promotion of `UserEntity` to a cross-channel principal with channel-native identifiers linked to it (design decision recorded in `risk-register.md`).
- Channel-identity records mapping each channel-native identifier (OIDC subject, Signal number, email address) to the canonical person, with a linking/verification flow (e.g., one-time code) to merge identities safely.
- Memory scope change: derive the memory namespace/slug from the person id (`memory/{tenantId}/{personId}/{key}`), removing `channelId` from the memory isolation dimension while retaining channel in metadata for provenance/audit.
- Preference profile store keyed at the person level, available to context assembly on every channel.
- Backfill/migration of existing channel-scoped memory to person-scoped keys, with a reversible, dry-run-capable migration.
- Update `IPermit`, `IdentityResolver`, and memory-specific scope types so resolution yields a person id and channel id. Keep `IdentityIsolationKeyProvider` aligned to agent-session isolation unless a separate history/session-sharing design is explicitly approved.
- Configuration flags for scope mode (person-scoped memory on/off), history-sharing policy, and linking requirements; startup validation.
- Tests proving the same person on two channels shares memory and preferences, that unlinked identities remain isolated, and that tenant isolation is never crossed.

## Out of Scope
- New channel adapters (Phase 06) or connectors (Phase 11).
- Sharing raw transcript history across channels by default (configurable, not mandated here).
- Cross-tenant identity linking (explicitly forbidden).

## Entry Criteria
- Identity partitioning and the memory pipeline are operational: `IdentityResolver`, `RequestContextPermit`, `IdentityIsolationKeyProvider`, `MemoryProvider`, `GBrainMemoryClient`.
- Persistence layer can add entities/migrations (`EntityContext`).
- Phase 06 channels context understood so linking works for non-HTTP transports.

## Exit Criteria
The same verified person is recognized across channels, their memory and preferences are shared person-scoped, unlinked identities stay isolated, tenant boundaries hold, and existing memory is migrated without loss. See `exit-criteria.md`.

## Roles
- Owner: Rebuild maintainer
- Reviewer: Separate agent session / model review
- Approver: Repository owner
