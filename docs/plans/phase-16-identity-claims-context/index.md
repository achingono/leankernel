# Phase 16 Identity Claims To Agent Context

## Companion Documents
- [Inputs](inputs.md)
- [Activities](activities.md)
- [Outputs](outputs.md)
- [Exit Criteria](exit-criteria.md)
- [Risk Register](risk-register.md)
- [Evidence](evidence.md)

## Objective
Flow the authenticated user's identity from OIDC/OAuth claims into the agent's reasoning context so the assistant knows who it is talking to. Identity claims are persisted to the database as a durable identity profile, and the context builder renders that profile into the system prompt context each turn. Phase 16 closes the prior gaps where claim capture was narrow and prompt identity context was not injected.

## Scope
This phase covers two linked steps: (1) capturing and persisting a richer identity profile from claims (refreshed on each authenticated resolution), and (2) an identity-context assembler that renders the persisted profile into the system prompt. It integrates with the Phase 03 context gatekeeper/budget when present but is functional on the current turn path. It does not add new auth providers, cross-channel person linking (Phase 10), or preference editing UI.

## In Scope
- Extend claim capture in `IdentityResolver.ResolveOrCreateUserAsync` to persist a richer, provider-agnostic identity profile: existing fields plus additional standard claims (preferred_username, locale, zoneinfo/timezone, organization, roles/groups) and a bounded set of configurable custom claims.
- Refresh persisted identity on each authenticated resolution (currently updated only on create), with change detection so profile edits track the IdP as source of truth.
- A durable identity profile representation (fields on `UserEntity` and/or a related profile/claims entity) with a claim allowlist to avoid persisting sensitive/unbounded claims.
- An identity-context assembler that renders the persisted profile into a concise, deterministic system-prompt block (name, email, locale, timezone, org, roles — allowlisted), inserted into the agent invocation.
- Integration with the memory/context injection point (`MemoryProvider`) or the Phase 03 gatekeeper so identity context is admitted under budget alongside memory.
- Configuration for the claim allowlist, which fields appear in the prompt, and enable/disable; startup validation.
- Tests for claim persistence + refresh, allowlist enforcement, deterministic prompt rendering, budget/admission behavior, and partitioning safety.

## Out of Scope
- New authentication providers or the auth pipeline itself (JWT/OIDC wiring already exists).
- Cross-channel person linking and person-scoped memory (Phase 10) — this phase persists per-user identity and can feed the person profile later.
- Editing identity via UI (Phase 09) beyond read-through from the IdP.

## Entry Criteria
- Authenticated resolution from claims exists (`IdentityResolver.ResolveOrCreateUserAsync`) and JWT/OIDC is wired in the gateway.
- A context/prompt injection point exists (`MemoryProvider` today; Phase 03 gatekeeper if merged).
- `ClaimsPrincipalExtensions` provides claim-reading helpers to extend.

## Exit Criteria
Authenticated users' identity claims are persisted to the database (and refreshed on login), and the context builder injects an allowlisted identity block into the system prompt each turn, under budget and respecting partitioning. The current implementation meets this gate; see `exit-criteria.md`.

## Roles
- Owner: Rebuild maintainer
- Reviewer: Separate agent session / model review
- Approver: Repository owner
