# Phase 24 - Anonymous Fallback Hardening

## Companion Documents
- [Inputs](inputs.md)
- [Activities](activities.md)
- [Outputs](outputs.md)
- [Exit Criteria](exit-criteria.md)
- [Risk Register](risk-register.md)
- [Evidence](evidence.md)

## Objective

Harden tenant resolution and identity mapping in `TenantResolutionMiddleware` so that the anonymous fallback (Path C) is disabled by default and only enabled via explicit configuration, instead of falling back silently for any unauthenticated request.

## Scope

This phase replaces the implicit guest user resolution pathway with an opt-in configuration flag (`Identity:AllowGuestFallback`) that must be explicitly set to true. In production, unauthenticated requests will fail closed with 401 unless the operator has deliberately enabled guest fallback.

## In Scope
- Adding a `bool AllowGuestFallback` property to `IdentitySettings` (default `false`).
- Refactoring `TenantResolutionMiddleware` Path C to gate on `AllowGuestFallback`.
- Returning 401 Unauthorized instead of resolving a guest user when `AllowGuestFallback` is `false`.
- Setting `AllowGuestFallback: true` in `appsettings.Development.json` so DevUI continues to work in development.
- Adding a `LogWarning` with structured data when Path C is entered (allowed) and when it is blocked.
- Updating existing unit and integration tests that assume anonymous requests are always accepted.
- Adding new tests for blocked anonymous requests and for the config-flag behavior.

## Out of Scope
- Modifying standard channel bearer authentication (Path A / Path B).
- Implementing production anonymous chat interfaces beyond the config flag.
- Migrating DevUI to use a bearer token (tracked separately).
- Adding `IWebHostEnvironment` to the middleware (the config flag makes this unnecessary).

## Entry Criteria
- The existing Path C (anonymous/guest) logic in `TenantResolutionMiddleware` is fully mapped.
- The two existing tests that assert anonymous acceptance are identified:
  - `TenantResolutionMiddlewareTests.InvokeAsync_AnonymousUser_StoresGuestIdentityInItems`
  - `Phase02BoundaryTests.AnonymousRequest_WithoutToken_IsAcceptedWhenNoSigningKeyConfigured`

## Exit Criteria
- Guest resolution is blocked for arbitrary unauthenticated requests in production.
- DevUI continues to function in development (no behavioral regression).
- A blocked anonymous request produces a structured warning log.
- All existing and new unit and integration tests pass.

## Status
**Draft**

## Roles
- Owner: Coding Agent
- Reviewer: Model review
- Approver: Repository owner
