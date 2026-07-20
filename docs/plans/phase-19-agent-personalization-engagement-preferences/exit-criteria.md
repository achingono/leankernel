# Phase 19 Agent Personalization and Engagement Preferences — Exit Criteria

## Gate Checklist
- [ ] Persistent engagement-preference model exists for both user-level and tenant/org-default scopes, keyed by `IPermit` partition keys (`TenantId`, `PersonId`/`UserId`, `ChannelId`).
- [ ] A closed allowlist governs every personalization field; unknown fields are rejected and the only near-free field (`AddressAs`) is sanitized and length-bounded before rendering.
- [ ] The preference renderer produces a deterministic, byte-stable prompt block for identical inputs, independent of input ordering (proven by tests).
- [ ] The merge policy composes exactly four layers in fixed precedence (system/base → tenant/org defaults → user preferences → per-turn overrides) with field-level "highest layer that sets a field wins" semantics, defaults filling the remainder.
- [ ] Per-turn overrides are validated like stored preferences, apply only to the current turn, and are never implicitly persisted.
- [ ] The safety-constraint layer is non-overridable: it is a fixed constant appended last and outside the preference block, travels via `TurnContext.ComposedInstructions` on the non-gated `PromptAssembler` path, and therefore is never subject to `ContextGatekeeper` admission; conflicting preference values cannot be represented and malformed input is dropped and recorded.
- [ ] `PromptAssembler` emits `TurnContext.ComposedInstructions` as the first system message (falling back to `AgentSettings.DefaultInstructions` when null/empty), keeping the current user message last; preferences are not emitted as a `ContextItem`, so `ContextGatekeeper` budget behavior is unchanged.
- [ ] `PreferenceCompositionStage`, `IEngagementPreferenceStore`, and `EngagementInstructionComposer` are registered `Scoped` in `TurnPipelineServiceExtensions`, with the stage slotted after `HistoryShaper` and before `PromptAssembler`.
- [ ] Per-turn overrides enter via a bounded, allowlisted `engagement` request field validated at the invocation site, flow through `TurnContext.PerTurnPreferenceOverride`, apply only to the current turn, and are never persisted (store exposes no override-accepting method).
- [ ] Merge uses nullable fields as the unset sentinel; a field explicitly set equal to its enum default still overrides a lower layer; channel-specific user rows overlay channel-default rows field-level.
- [ ] Store-failure fallback composes base + safety only, logs an actionable warning, and completes the turn.
- [ ] Isolation is enforced: preferences for one tenant/person/channel never contribute to composition for another; all store queries scope by tenant + identity.
- [ ] Tests pass for deterministic composition, merge precedence, safety non-override, allowlist enforcement/sanitization, isolation, and pipeline integration; coverage ≥80% on new types.
- [ ] `scripts/quality/sonarqube-scan.sh` run with all Blocker/Critical/Major issues resolved.
- [ ] Deep-review sub-agent run and all reported issues addressed.
- [ ] Documentation updated (feature page, configuration, architecture references); config shape under `Agents` preserved.

## Approval Table

| Role | Name | Status | Notes |
| --- | --- | --- | --- |
| Owner | | Pending | |
| Reviewer | | Pending | |
| Approver | | Pending | |
