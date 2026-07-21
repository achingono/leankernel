# Phase 16 Activities

## Completed Activities
1. [x] Confirm the current behavior: `ResolveOrCreateUserAsync` persists Email/FirstName/LastName/FullName/UserName only on first creation and does not inject identity into the prompt.
2. [x] Define the identity profile representation: extended `UserEntity` with `RolesJson`, `GroupsJson`, `CustomClaimsJson`, `PreferredUserName`, `Locale`, `TimeZone`, `Organization`; added EF migration `20260719163620_AddIdentityClaimsContext`.
3. [x] Define a claim allowlist configuration: `IdentityClaimsContextSettings` with `AllowedCustomClaims` (deny-by-default).
4. [x] Extend claim capture in `IdentityResolver.ApplyIdentityProfile` to read allowlisted claims provider-agnostically using `Constants.Claims` constants.
5. [x] Persist and refresh: `ApplyIdentityProfile` called on each resolution path (create, re-resolve, guest refresh).
6. [x] Implement `IdentityContextAssembler` — renders persisted profile into deterministic prompt block (stable field order, empty-field elimination, token-budget truncation).
7. [x] Integrate assembler in `MemoryProvider.ProvideAIContextAsync` — injects identity context before memory; `ContextGatekeeper` admits under system budget via `ContextSource.Identity`.
8. [x] Add configuration + `ValidateOnStart()` in `Programs.cs`.
9. [x] Add tests: `IdentityResolverTests` (persistence + refresh), `IdentityContextAssemblerTests` (rendering + edge cases), `MemoryProviderBehaviorTests` (integration).
10. [x] Document in `docs/features/identity-partitioning.md`, `docs/configuration/appsettings-reference.md`, `docs/architecture/system-overview.md`.

## Review Focus
- [x] Only allowlisted, non-sensitive claims are persisted and rendered (no tokens/secrets).
- [x] Identity refreshes on login; stale profiles do not persist indefinitely.
- [x] Prompt rendering is deterministic and bounded (respects budget when gated).
- [x] Identity context is correctly partitioned (no cross-user/tenant leakage).
- [x] Behavior is safe when claims are missing (graceful, no prompt corruption).
