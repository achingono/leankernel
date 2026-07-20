# Phase 19 Agent Personalization and Engagement Preferences

## Companion Documents
- [Inputs](inputs.md)
- [Activities](activities.md)
- [Outputs](outputs.md)
- [Exit Criteria](exit-criteria.md)
- [Risk Register](risk-register.md)
- [Evidence](evidence.md)

## Objective
Let users shape *how* the assistant engages with them (tone, verbosity, formatting, addressing style, language, pacing) without letting those preferences weaken core system behavior or leak across identity boundaries. Today the effective system message is a single flat string (`AgentSettings.DefaultInstructions`) injected verbatim by `PromptAssembler`, with no user-, tenant-, or turn-level personalization. This phase introduces a persistent, per-identity engagement-preference model, an allowlisted rendering path that turns preference fields into deterministic prompt text, and an explicit, layered merge policy (system/base → tenant/org defaults → user preferences → per-turn overrides) governed by non-overridable safety constraints. The result is reproducible, isolation-safe prompt composition that respects tenant/person/channel partitioning.

## Scope
This phase adds the preference model, its persistence and repository access, an allowlisted preference renderer, a merge/composition component that produces the effective instruction stack, and the safety layer that prevents preferences from overriding core policy. It wires composition into the existing turn pipeline (`PromptAssembler` / context stages) and covers deterministic-composition and isolation tests.

## In Scope
- A persistent engagement-preference model scoped by identity: user-level preferences plus tenant/org-level defaults, keyed off `IPermit` (`TenantId`, `PersonId`/`UserId`, `ChannelId`).
- A closed **allowlist** of personalization/customization fields (e.g., tone, verbosity, formality, language/locale, response format, addressing name, emoji usage, reading level) with typed, bounded, validated values — free-form text is either rejected or strictly sanitized and length-bounded.
- A deterministic **preference renderer** that converts allowlisted fields into a stable, ordered prompt block (no raw user text interpolated into instructions unless allowlisted and sanitized).
- An explicit **merge policy** composing four layers in fixed precedence: (1) system/base agent instructions, (2) tenant/org defaults, (3) user preferences, (4) per-turn overrides — with defined conflict resolution (later layer wins for a given field, subject to safety clamps).
- A **safety-constraint layer** (immutable core policy) applied last/highest that cannot be weakened by any lower layer: preferences may adjust presentation but never disable safety, guardrails, tool authorization, identity partitioning, or the system role.
- Per-turn override channel: an allowlisted, request-scoped override surface (validated the same way as stored preferences) that applies only to the current turn and is never persisted implicitly.
- Integration into the turn pipeline so the effective instruction stack replaces/augments the flat `DefaultInstructions` injection in `PromptAssembler`, preserving admission/budget behavior in `ContextGatekeeper`.
- A new `TurnContext.ComposedInstructions` property carrying the composer's output, plus a `TurnContext.PerTurnPreferenceOverride` init property carrying the validated per-turn override, so an earlier stage can hand results to `PromptAssembler`.
- A dedicated preference-loading/composition stage (`PreferenceCompositionStage`) with a defined slot in the pipeline and DI registration in `TurnPipelineServiceExtensions`, plus registration of `IEngagementPreferenceStore` and `EngagementInstructionComposer`.
- A defined per-turn override transport at the gateway boundary (a bounded, allowlisted request field validated before the pipeline runs) and a store-failure fallback path.
- Configuration for the field allowlist, tenant-default source, enable/disable, and startup validation of allowlist + safety constraints.
- Tests: deterministic prompt composition (byte-stable output for identical inputs), merge-precedence correctness, safety non-override, allowlist enforcement/sanitization, and isolation by tenant/person/channel.

## Out of Scope
- Preference-editing UI (Phase 09) beyond the persistence + API contract needed to read/write preferences.
- Learning or inferring preferences automatically from conversation (Phase 07 learning) — this phase stores only explicitly set preferences.
- New authentication providers or identity resolution changes (Phases 15/16 own claim capture and identity context).
- Model/provider selection or routing personalization (Phase 04).
- Cross-channel person-preference reconciliation beyond reading the `PersonId` already resolved by the permit.

## Resolved Design Decisions
These resolve the open questions raised in review; they define the component contracts before implementation.

1. **Composed-instructions carrier.** Add `TurnContext.ComposedInstructions` (string, mutable, default `null`). The composer writes the full effective system instruction string here; `PromptAssembler` reads it and falls back to `AgentSettings.DefaultInstructions` only when it is `null`/empty.
2. **Safety/budget interaction — preferences are NOT a gated `ContextItem`.** The composer folds base instructions + rendered preference block + safety segment into the single system instruction string emitted by `PromptAssembler` as the first `System` message — the *same non-gated path* the flat `DefaultInstructions` uses today (it is added directly in `PromptAssembler`, never as a `ContextItem`, so it already bypasses `ContextGatekeeper`). Consequently the preference/safety text never competes in `SystemContextTokenBudget`. Starvation is prevented by bounding the rendered preference block to a configured max length (chars + estimated tokens) enforced inside the composer; the safety segment is a fixed constant appended last and outside the preference block, so it can never be shadowed, dropped, or budgeted out.
3. **Source labeling — dissolved.** Because preferences are composed into the system message rather than emitted as a `ContextItem`, no new `"preferences"` source is introduced and neither `ContextGatekeeper`'s budget-tier map nor `PromptAssembler`'s source-priority map changes.
4. **Unset-field sentinel.** Every allowlisted field in `EngagementPreferenceSet` is **nullable**; `null` means "not set by this layer." Merge is field-level over nullables: for each field the highest-precedence layer with a non-null value wins; if all layers are null the allowlist default applies. This correctly lets a user set `Tone = Neutral` override a tenant `Tone = Warm` and vice-versa, independent of enum default values.
5. **Preference-loading stage + DI slot.** Introduce `PreferenceCompositionStage : ITurnStage`. It reads `TurnContext.Permit` and `TurnContext.PerTurnPreferenceOverride`, loads tenant + user preferences via `IEngagementPreferenceStore`, runs `EngagementInstructionComposer`, and sets `TurnContext.ComposedInstructions`. Registered in `TurnPipelineServiceExtensions` and slotted **after `HistoryShaper`, before `PromptAssembler`** (it depends only on the permit/override, not on gating, so it must simply precede assembly). `IEngagementPreferenceStore` and `EngagementInstructionComposer` are registered `Scoped`.
6. **Per-turn override transport.** The turn pipeline is currently constructed only in tests (no `new TurnContext` exists in `src/`), so the override input path is defined at the pipeline's future invocation site: add `TurnContext.PerTurnPreferenceOverride` (nullable, `init`). The gateway request handler parses an **optional, bounded, allowlisted `engagement` object** from the request (body field; a bounded header variant is acceptable for header-only channels), validates it with the *same* allowlist/sanitization used for stored preferences, and passes the validated value into `TurnContext` at construction. It is request-scoped only.
7. **Per-turn overrides are never persisted.** `IEngagementPreferenceStore` exposes only tenant/user read/write methods and **no method accepting a per-turn override shape**. The composer treats layer 4 as read-only input. Tests assert no store write occurs when only an override is supplied.
8. **Channel scope + unique index.** User preferences are unique on `(TenantId, UserId, ChannelId)` with **nullable `ChannelId`**: a `ChannelId = null` row is the user's cross-channel default; a non-null row is a channel-specific overlay. Within the user layer, the channel-specific row overlays the channel-default row (field-level, same null-sentinel rule). Tenant defaults are unique on `(TenantId)`. This keeps the four conceptual layers (base → tenant → user → per-turn) while giving the user layer an internal channel overlay.
9. **Store-failure fallback.** If the store throws a specific data-access exception or is unreachable, `PreferenceCompositionStage` composes base instructions + safety segment only (no preferences), logs an actionable warning with scope context, and continues — never swallowing broadly and never failing the turn.


- Turn pipeline with `PromptAssembler`, `ContextGatekeeper`, and `TurnContext` exists and injects `AgentSettings.DefaultInstructions` as the system message. Note: `TurnContext` is currently constructed only in tests (no `new TurnContext` in `src/`); the production invocation site that wires the pipeline into the request path is where the per-turn override is parsed and passed in.
- `IPermit` exposes `TenantId`, `PersonId`, `UserId`, and `ChannelId` partitioning keys.
- `EntityContext` (EF Core) and the migration workflow are in place for adding a new preference entity.
- Identity context assembly (Phase 16) is available as the reference pattern for allowlisted, deterministic prompt rendering.

## Exit Criteria
Engagement preferences are persisted per identity, rendered through an allowlist into a deterministic prompt block, merged in fixed precedence under a non-overridable safety layer, and isolated by tenant/person/channel — all verified by tests and documented. See `exit-criteria.md`.

## Roles
- Owner: Rebuild maintainer
- Reviewer: Separate agent session / model review
- Approver: Repository owner
