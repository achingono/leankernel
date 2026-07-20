# Phase 16 Activities

## Step-By-Step Activities
1. Confirm the current behavior: `ResolveOrCreateUserAsync` persists Email/FirstName/LastName/FullName/UserName only on first creation and does not inject identity into the prompt.
2. Define the identity profile representation: extend `UserEntity` and/or add a related identity-claims/profile entity for additional allowlisted claims (preferred_username, locale, timezone, organization, roles/groups, bounded custom claims); add EF migration.
3. Define a claim allowlist configuration so only approved, non-sensitive claims are persisted and rendered (no tokens, no unbounded claim dumps).
4. Extend claim capture in `IdentityResolver` (and `ClaimsPrincipalExtensions`) to read the allowlisted claim set provider-agnostically.
5. Persist and refresh: update the identity profile on each authenticated resolution (not only on create) with change detection, treating the IdP as source of truth.
6. Implement an identity-context assembler that renders the persisted profile into a concise, deterministic system-prompt block (stable field order, redaction of empty fields).
7. Integrate the assembler at the context/prompt injection point: via `MemoryProvider` today, or as an admitted candidate under the Phase 03 gatekeeper/budget when present.
8. Add configuration (allowlist, prompt field selection, enable/disable) and startup validation.
9. Add tests: claim persistence + refresh + change detection, allowlist enforcement, deterministic prompt rendering, budget/admission behavior, and tenant/user partitioning safety.
10. Document identity-to-context flow in `docs/features/identity-partitioning.md` and the runtime/context docs.

## Review Focus
- Only allowlisted, non-sensitive claims are persisted and rendered (no tokens/secrets).
- Identity refreshes on login; stale profiles do not persist indefinitely.
- Prompt rendering is deterministic and bounded (respects budget when gated).
- Identity context is correctly partitioned (no cross-user/tenant leakage).
- Behavior is safe when claims are missing (graceful, no prompt corruption).

## Implementation Status
- [x] Steps 1-10 completed.
- [x] Unit/integration test suite executed for the phase changes.
- [x] SonarQube quality gate passed for the updated implementation.
