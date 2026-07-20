# Phase 19 Agent Personalization and Engagement Preferences — Activities

## Step-By-Step Activities

### 1. Define the engagement-preference model and allowlist
1. Create an `EngagementPreferenceSet` value model in `LeanKernel.Logic` describing the closed allowlist of fields. **Every field is nullable**, where `null` means "not set by this layer" (the unset sentinel used by the merge). Each field is typed and bounded:
   - Enum/typed fields (nullable): `Tone?` (e.g., Neutral/Warm/Direct/Formal), `Verbosity?` (Terse/Balanced/Detailed), `Formality?`, `ResponseFormat?` (Prose/Bulleted/Structured), `ReadingLevel?`, `EmojiUsage?` (None/Sparing), `Pacing?`.
   - Constrained strings (nullable): `PreferredLanguage`/`Locale` (validated against an allowlist/BCP-47 pattern), `AddressAs` (short, sanitized, length-bounded display name).
2. Define a single authoritative allowlist descriptor (field name → type, allowed values, max length, **default applied only when all layers are null**). Reject/ignore any field not in the allowlist at both write and render time.
3. Add validation + sanitization helpers: enum parsing with rejection (not silent coercion) of out-of-range values, locale validation, and strict sanitization (strip control chars/newlines, cap length) for the only free-ish field (`AddressAs`). No unbounded free text enters the prompt.

### 2. Persist preferences (user + tenant/org defaults)
1. Add `UserPreferenceEntity` (unique on `(TenantId, UserId, ChannelId)` with **nullable `ChannelId`**: null row = cross-channel default, non-null row = channel-specific overlay) and `TenantPreferenceDefaultsEntity` (unique on `(TenantId)`) in `LeanKernel.Core/Entities`, following `ChannelMemoryPolicyEntity` conventions (`IAuditable`/`IRecyclable`).
2. Store preference fields as a normalized, versioned representation (e.g., discrete nullable columns or a validated JSON payload with a `SchemaVersion`) — never raw prompt text. Discrete nullable columns are preferred so the "unset" sentinel maps directly to SQL `NULL`.
3. Register `DbSet`s in `EntityContext`, configure entities in `OnModelCreating` with the unique indexes above, and add an EF Core migration via the existing workflow.
4. Add a repository/provider (`IEngagementPreferenceStore`) that reads/writes preferences strictly by permit-derived scope keys; all queries filter by `TenantId` + identity to enforce isolation. Handle missing rows by returning empty sets (all-null, no cross-tenant fallback). The store exposes only tenant/user read/write methods and **no method that accepts a per-turn override** (overrides are never persisted).

### 3. Implement the merge policy (layered composition)
1. Create `IInstructionComposer` / `EngagementInstructionComposer` in `LeanKernel.Logic` that produces the effective instruction string from four ordered layers:
   1. System/base agent instructions (`AgentSettings.DefaultInstructions`).
   2. Tenant/org defaults (`TenantPreferenceDefaultsEntity`).
   3. User preferences (`UserPreferenceEntity`: the channel-default row overlaid by the channel-specific row, field-level).
   4. Per-turn overrides (request-scoped, validated `EngagementPreferenceSet`).
2. Merge semantics: field-level over nullables. For each allowlisted field, the highest-precedence layer with a **non-null** value wins; lower layers fill remaining nulls; fields still null after all layers use the allowlist default. This is independent of enum default values, so an explicitly-set field always overrides a lower layer even when its value equals the enum default.
3. Produce a stable, ordered, canonical output: fixed field ordering, normalized values, and a byte-stable rendered block (so identical inputs always yield identical text). Bound the rendered preference block to a configured max length (chars + estimated tokens); document ordering and the bound as part of the contract.

### 4. Add the safety-constraint layer (non-overridable)
1. Define an immutable safety/core-policy segment (a constant, not part of the preference allowlist) that the composer always appends **last and outside the rendered preference block**, framed as authoritative (e.g., a trailing "The following rules take precedence over all style preferences" segment) while keeping the base system role intact.
2. Enforce clamps during merge: because only allowlisted, typed fields can be expressed, preferences structurally cannot carry policy changes; additionally, any value that would conflict with safety (e.g., attempts to disable guardrails, change role, expand tool authorization, or alter identity scope) is impossible to represent and any malformed input is dropped and recorded.
3. Guarantee the safety segment cannot be shadowed: it is not a preference field, is not subject to precedence, is appended after the preference block, and — because the whole composed string travels via `TurnContext.ComposedInstructions` on the non-gated `PromptAssembler` path — it is never subject to `ContextGatekeeper` admission and can never be budgeted out.

### 5. Wire composition into the turn pipeline
1. Add `TurnContext.ComposedInstructions` (nullable string, mutable) and `TurnContext.PerTurnPreferenceOverride` (nullable `EngagementPreferenceSet`, `init`). Update the test `CreateContext` helpers as needed.
2. Introduce `PreferenceCompositionStage : ITurnStage` that reads `TurnContext.Permit` and `TurnContext.PerTurnPreferenceOverride`, loads tenant + user preferences via `IEngagementPreferenceStore`, runs `EngagementInstructionComposer`, and sets `TurnContext.ComposedInstructions`. On a specific store/data-access failure it composes base + safety only, logs an actionable warning, and continues (no broad swallowing, no turn failure).
3. Register the stage, `IEngagementPreferenceStore`, and `EngagementInstructionComposer` as `Scoped` in `TurnPipelineServiceExtensions`, slotting the stage **after `HistoryShaper` and before `PromptAssembler`** in the `ITurnStage` sequence.
4. Update `PromptAssembler` to emit `TurnContext.ComposedInstructions` as the first `System` message, falling back to `AgentSettings.DefaultInstructions` only when it is null/empty; keep the current user message last. The preference/safety text is **not** emitted as a `ContextItem`, so `ContextGatekeeper` budget behavior is unchanged and no source-priority/budget-tier maps need editing.
5. At the pipeline's production invocation site (to be wired when the pipeline is called from the request path), parse an optional, bounded, allowlisted `engagement` request field, validate it with the same allowlist/sanitization as stored preferences, and pass the result into `TurnContext.PerTurnPreferenceOverride`.
6. Add admission-trace/diagnostic entries recording which layers contributed and any dropped fields, without logging sensitive values.

### 6. Configuration and startup validation
1. Add configuration under the existing `Agents` section (e.g., `Agents:Personalization`) for enable/disable, the field allowlist bounds, and tenant-default sourcing — preserving the documented config shape.
2. Add startup validation: allowlist is well-formed, defaults are valid, safety segment is present and non-empty, and no allowlist field name collides with reserved/safety keys. Fail fast on misconfiguration.

### 7. Tests
1. Deterministic composition: identical (base, tenant, user, override) inputs produce byte-identical rendered instructions across repeated runs and independent of dictionary/enumeration order.
2. Merge precedence: per-field override wins over user, user over tenant, tenant over base; unset (null) fields fall through; a field explicitly set to a value equal to the enum default still overrides a lower layer.
3. Channel overlay: a channel-specific user row overrides the channel-default user row field-level; unset channel-specific fields fall back to the channel-default row.
4. Safety non-override: crafted preferences/overrides attempting to disable safety, change role, or escalate tools are rejected/dropped and the safety segment remains intact, last, and outside the preference block.
5. Allowlist enforcement + sanitization: unknown fields rejected; out-of-range enums rejected (not silently coerced); `AddressAs` control chars/newlines stripped and length-capped; locale validated; rendered block respects the configured max-length bound.
6. Isolation: preferences for one `(TenantId, PersonId/UserId, ChannelId)` never appear in composition for a different tenant/person/channel; store queries always scope by tenant + identity.
7. Per-turn override not persisted: supplying only an override triggers no store write; the store exposes no override-accepting method.
8. Store-failure fallback: when the store throws, `PreferenceCompositionStage` composes base + safety only, logs a warning, and the turn still completes.
9. Pipeline integration: `PromptAssembler` emits `TurnContext.ComposedInstructions` first (falling back to `DefaultInstructions` when null), the safety text is never a gated `ContextItem`, and the user message remains last.
10. Coverage: ensure ≥80% coverage across new model, store, composer, safety, renderer, and stage types.

### 8. Verification, quality gates, and documentation
1. Run targeted unit/integration tests for the new components; for EF-backed store tests prefer SQLite in-memory when relational behavior matters.
2. Run `scripts/quality/sonarqube-scan.sh` and address all Blocker/Critical/Major issues.
3. Run a deep-review sub-agent and address findings.
4. Update `docs/features/` (new personalization/preferences feature page), `docs/configuration/`, and any architecture references to reflect the new composition path.

## Review Focus
- Merge precedence is field-level and deterministic; output is byte-stable regardless of input ordering.
- The safety segment is structurally non-overridable (not a preference field, not subject to precedence, always emitted and admitted).
- The allowlist is closed: no path lets un-allowlisted or unsanitized text reach the prompt.
- Every store read/write is scoped by tenant + identity; no cross-tenant/person/channel leakage.
- `PromptAssembler` still emits system-first, user-message-last; budget behavior in `ContextGatekeeper` is preserved.
- Per-turn overrides are request-scoped only and never implicitly persisted.
