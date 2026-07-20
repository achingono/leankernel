# Phase 19 Agent Personalization and Engagement Preferences — Risk Register

## Risks

| ID | Risk | Impact | Mitigation | Status |
| --- | --- | --- | --- | --- |
| R1 | Preferences used as a prompt-injection vector to weaken safety/core policy | Critical — guardrail bypass | Closed allowlist (no free-form policy text can be expressed); safety segment is non-overridable and rendered outside the preference block; clamp + drop conflicting values; explicit safety-non-override tests | Open |
| R2 | Non-deterministic composition (dictionary/enum ordering) breaks reproducibility and caching | Medium — flaky output, hard to test | Canonical fixed field ordering, normalized values, byte-stable renderer; repeated-run equality tests | Open |
| R3 | Cross-tenant/person/channel preference leakage | High — privacy/isolation breach | All store queries scoped by tenant + identity; no cross-tenant default fallback; isolation tests over all four partition keys | Open |
| R4 | Merge precedence ambiguity when multiple layers set the same field | Medium — surprising behavior | Field-level precedence with documented "highest layer wins"; explicit per-field precedence tests | Open |
| R5 | `AddressAs`/near-free fields inject newlines or control chars into instructions | Medium — prompt structure corruption | Strict sanitization (strip control/newlines), length cap, validation at write and render | Open |
| R6 | Preference block consumes system token budget and starves identity/system context | Medium — degraded context | **Resolved:** preferences ride in `TurnContext.ComposedInstructions` on the non-gated `PromptAssembler` path (not a `ContextItem`), so they never draw from `SystemContextTokenBudget`; the composer bounds the rendered block to a configured max length, and the safety segment is a fixed constant appended outside the block | Mitigated |
| R7 | Per-turn override accidentally persisted | Medium — sticky unintended behavior | Request-scoped override path with no write path; tests asserting no persistence | Open |
| R8 | EF schema/migration drift or relational-behavior gaps in tests | Low/Medium — runtime errors | Follow existing migration workflow; unique index on identity key; SQLite in-memory for relational store tests | Open |
| R9 | Config/allowlist misconfiguration ships silently | Medium — inconsistent behavior | Fail-fast startup validation of allowlist, defaults, and safety segment presence | Open |

## Resolved Decisions
See `index.md` → "Resolved Design Decisions" for full rationale.
- **Storage representation:** discrete nullable columns preferred (SQL `NULL` = the unset sentinel), with a `SchemaVersion` field; a validated versioned JSON payload is the fallback if column count becomes unwieldy.
- **Channel scope:** `UserPreferenceEntity` is unique on `(TenantId, UserId, ChannelId)` with nullable `ChannelId`; null = cross-channel default, non-null = channel-specific overlay applied over the default within the user layer.
- **Source label:** dissolved — preferences are composed into the system instruction string (`TurnContext.ComposedInstructions`), not emitted as a `ContextItem`, so no new source and no gatekeeper/assembler map changes.
- **Override transport:** a bounded, allowlisted `engagement` request field validated at the pipeline invocation site and passed into `TurnContext.PerTurnPreferenceOverride`; request-scoped only, never persisted.

## Open Decisions
- Exact wire shape of the `engagement` request field (JSON object in body vs. bounded header for header-only channels) — to be finalized when the pipeline is wired into the request path.
