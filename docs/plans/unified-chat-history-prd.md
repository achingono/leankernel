# PRD: Unified LeanKernel Chat History

## Problem Statement

LeanKernel's Blazor chat UI generates a random browser-local GUID (`leankernel.chat.owner-id` in localStorage) as the session owner identity. All sessions are keyed by `(ChannelId, UserId)` where `UserId` = this ephemeral GUID. Consequently, the same authenticated person gets different session silos in different browsers/devices, defeating cross-device history continuity.

The `/api/chat` endpoint likewise trusts the caller-supplied `UserId` without authentication, allowing session spoofing.

## Goals

1. **Stable user identity across sessions**: After authentication via oauth2-proxy, the Gateway derives a consistent user key from forwarded OIDC claims (`sub` > email > `X-Auth-Request-User`).
2. **Unified chat history**: A user sees the same session list from any browser after signing in.
3. **Ownership enforcement**: Sessions are scoped to the authenticated user; direct URL access to another user's `SessionId` is denied.
4. **API auth alignment**: `/api/chat` derives `SenderId` from the authenticated user, ignoring caller-supplied `UserId`.
5. **Best-effort legacy migration**: Existing sessions created with the old localStorage GUID are migrated to the authenticated user key on first login.
6. **Local dev compatibility**: Config flags allow disabling forwarded-auth for development.

## Non-Goals

- Adding a new OIDC login flow inside the Gateway (oauth2-proxy remains the auth boundary).
- Schema changes to `SessionEntity` (reuses `UserId` column).
- Merging `api` and `blazor:` channel session rails in the UI.

---

## Actionable Checklist

### Phase 1: Forwarded-Auth Authentication Handler

- [ ] **1.1 Create `Auth/ForwardedAuthHandler.cs`** — Implement a custom `AuthenticationHandler<ForwardedAuthOptions>` that reads:
  - `X-Auth-Request-User` header → primary fallback
  - `X-Auth-Request-Email` header → secondary fallback
  - `Authorization: Bearer <JWT>` → parse claims, extract `sub` claim (preferred)
  - Normalize stable user key: `sub` > email > forwarded user
- [ ] **1.2 Create `Auth/ForwardedAuthOptions.cs`** — Define `AuthenticationSchemeOptions` with config properties:
  - `Enabled` (bool, default `false` in dev)
  - `RequireAuthenticatedUser` (bool, default `true` in prod)
- [ ] **1.3 Create `Auth/ClaimsExtensions.cs`** — Helper to extract the normalized user key from `ClaimsPrincipal` with the precedence: `sub` → `email` → `name` claim
- [ ] **1.4 Register handler DI** — Add `AddAuthentication().AddScheme<ForwardedAuthOptions, ForwardedAuthHandler>(...)` with a named scheme (e.g. `"ForwardedAuth"`)

### Phase 2: Wire Auth into Gateway Composition Root

- [ ] **2.1 Register auth services in `Program.cs`** — Add:
  - `builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(...)` + the forwarded scheme as fallback
  - `builder.Services.AddAuthorization()` with a default policy requiring authenticated user
  - `builder.Services.AddCascadingAuthenticationState()` for Blazor
- [ ] **2.2 Add middleware** — Insert `app.UseAuthentication()` and `app.UseAuthorization()` before the endpoint/component mapping block (before `app.MapHealthChecks`)
- [ ] **2.3 Configure public endpoints** — Annotate `/api/health` and `/healthz` with `.AllowAnonymous()`
- [ ] **2.4 Configure protected routes** — Require authorization for:
  - Blazor Razor components (`RequireAuthorization()` on `MapRazorComponents<App>()`)
  - `/api/chat` (via `[Authorize]` metadata or policy)
  - `/api/diagnostics/*` endpoints

### Phase 3: Replace Browser-Local Owner Identity in Blazor Chat

- [ ] **3.1 Inject `AuthenticationStateProvider` into `Chat.razor`** — Or create a scoped `IUserIdentityAccessor` service that resolves the authenticated user key from `HttpContext` or `AuthenticationStateProvider`
- [ ] **3.2 Replace `GetOrCreateOwnerIdAsync()`** — Instead of generating a GUID, resolve the authenticated user key; keep the old localStorage call only as a legacy migration source (pass legacy GUID to migration step)
- [ ] **3.3 Update `InitializeAsync` call** — Pass the authenticated user key as `ownerId`; pass the legacy localStorage GUID separately for migration
- [ ] **3.4 Handle unauthenticated state** — If `RequireAuthenticatedUser` is false (dev), fall back to the old GUID behavior. If true, redirect to login or show an error.

### Phase 4: User-Scope Browser Session Cache

- [ ] **4.1 Scope `leankernel.chat.sessions` key** — Change the localStorage cache key from `leankernel.chat.sessions` to `leankernel.chat.sessions.{userKeyHash}` (e.g. SHA256 prefix of the user key) to avoid cross-user cache collision on shared browsers
- [ ] **4.2 Cache invalidation** — Clear the old un-scoped `leankernel.chat.sessions` key on migration to prevent stale data
- [ ] **4.3 Consider removal** — Evaluate whether the client-side session cache is still needed given the DB-backed `RefreshSessionsAsync` path; if not, remove `LoadCachedSessionsAsync`/`PersistSessionCacheAsync` entirely

### Phase 5: Best-Effort Legacy Migration

- [ ] **5.1 Add `MigrateUserSessionsAsync` to `ChatService`** — Before first `RefreshSessionsAsync`, if legacy localStorage GUID exists and differs from authenticated user key:
  - Query `engine."Sessions"` WHERE `UserId == legacyOwnerId` AND `ChannelId LIKE 'blazor:%'`
  - Update those rows to `UserId = authenticatedUserKey` (raw SQL or EF)
  - Handle unique-index races on `(ChannelId, UserId)` by catching `DbUpdateException` and logging
  - Log count of migrated sessions
- [ ] **5.2 Make migration idempotent** — Guard with `WHERE NOT EXISTS (SELECT 1 FROM engine."Sessions" WHERE ChannelId = ... AND UserId = ...)` or catch-and-log duplicate key violations
- [ ] **5.3 Clear legacy localStorage** — After successful migration, remove `leankernel.chat.owner-id` from localStorage and/or set a sentinel flag
- [ ] **5.4 Add migration to `InitializeAsync` flow** — Call migration before `RefreshSessionsAsync` in the initialization sequence

### Phase 6: Harden Session Ownership Checks

- [ ] **6.1 Update `ResolveChannelIdAsync`** — Already filters by `OwnerId`, verify this is sufficient. Add explicit `UserId != OwnerId` → return null (not-found) guards.
- [ ] **6.2 Update `OpenSessionAsync`** — After resolving `ChannelId`, verify the session belongs to the authenticated user; if not, set error message and clear conversation ("Session not found").
- [ ] **6.3 Update `ChatService` methods** — Audit `SendAsync`, `LoadHistoryAsync`, `CreateNewSessionAsync` to fail closed if `OwnerId` is not authenticated (not just initialized).
- [ ] **6.4 Add `SessionBelongsToUserAsync` to `ISessionStore`** — New method: `Task<bool> SessionBelongsToUserAsync(string sessionId, string userId, CancellationToken ct)`. Implement in `PostgresSessionStore` and `ResilientSessionStore`/`DegradedSessionBuffer`.

### Phase 7: Protect `/api/chat` with Authenticated Identity

- [ ] **7.1 Derive `SenderId` from auth** — In `HandleChatAsync`, replace `request.UserId` with the authenticated user key from `HttpContext.User`
- [ ] **7.2 Ignore caller-supplied `UserId`** — When auth is enabled, log a warning if `request.UserId` differs from authenticated user, but always use the authenticated value
- [ ] **7.3 Default `ChannelId`** — Keep default `"api"` unless a specific allowed channel is supplied
- [ ] **7.4 Session ownership check** — Before using an existing `request.SessionId`, verify it belongs to the authenticated user via `ISessionStore.SessionBelongsToUserAsync`. Return 404 if not owned.
- [ ] **7.5 API key fallback** — Keep API-key validation as a secondary gate; when no authentication is present (dev mode), fall back to the existing `request.UserId` behavior

### Phase 8: Extend Persistence APIs

- [ ] **8.1 Add `SessionBelongsToUserAsync` to `ISessionStore`** — Signature: `Task<bool> SessionBelongsToUserAsync(string sessionId, string userId, CancellationToken ct)`. Default implementation returns false.
- [ ] **8.2 Implement in `PostgresSessionStore`** — Query `db.Sessions.AnyAsync(s => s.Id == sessionId && s.UserId == userId)`
- [ ] **8.3 Implement in `ResilientSessionStore`** — Delegate to inner store, catch exceptions gracefully
- [ ] **8.4 Implement in `DegradedSessionBuffer`** — Check in-memory dictionary by session ID
- [ ] **8.5 Add `MigrateUserSessionsAsync` persistence method** — Either on `ISessionStore` or as a new `ISessionMigrationService` in the Gateway layer. Prefer the latter if migration is a UI/API concern rather than turn-pipeline persistence.

### Phase 9: Schema Initialization (If Needed)

- [ ] **9.1 Assess schema needs** — The plan says no new columns needed; reuses `UserId` and existing unique index on `(ChannelId, UserId)`. Verify this is sufficient.
- [ ] **9.2 Add migration SQL** — If any additive schema change is needed (e.g. index on `UserId` alone for faster user-scoped queries), add it to `LeanKernelDbContextSchemaExtensions` with `CREATE INDEX IF NOT EXISTS` style SQL.

### Phase 10: Update Deployment Configuration

- [ ] **10.1 Verify oauth2-proxy header forwarding** — In `deploy/leankernel/docker-stack.yml`, confirm these env vars are set on the `oauth2-proxy` service:
  - `OAUTH2_PROXY_SET_XAUTHREQUEST: "true"`
  - `OAUTH2_PROXY_PASS_ACCESS_TOKEN: "true"`
  - `OAUTH2_PROXY_SET_AUTHORIZATION_HEADER: "true"`
  - Add `OAUTH2_PROXY_SET_XAUTHREQUEST_GROUPS` or user-specific options if the tested version requires them
- [ ] **10.2 Add Gateway auth env vars to engine service** — Add environment variables:
  - `LEANKERNEL__AUTH__FORWARDEDHEADERS__ENABLED: "true"`
  - `LEANKERNEL__AUTH__FORWARDEDHEADERS__REQUIREAUTHENTICATEDUSER: "true"`
- [ ] **10.3 Bump config version** — If `engine-entrypoint.sh` changes (e.g. to export auth-related vars), rename the config (e.g. `engine_entrypoint_v3` → `engine_entrypoint_v4`) per the immutable configs rule
- [ ] **10.4 Update `.env`** — Add any new env var defaults (if needed for local dev)

### Phase 11: Update Documentation

- [ ] **11.1 Update `docs/deployment/stacks/leankernel.md`** — Document:
  - oauth2-proxy provides the browser session
  - Gateway derives the stable user key from forwarded headers
  - Browser-local GUIDs are legacy only
  - History is unified by authenticated user across browsers
  - Migration behavior (best-effort, idempotent)
  - Ownership enforcement rules

### Phase 12: Testing

- [ ] **12.1 Unit tests for `ForwardedAuthHandler`** — Test header parsing, claim extraction, user key precedence, unauthenticated fallback
- [ ] **12.2 Unit tests for `ChatService` migration** — Test `MigrateUserSessionsAsync` with matching/non-matching GUIDs, idempotency, race handling
- [ ] **12.3 Unit tests for session ownership** — Test `SessionBelongsToUserAsync` in `PostgresSessionStore` (via `IDbContextFactory<LeanKernelDbContext>` with InMemory)
- [ ] **12.4 Update `GatewayEndpointTests.cs`** — Add tests for:
  - Authenticated `/api/chat` requests derive `SenderId` from auth, not from `ChatRequest.UserId`
  - Requests with spoofed `UserId` are ignored when auth is present
  - Requests without auth are rejected (401)
  - Session ownership enforcement returns 404 for unowned sessions
- [ ] **12.5 Integration tests** — Add/verify:
  - Two simulated authenticated requests with the same claim but different legacy owner IDs see the same Blazor session list after migration
  - Two different authenticated users cannot open each other's `/chat/{sessionId}` URL
  - Two different authenticated users cannot send `/api/chat` to another user's `SessionId`
  - `/api/chat` where `UserId = "attacker"` but auth resolves to `user-a`; `SenderId` must be `user-a`
- [ ] **12.6 Run existing test suite** — `dotnet test src/LeanKernel.sln` and confirm no regressions

### Phase 13: Deploy & Verify

- [ ] **13.1 Build and deploy** — `./deploy/leankernel/scripts/deploy.sh --build --build-tag <unique-tag>` from the swarm repo
- [ ] **13.2 Verify service health** — `DOCKER_HOST="ssh://192.168.1.5" docker stack services leankernel` and `docker service logs leankernel_engine --tail 100`
- [ ] **13.3 Manual browser validation** — Sign in as the same user in two browsers, create a session in browser A, refresh browser B → session appears with same turns
- [ ] **13.4 Manual migration validation** — Use a browser with existing `leankernel.chat.owner-id` and old sessions; after deploy confirm old sessions appear, then confirm second browser sees them too
- [ ] **13.5 Negative validation** — Sign in as a different user; first user's sessions don't appear; direct `/chat/{sessionId}` navigation shows "not found"

---

## Key Files Reference

| File | Change Type |
|------|-------------|
| `src/LeanKernel.Gateway/Auth/ForwardedAuthHandler.cs` | **New** |
| `src/LeanKernel.Gateway/Auth/ForwardedAuthOptions.cs` | **New** |
| `src/LeanKernel.Gateway/Auth/ClaimsExtensions.cs` | **New** |
| `src/LeanKernel.Gateway/Program.cs` | Edit |
| `src/LeanKernel.Gateway/Components/Pages/Chat.razor` | Edit |
| `src/LeanKernel.Gateway/Services/ChatService.cs` | Edit |
| `src/LeanKernel.Gateway/Endpoints.cs` | Edit |
| `src/LeanKernel.Gateway/Models/ChatRequest.cs` | Edit (doc comment) |
| `src/LeanKernel.Abstractions/Interfaces/ISessionStore.cs` | Edit |
| `src/LeanKernel.Persistence/PostgresSessionStore.cs` | Edit |
| `src/LeanKernel.Persistence/Resilience/ResilientSessionStore.cs` | Edit |
| `src/LeanKernel.Persistence/Resilience/DegradedSessionBuffer.cs` | Edit |
| `test/LeanKernel.Tests.Unit/Persistence/PostgresSessionStoreTests.cs` | Edit (add new tests) |
| `test/LeanKernel.Tests.Integration/GatewayEndpointTests.cs` | Edit (add auth tests) |
| `deploy/leankernel/docker-stack.yml` | Edit (add env vars) |
| `deploy/leankernel/config/engine-entrypoint.sh` | Edit (if needed) |
| `deploy/leankernel/.env` | Edit (if needed) |
| `docs/deployment/stacks/leankernel.md` | Edit |

---

## Decisions Log

| Decision | Rationale |
|----------|-----------|
| Auth applies to both Blazor UI and `/api/chat` | Consistent security model |
| Reuse `SessionEntity.UserId` for auth key | No schema migration needed |
| Prefer oauth2-proxy forwarded headers over in-Gateway OIDC | Leverages existing auth boundary |
| User key precedence: `sub` > email > forwarded user | `sub` is immutable; email is reliable fallback |
| Legacy migration is best-effort, not blocking | Avoids deploy failures due to migration issues |
| UI sessions stay scoped to `blazor:` prefix | No product decision to merge with API sessions yet |
| Session ownership check fails closed (not-found) | Security best practice — don't reveal existence of other users' sessions |
