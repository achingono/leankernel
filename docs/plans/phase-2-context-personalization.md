# PRD: Phase 2 Context Personalization

## Executive Summary

Phase 2 adds durable identity grounding, guided onboarding, scoped retrieval, deterministic history shaping, context diagnostics, and channel expansion to the LeanKernel rearchitecture. This phase builds on the Phase 1 foundation that already delivers the MAF-native runtime, deny-by-default context gating, GBrain knowledge, PostgreSQL persistence, and the API gateway.

The outcome of Phase 2 is an API-first personalization layer that makes prompt construction more useful, more inspectable, and more predictable without introducing UI work. Identity becomes part of GBrain instead of a separate context path, retrieval becomes explicitly scoped, history becomes budget-aware and traceable, diagnostics become queryable over HTTP, and non-UI inbound channels reconnect to the same `IAgentRuntime` entry point.

## Problem Statement

Phase 1 proves that LeanKernel can receive a request, assemble context, execute a MAF-native agent turn, and persist durable state. It does not yet fully solve five product-defining needs:

1. The system lacks a single durable identity source that can ground both agent behavior and user personalization.
2. Knowledge retrieval is searchable but not yet governed by explicit per-agent or per-task retrieval boundaries.
3. Conversation history can grow faster than the history budget and is not yet shaped with deterministic, auditable tiers.
4. Operators cannot inspect a complete per-turn context audit through stable API contracts.
5. Inbound channel support needs to reconnect to Signal and normalize all inbound messages through a shared channel abstraction and runtime path.

Without these capabilities, LeanKernel remains functional but not yet sufficiently personalized, explainable, or production-ready for API-driven personal agent use.

## Phase Scope

### In Scope

- GBrain-backed identity pages for agent profile and user preferences.
- Guided onboarding for missing or weak identity data.
- Scoped knowledge retrieval with deterministic policy enforcement.
- Entity-aware retrieval expansion using GBrain relationship edges.
- Deterministic history shaping and persisted compaction markers.
- Context diagnostics APIs for context, budget, and history decisions.
- Inbound channel abstraction hardening and Signal daemon reconnection.
- Per-channel authentication and authorization.

### Out of Scope

- Blazor or other UI work.
- New end-user onboarding screens.
- Multi-tenant identity modeling beyond the existing Phase 1 principal/session model.
- New external graph database infrastructure.
- Model routing, quality gates, or autonomous workflow expansion from later phases.
- Replacing Qdrant or the existing GBrain/indexing stack.

## Goals

- Make identity a first-class, durable, queryable input to prompt construction.
- Detect missing personalization early and guide users through lightweight onboarding questions.
- Prevent retrieval leakage by enforcing explicit scope policy before knowledge enters context.
- Keep history inside budget using deterministic tiering and traceable compaction artifacts.
- Expose machine-readable diagnostics for every context assembly decision.
- Route inbound API and channel traffic through the same `IAgentRuntime` path.

## Non-Goals

- Building a general-purpose profile management UI.
- Introducing non-deterministic or hidden retrieval expansion rules.
- Allowing unrestricted fallback from scoped retrieval to global retrieval.
- Preserving legacy `SELF.md` and `USER.md` files as long-term sources of truth.
- Creating channel-specific business logic outside the shared runtime path.

## Phase 1 Dependencies and Assumptions

Phase 2 assumes the following Phase 1 capabilities are already working and available:

- `IAgentRuntime` is the canonical turn execution entry point.
- GBrain knowledge storage, retrieval, and indexing are operational.
- PostgreSQL-backed persistence exists for sessions and related durable state.
- Deny-by-default context gating and token budgeting already run in production.
- The API gateway already enforces authentication for first-party HTTP callers.
- Qdrant-backed semantic search and GBrain wiki/page retrieval are available.

Additional assumptions for Phase 2:

- GBrain pages are persisted in PostgreSQL and may optionally be exported as markdown for portability, but PostgreSQL is the runtime source of truth.
- Existing legacy identity files are treated as migration inputs only.
- Diagnostics snapshots are stored at turn assembly time rather than recomputed later from mutable state.

## Product Principles for Phase 2

1. **MAF-native first** — channel ingress and turn execution must continue to flow through native MAF-aligned agent runtime concepts.
2. **Deterministic where it matters** — scope enforcement, budget slicing, tier assignment, and diagnostics reason codes must be deterministic and auditable.
3. **Identity is context, not a sidecar** — identity should live in the same GBrain ecosystem as other durable knowledge.
4. **Explainability by default** — every included or excluded context artifact must have a machine-readable reason.
5. **API-only delivery** — every Phase 2 capability must be usable and testable without a UI.

## Architecture Overview

| Component | Responsibility |
| --- | --- |
| `IdentityProvider` | Reads agent and user identity pages from GBrain and emits structured prompt segments. |
| `OnboardingGapDetector` | Detects missing, stale, weak-confidence, or conflicting identity attributes. |
| `OnboardingDirectiveBuilder` | Produces deterministic onboarding instructions when identity gaps are present. |
| `IdentityUpdateProjector` | Extracts identity updates from conversation output and writes approved updates back to GBrain. |
| `RetrievalScopePolicy` | Resolves effective retrieval boundaries from agent defaults, task overlays, and request context. |
| `ScopedKnowledgeService` | Executes GBrain/vector retrieval under scope policy and emits diagnostics for considered, selected, and excluded items. |
| `HistoryCompactionStrategy` | Assigns history tiers and enforces the history slice of the token budget. |
| `ConversationCompactor` | Produces compacted summaries using deterministic prompts and persists compaction markers. |
| `ContextDiagnosticsService` | Stores and serves per-turn diagnostics snapshots for context, budget, and history decisions. |
| `IChannel` adapters | Normalize inbound/outbound channel traffic into a shared message contract. |
| `ChannelRouter` | Authenticates channel messages and dispatches them to `IAgentRuntime`. |

### High-Level Flow

1. Inbound HTTP or channel traffic arrives with an authenticated principal.
2. `ChannelRouter` or the API gateway converts the request into a `LeanKernelMessage` and invokes `IAgentRuntime`.
3. `IdentityProvider` loads the agent profile page and user preference page from GBrain.
4. `OnboardingGapDetector` evaluates identity coverage and `OnboardingDirectiveBuilder` emits onboarding guidance if needed.
5. `RetrievalScopePolicy` resolves the effective knowledge scope.
6. `ScopedKnowledgeService` retrieves candidate knowledge, applies entity-aware expansion, filters by scope, and records diagnostics.
7. `HistoryCompactionStrategy` selects verbatim, compacted, and summarized history artifacts within budget.
8. Prompt construction proceeds with identity, scoped knowledge, history, and instructions.
9. After the turn, diagnostics snapshots and any approved identity updates are persisted.

---

## FR-1: Identity and Onboarding

LeanKernel must ground every turn in durable GBrain identity artifacts and guide onboarding whenever required identity information is missing or weak.

### Requirements

- Identity must be stored as two first-class GBrain pages:
  - agent profile page
  - user preference page
- Identity pages must live in the same storage, indexing, and retrieval system as other GBrain content to simplify context management.
- `IdentityProvider` must read identity from GBrain and provide structured segments for prompt construction.
- `OnboardingGapDetector` must detect:
  - missing fields
  - placeholder values
  - stale values
  - low-confidence values
  - contradictory values
- `OnboardingDirectiveBuilder` must generate deterministic onboarding instructions when gaps are present.
- Identity updates discovered during conversation must be extracted and written back to the corresponding GBrain page.
- Identity updates must follow an allowlisted schema and must not permit arbitrary page mutation.
- Identity writeback must support conflict handling:
  - append low-confidence candidate facts
  - preserve higher-confidence existing facts
  - record conflict diagnostics instead of silently overwriting trusted values
- Legacy `SELF.md` and `USER.md` content must be migrated into GBrain-backed identity pages during rollout, then treated as compatibility export only.

### Identity Storage Contract

Identity pages remain part of GBrain but use explicit page metadata instead of generic free-form wiki facts.

#### Agent Profile Page

```yaml
id: identity-agent-main
pageType: identity-agent-profile
subject: main-agent
scope: private
sourceOfTruth: gbrain
```

Recommended sections:

- name and role
- communication style
- operating principles
- capabilities and boundaries
- preferred response style
- stable task preferences

#### User Preference Page

```yaml
id: identity-user-default
pageType: identity-user-preferences
subject: primary-user
scope: private
sourceOfTruth: gbrain
```

Recommended sections:

- preferred name
- timezone / locale
- communication preferences
- recurring goals
- work style
- tool/autonomy preferences

### Component Responsibilities

| Component | Responsibility |
| --- | --- |
| `IdentityProvider` | Loads identity pages, normalizes them into prompt-safe segments, and exposes provenance. |
| `OnboardingGapDetector` | Scores identity completeness and emits gap codes such as `missing_name`, `missing_timezone`, `weak_agent_role`. |
| `OnboardingDirectiveBuilder` | Produces a short instruction block that asks at most two focused follow-up questions per turn. |
| `IdentityUpdateProjector` | Validates extracted updates against an allowlist and writes approved updates back to GBrain. |
| `ContextDiagnosticsService` | Records which identity fields were included, omitted, or flagged as weak. |

### Behavioral Rules

- Identity must be loaded before retrieval and history shaping so personalization can influence both.
- Onboarding prompts must only trigger when the identity confidence score is below the configured threshold.
- Onboarding must be additive, not blocking: the system should still attempt to answer the user while collecting missing identity data.
- Identity extraction must use a fixed output schema, deterministic field names, and low-temperature generation settings.
- Identity pages must be tagged as private and excluded from unscoped semantic recall unless explicitly requested by the identity provider.

### Acceptance Criteria

- **AC-1.1:** When the user preference page lacks a preferred name or timezone, the first eligible turn includes onboarding guidance asking for one or both fields.
- **AC-1.2:** `IdentityProvider` injects agent profile and user preference segments into prompt construction with source metadata.
- **AC-1.3:** When a user says "Call me Alex" or "I am in UTC+2," the extracted identity update is persisted to the user preference page and becomes available on the next turn.
- **AC-1.4:** Conflicting identity updates do not overwrite higher-confidence values without recording a conflict reason in diagnostics.
- **AC-1.5:** After migration, the runtime reads identity from GBrain as the source of truth; legacy identity files are optional exports, not the active store.

---

## FR-2: Scoped Knowledge Retrieval

LeanKernel must retrieve only the knowledge allowed for the active agent and task, while preserving explainability for what was considered, selected, and excluded.

### Requirements

- `RetrievalScopePolicy` must define the effective retrieval scope for each turn.
- Scope policy must support inputs from:
  - agent defaults
  - task or route overlay
  - channel restrictions
  - explicit source-type filters
- `ScopedKnowledgeService` must apply scope policy before knowledge enters prompt construction.
- Scope policy must support allow and deny rules for:
  - tags
  - page types
  - source types
  - entities
  - maximum expansion depth
- Entity-aware context boost must expand from directly matched entities to related GBrain entities using stored relationship edges and backlinks.
- Entity expansion must be bounded by deterministic depth and score rules; no unbounded graph walk is allowed.
- Retrieval diagnostics must include what was considered, scored, selected, expanded, and excluded.
- If scope policy yields zero selected results, the system must return an empty scoped result set and a diagnostic reason; it must not silently fall back to global retrieval.

### Scope Policy Contract

```json
{
  "policyId": "research-default",
  "allowedTags": ["wiki", "projects", "identity"],
  "deniedTags": ["admin", "secrets"],
  "allowedSourceTypes": ["wiki", "document"],
  "entityExpansionDepth": 1,
  "maxCandidates": 20,
  "allowWildcard": false
}
```

### Retrieval Diagnostics Reason Codes

Required exclusion or decision codes include:

- `out_of_scope_tag`
- `out_of_scope_source`
- `out_of_scope_entity`
- `low_score`
- `duplicate`
- `over_budget`
- `superseded_by_higher_rank`
- `expanded_from_entity`

### Component Responsibilities

| Component | Responsibility |
| --- | --- |
| `RetrievalScopePolicy` | Resolves the effective scope from agent/task/channel inputs. |
| `ScopedKnowledgeService` | Executes the scoped search, merges direct and expanded candidates, and returns diagnostics-rich results. |
| `EntityExpansionService` | Traverses GBrain relationship edges/backlinks up to the configured depth. |
| `ContextDiagnosticsService` | Persists considered, selected, and excluded retrieval artifacts with reason codes. |

### Behavioral Rules

- Scope policy evaluation must happen before candidate ranking enters the prompt assembly budget pass.
- Entity-aware boost may raise score or candidate priority, but the original score and boosted score must both be preserved in diagnostics.
- Expansion must operate on existing GBrain relationships stored in PostgreSQL/GBrain metadata; Phase 2 must not introduce a separate graph database.
- Identity pages may be included in scoped retrieval only when the policy explicitly permits `identity` content.

### Acceptance Criteria

- **AC-2.1:** A request executed under a restricted scope never includes knowledge tagged outside the allowed policy set.
- **AC-2.2:** When a query mentions an entity with linked GBrain neighbors, the service can include directly related entries up to the configured expansion depth and records the expansion path.
- **AC-2.3:** Diagnostics for a retrieval decision show considered, boosted, selected, and excluded candidates with deterministic reason codes.
- **AC-2.4:** When no candidates survive scope filtering, the response metadata records an empty scoped result rather than falling back to unrestricted retrieval.
- **AC-2.5:** Policy resolution is deterministic for the same agent, task, and channel inputs.

---

## FR-3: Deterministic History Shaping

LeanKernel must shape history into deterministic tiers so history remains useful, budget-safe, and traceable.

### Requirements

- `HistoryCompactionStrategy` must support three configurable tiers:
  - recent turns: verbatim
  - older turns: compacted into key points
  - very old turns: summarized or dropped
- The strategy must enforce the history slice of the overall token budget before final prompt assembly.
- `ConversationCompactor` must use deterministic prompt templates and fixed output schema when generating compacted history artifacts.
- Tier assignment rules must be deterministic and independent of model output.
- Compaction must be idempotent: already-compacted ranges must not be compacted again unless the strategy version changes.
- Persisted history must carry compaction markers for traceability.
- Diagnostics must show which turns were kept verbatim, compacted, summarized, or dropped and why.

### Default Strategy

| Tier | Default Shape | Default Rule |
| --- | --- | --- |
| Verbatim | raw turns | newest 16 messages (8 user/assistant pairs) |
| Compacted | key-point blocks | next 48 messages, grouped into stable ranges |
| Summary | summary block or dropped | anything older than compacted range |

All thresholds must be configurable under `LeanKernel:Context:HistoryCompaction`.

### Persisted Compaction Marker Contract

```json
{
  "sessionId": "sess_123",
  "rangeStart": 1,
  "rangeEnd": 12,
  "tier": "compacted",
  "strategyVersion": "v1",
  "compactedAt": "2026-05-22T10:00:00Z",
  "sourceChecksum": "sha256:...",
  "summaryMessageId": "hist_456"
}
```

### Behavioral Rules

- Recent turns must always be evaluated first and preserved verbatim if they fit the history slice.
- Compacted blocks must be inserted from newest to oldest until the history budget is exhausted.
- Summary blocks must be considered only after verbatim and compacted blocks are placed.
- The current inbound user message must never be duplicated in history.
- History shaping must preserve enough provenance to reconstruct what source range produced each compacted artifact.
- Summarization prompts must use low temperature and a fixed instruction template, but determinism claims apply to tier assignment and marker generation, not exact summary wording.

### Acceptance Criteria

- **AC-3.1:** Replaying the same session through `HistoryCompactionStrategy` yields the same tier assignment and range boundaries.
- **AC-3.2:** The total selected history remains inside the history budget slice for the configured model context window.
- **AC-3.3:** Persisted history records contain compaction markers that identify tier, source range, timestamp, and strategy version.
- **AC-3.4:** Running compaction twice without source-history changes does not create duplicate compacted artifacts.
- **AC-3.5:** Diagnostics for a turn expose why each historical segment was kept, compacted, summarized, or dropped.

---

## FR-4: Context Diagnostics API

LeanKernel must expose structured, machine-readable diagnostics for context assembly decisions through API-only endpoints.

### Requirements

- The API must expose these endpoints:
  - `GET /api/diagnostics/{sessionId}/context`
  - `GET /api/diagnostics/{sessionId}/budget`
  - `GET /api/diagnostics/{sessionId}/history`
- Each endpoint must support querying the latest turn by default and a specific turn via query parameter.
- Diagnostics must be sourced from persisted per-turn snapshots rather than recomputed from mutable state.
- Response models must be stable, documented, and versionable.
- Diagnostics access must require either:
  - the owning principal, or
  - an admin/operator scope such as `diagnostics.read`
- Diagnostics must expose included items, excluded items, and reason codes.

### Response Models

#### `GET /api/diagnostics/{sessionId}/context`

```json
{
  "sessionId": "sess_123",
  "turnId": "turn_045",
  "assembledAt": "2026-05-22T10:00:00Z",
  "identity": [
    {
      "sourcePageId": "identity-user-default",
      "field": "preferred_name",
      "included": true,
      "reasonCode": "identity_required"
    }
  ],
  "retrieval": [
    {
      "candidateId": "wiki-project-atlas",
      "sourceType": "wiki",
      "rawScore": 0.82,
      "finalScore": 0.89,
      "included": true,
      "reasonCode": "selected"
    }
  ],
  "excluded": [
    {
      "candidateId": "doc-admin-runbook",
      "included": false,
      "reasonCode": "out_of_scope_tag"
    }
  ]
}
```

#### `GET /api/diagnostics/{sessionId}/budget`

```json
{
  "sessionId": "sess_123",
  "turnId": "turn_045",
  "totalBudgetTokens": 24000,
  "reservedResponseHeadroom": 8000,
  "categories": [
    { "name": "system", "allocated": 3600, "used": 3100 },
    { "name": "identity", "allocated": 1800, "used": 1200 },
    { "name": "history", "allocated": 9600, "used": 8700 },
    { "name": "retrieval", "allocated": 4800, "used": 4100 },
    { "name": "tools", "allocated": 1200, "used": 300 }
  ]
}
```

#### `GET /api/diagnostics/{sessionId}/history`

```json
{
  "sessionId": "sess_123",
  "turnId": "turn_045",
  "strategyVersion": "v1",
  "items": [
    {
      "rangeStart": 29,
      "rangeEnd": 44,
      "tier": "verbatim",
      "included": true,
      "reasonCode": "recent_window"
    },
    {
      "rangeStart": 1,
      "rangeEnd": 28,
      "tier": "compacted",
      "included": true,
      "reasonCode": "compacted_to_fit_budget"
    }
  ]
}
```

### Behavioral Rules

- Diagnostics must align to the exact turn that was executed and must include a correlation identifier.
- Missing diagnostics for an executed turn are a correctness issue, not an optional condition.
- Sensitive values in diagnostics must be masked where they would reveal secrets or unauthorized identity details.
- Diagnostics models must remain UI-agnostic and usable by scripts, tests, and future tooling.

### Acceptance Criteria

- **AC-4.1:** Each completed turn persists a diagnostics snapshot that can be retrieved through all three endpoints.
- **AC-4.2:** `GET /context` returns included and excluded context artifacts with deterministic reason codes.
- **AC-4.3:** `GET /budget` returns per-category allocations, usage, and reserved response headroom.
- **AC-4.4:** `GET /history` returns tier decisions, source ranges, and compaction marker references.
- **AC-4.5:** Unauthorized callers cannot read diagnostics for sessions they do not own.

---

## FR-5: Channel Expansion

LeanKernel must normalize inbound message sources behind `IChannel`, reconnect to Signal, and dispatch authenticated inbound traffic through `IAgentRuntime`.

### Requirements

- `IChannel` must remain the shared abstraction for inbound and outbound message sources.
- A Signal channel adapter must reconnect to the Signal daemon and resume inbound processing after transient failures.
- `ChannelRouter` must dispatch inbound messages to `IAgentRuntime` instead of bypassing the canonical runtime path.
- Per-channel authentication and authorization must be enforced before a message is accepted for processing.
- Channel metadata must be normalized into a consistent principal model that includes:
  - channel id
  - sender id
  - authenticated principal id if available
  - authorization result
- Channel failures, reconnect attempts, and authorization decisions must be observable in logs and diagnostics.

### Channel Security Model

| Channel Type | Authentication | Authorization |
| --- | --- | --- |
| API / HTTP | existing gateway token or session auth | API scopes and session ownership |
| Signal | daemon identity plus sender normalization | allowlist, denylist, or mapped principal policy |
| Future channels | adapter-specific auth | shared channel policy evaluation |

### Behavioral Rules

- Unauthorized senders must be rejected before `IAgentRuntime` execution.
- Signal reconnect behavior must use deterministic retry and backoff settings from configuration.
- Channel adapters must not embed business logic; they translate transport events into `LeanKernelMessage` envelopes.
- Routing must preserve correlation ids so diagnostics and channel delivery can be traced end to end.

### Acceptance Criteria

- **AC-5.1:** An inbound Signal message is received, authorized, routed through `IAgentRuntime`, and answered through the same channel path.
- **AC-5.2:** When the Signal daemon disconnects, the adapter retries according to configured backoff and resumes processing without process restart.
- **AC-5.3:** Messages from unauthorized senders are rejected before runtime execution and produce an audit/diagnostic event.
- **AC-5.4:** `ChannelRouter` uses the shared runtime path so API and channel turns produce equivalent diagnostics artifacts.
- **AC-5.5:** Per-channel authn/authz rules are configurable without changing channel adapter code.

---

## Non-Functional Requirements

### NFR-1: Performance

- Identity lookup and onboarding-gap evaluation must add no more than 75 ms p95 to turn assembly when pages are warm.
- Diagnostics API reads must complete in under 250 ms p95 from persisted snapshots.
- Scoped retrieval filtering and entity expansion must complete within the existing turn budget and must not trigger unbounded graph traversal.

### NFR-2: Determinism and Auditability

- Scope resolution, history tier assignment, budget calculations, and diagnostics reason codes must be deterministic for the same inputs.
- Every included or excluded context artifact must have a machine-readable reason code.
- Compaction markers and retrieval decisions must be persisted with strategy/policy versions.

### NFR-3: Reliability

- Missing diagnostics snapshots for successful turns must remain below 0.1% of completed turns.
- Signal reconnect must recover automatically from transient disconnects without manual intervention.
- Identity writeback failures must not fail the user-facing turn; they must be logged and retried or surfaced in diagnostics.

### NFR-4: Security and Privacy

- Identity pages and diagnostics must respect session ownership and authorization scopes.
- Private identity content must never leak into unscoped retrieval results.
- Sensitive values in diagnostics and logs must be masked when not required for troubleshooting.

### NFR-5: Maintainability and Extensibility

- New configuration must follow existing `LeanKernel:*` binding conventions.
- Phase 2 must reuse existing contracts in `LeanKernel.Core.Interfaces` where practical.
- No new external database class is introduced for entity traversal; expansion must build on existing GBrain/PostgreSQL metadata.

## Dependencies

- Phase 1 MAF-native runtime and `IAgentRuntime`.
- Phase 1 GBrain/wiki store and Qdrant-backed search.
- PostgreSQL persistence for sessions, diagnostics snapshots, and identity pages.
- Existing token estimation and budget infrastructure.
- API gateway authentication and authorization.
- Signal daemon availability and channel configuration.
- Background processing for post-turn identity updates and history compaction.

## Risks and Mitigations

| Risk | Mitigation |
| --- | --- |
| Identity writeback introduces noisy or incorrect personalization | Use allowlisted fields, confidence thresholds, conflict detection, and diagnostics reviewability. |
| Scoped retrieval is too restrictive and starves the model of useful context | Return explicit empty-scope diagnostics, allow policy tuning, and measure selected-result rates before widening defaults. |
| History summarization becomes non-repeatable | Keep deterministic tier assignment, fixed prompts, low temperature, and persisted strategy versions/checksums. |
| Diagnostics snapshots become too large | Store normalized diagnostics records and cap per-category candidate counts with truncation markers. |
| Signal reconnect loops or duplicate processing | Use bounded retry policy, idempotent message handling, and reconnect telemetry. |

## Success Metrics

### Product Metrics

- At least 85% of new sessions collect the minimum onboarding identity set (preferred name, timezone or locale, and at least one communication preference) within the first three turns.
- At least 90% of turns marked as personalized include both agent and user identity segments in prompt assembly diagnostics.
- Fewer than 2% of audited turns contain an out-of-scope retrieval inclusion defect.

### Engineering Metrics

- 100% of completed turns persist context, budget, and history diagnostics snapshots.
- 100% of history compaction decisions include a strategy version and traceable source range.
- Signal reconnect recovers from transient disconnects within 60 seconds in the standard deployment profile.

### Cost and Quality Metrics

- History budget overruns caused by raw conversation growth drop to zero after Phase 2 rollout.
- Average retrieval candidate count entering final prompt assembly decreases without reducing selected-result usefulness.
- Operators can answer "why was this context included or excluded?" for every audited turn using the diagnostics APIs alone.

## Phase Exit Acceptance Criteria

Phase 2 is complete when all of the following are true:

1. Identity and onboarding run from GBrain-backed identity pages with migration from legacy files completed.
2. Retrieval scope policy is enforced for all agent turns and produces diagnostics-rich selection traces.
3. History shaping is deterministic, budget-aware, and traceable through persisted compaction markers.
4. The three diagnostics endpoints return stable, authorized, structured payloads per turn.
5. Signal and other inbound channels route through `IAgentRuntime` with per-channel authn/authz and reconnect support.
6. All implementation work remains API-only with no dependency on new UI surfaces.

## Implementation Clarifications

- `IdentityProvider` is a new prompt-side read model and should replace direct identity-file reads during runtime prompt assembly.
- `OnboardingDirectiveBuilder` may wrap or replace the current onboarding instruction generation path, but the Phase 2 contract is the named builder and a structured output model rather than ad hoc strings alone.
- `IdentityUpdateProjector` should become the write path for identity updates; existing file-update behavior can remain as a migration/export shim until removed.
- GBrain entity expansion means traversal of existing page links, relation edges, or adjacency metadata already stored with GBrain/PostgreSQL data; it does not imply a new graph service.
- Diagnostics should be stored at turn time so later reads are historically accurate even if knowledge or identity changes afterward.
- `ChannelRouter` should depend on `IAgentRuntime` as the canonical execution surface, not a lower-level orchestration service.

## Sprint-Ready Engineering Tickets

- [ ] `P2-01` Define shared contracts for identity pages, onboarding gap codes, onboarding directive model, and identity update allowlist in `LeanKernel.Core`.
- [ ] `P2-02` Implement GBrain-backed `IdentityProvider` and migrate legacy `SELF.md` / `USER.md` content into identity pages with compatibility export support.
- [ ] `P2-03` Implement `OnboardingGapDetector` scoring and `OnboardingDirectiveBuilder` output with deterministic field coverage rules.
- [ ] `P2-04` Implement `IdentityUpdateProjector` for post-turn extraction, validation, conflict handling, and GBrain writeback.
- [ ] `P2-05` Add `RetrievalScopePolicy`, `ScopedKnowledgeService`, and entity expansion support with bounded traversal and diagnostics reason codes.
- [ ] `P2-06` Extend session/diagnostics persistence for compaction markers, retrieval decision traces, and per-turn context snapshots.
- [ ] `P2-07` Implement `HistoryCompactionStrategy` tier assignment and update `ConversationCompactor` for deterministic compacted artifacts and idempotent re-runs.
- [ ] `P2-08` Add diagnostics endpoints for `/context`, `/budget`, and `/history` with authorization enforcement and versioned response contracts.
- [ ] `P2-09` Refactor `ChannelRouter` to use `IAgentRuntime`, add per-channel authn/authz policy hooks, and restore Signal daemon reconnect behavior.
- [ ] `P2-10` Add integration tests covering onboarding trigger behavior, scoped retrieval exclusion, history compaction markers, diagnostics API authorization, and Signal reconnect/resume.
