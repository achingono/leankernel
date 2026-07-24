# Phase 10 Configurable Cross-Channel Memory

## Companion Documents
- [Inputs](inputs.md)
- [Activities](activities.md)
- [Outputs](outputs.md)
- [Exit Criteria](exit-criteria.md)
- [Risk Register](risk-register.md)
- [Evidence](evidence.md)

## Objective
Let a user control whether their memory, facts, and preferences follow them across channels — sharing by default, but configurable down to per-channel isolation. Today identity is resolved per channel and memory is scoped `memory/{tenantId}/{userId}/{channelId}/{key}`, so the same human on two channels gets two disconnected memories with no way to share. This phase introduces (1) a canonical person identity above channel-native identities with a verified linking flow so the system knows two channels belong to the same human, and (2) a **configurable, directional cross-channel memory policy** that decides which channels' memories are mutually visible. Memory keeps a channel dimension (`memory/{tenantId}/{personId}/{channelId}/{key}`); reads span the set of channels permitted by the policy defined and persisted in Phase 06. The default policy is wildcard in both directions, which yields fully unified cross-channel memory; explicit configuration narrows it toward isolation. Tenant isolation and per-channel provenance are always preserved.

## Scope
This phase changes the identity model (adds a canonical person and verified linking) and the memory-scoping/read model (person-keyed writes retaining channel, policy-driven multi-channel reads). It **enforces** the per-channel memory sharing/isolation policy whose schema, defaults, persistence, and resolution contract are defined in Phase 06. It covers memory, preferences, and long-term facts, and it guarantees 5W1H fact consistency across channels that share memory (reconciling same-entity facts so they do not diverge) while allowing isolated channels to differ. It deliberately keeps transcript history and MAF agent-session state configurable (shared vs per-channel-session) because conversation continuity is a distinct concern from durable memory. It does not add new channels, connectors, or tools, and it does not re-define the policy schema (that is Phase 06).

## In Scope
- A canonical person concept: either a `PersonEntity` above `UserEntity`, or promotion of `UserEntity` to a cross-channel principal with channel-native identifiers linked to it (design decision recorded in `risk-register.md`).
- Channel-identity records mapping each channel-native identifier (OIDC subject, Signal number, email address) to the canonical person, with a linking/verification flow (e.g., one-time code) to merge identities safely. These records build on Phase 06's per-channel identity resolution: the channel-native sender first resolves to a persisted `UserEntity` (via issuer/subject) within the channel's tenant scope, and that user is then linked to the canonical person — tenant remains the top boundary and cross-tenant linking is impossible.
- Person-keyed memory scope that **retains the channel dimension**: derive the memory namespace/slug as `memory/{tenantId}/{personId}/{channelId}/{key}`. `channelId` remains a first-class isolation/provenance dimension so cross-channel visibility can be governed rather than always collapsed.
- Policy-driven memory reads: on read, resolve the effective set of readable channels for the current channel from the Phase 06 policy resolution contract (directional AND — channel X's memory is readable in channel C iff X shares to C and C accesses X; C always reads itself), then search across those channel namespaces for the person. Writes always target the current channel's own scope.
- Wildcard default behavior: with the default `*`/`*` policy, the effective readable set is all of the person's channels, giving unified cross-channel memory; narrowing the policy yields partial sharing or full per-channel isolation without code changes.
- **Cross-channel 5W1H consistency and fact reconciliation.** Because the scope-relative key is `facts/{dim}/{subject-slug}/{factId}` and supersession (`SupersededBy`/`Supersedes`, `IsRetired`) currently chains only within one channel namespace, sharing must not let two channels hold divergent 5W1H facts about the same entity. This phase makes normalization/supersession operate across the *mutually visible* channel set (channels visible to each other under the directional AND policy): when a fact is written in one channel, related-page lookup, dimension classification, and supersession consider the same-subject/same-dimension pages in every mutually visible channel, so an update supersedes/merges the prior fact and the 5W1H fields converge to a single canonical current version. Authoring channel is retained as provenance metadata. Conflicts are resolved deterministically (newest `EffectiveTimestamp` / explicit supersession wins); genuinely irreconcilable conflicts are flagged rather than silently overwritten. Channels that do **not** share may keep divergent facts about the same entity (the "unless explicitly defined" case).
- A reconciliation pass that runs when the sharing policy is enabled or widened between channels, converging already-divergent same-entity facts across the newly-shared set (deterministic supersede/merge, provenance retained, conflicts flagged), and that is dry-run-capable.
- **Asymmetric (one-way) visibility handling.** When visibility is not mutual (e.g., A's memory is readable in B but B's is not readable in A), do **not** merge or supersede across the boundary — merge is reserved for the mutually visible set. Instead resolve at read time via a **non-destructive overlay**: the reading channel fans in the shared-from facts and applies deterministic precedence (newest `EffectiveTimestamp`, or local-channel-authored wins) with provenance labels distinguishing own-memory from shared-in memory. No page in either store is mutated, so writes never leak across the one-way boundary and dropping the `Access`/`Share` grant cleanly removes the overlay with no residual merged state.
- Preference profile store keyed at the person level, with the same policy-driven cross-channel visibility applied consistently with memory.
- Backfill/migration of existing `memory/{tenantId}/{userId}/{channelId}/{key}` pages to `memory/{tenantId}/{personId}/{channelId}/{key}` (retaining channelId), with a reversible, dry-run-capable migration.
- Update `IPermit`, `IdentityResolver`, and memory-specific scope types so resolution yields a person id and channel id, and the memory scope carries both. Keep `IdentityIsolationKeyProvider` aligned to agent-session isolation unless a separate history/session-sharing design is explicitly approved.
- Configuration flags for person-linking requirements and history-sharing policy, plus startup validation; the memory sharing policy schema/defaults themselves live in Phase 06's channel configuration.
- Tests proving: with the wildcard default the same person shares memory and preferences across two channels; explicit `Share`/`Access` narrowing correctly isolates or partially shares channels using directional AND; unlinked identities remain isolated; tenant isolation is never crossed; and — critically — that a fact updated in one channel supersedes/reconciles the same-entity 5W1H fact in every mutually shared channel (no divergent facts), while isolated channels are allowed to diverge.

## Out of Scope
- New channel adapters (Phase 06) or connectors (Phase 11).
- Defining the memory sharing policy schema/config/persistence (owned by Phase 06; this phase only enforces it).
- Sharing raw transcript history across channels by default (configurable, not mandated here).
- Cross-tenant identity linking (explicitly forbidden).

## Entry Criteria
- Identity partitioning and the memory pipeline are operational: `IdentityResolver`, `RequestContextPermit`, `IdentityIsolationKeyProvider`, `MemoryProvider`, `GBrainMemoryClient`.
- Persistence layer can add entities/migrations (`EntityContext`).
- Phase 06 channels context understood, including the per-channel memory sharing policy schema, persistence, and resolution contract that this phase enforces.

## Exit Criteria
The same verified person is recognized across channels; with the default wildcard policy their memory and preferences are unified across channels, and narrowing the `Share`/`Access` policy correctly partitions or isolates channels using directional AND semantics; unlinked identities stay isolated; tenant boundaries hold; and existing memory is migrated to person-keyed, channel-retaining keys without loss. See `exit-criteria.md`.

## Design Delta: Intelligent Brain Track
- Add stronger entity-link confidence model and review workflow for cross-channel merge/split corrections.
- Add deterministic alias resolution and provenance tracking so entity identity decisions are auditable and reversible.
- Add reconciliation hooks for synthesized document facts so cross-channel unification preserves person/channel policy boundaries.
- Add regression tests for high-risk identity collisions (same display name, reused phone/email identifiers, and stale bindings).

## Roles
- Owner: Rebuild maintainer
- Reviewer: Separate agent session / model review
- Approver: Repository owner
