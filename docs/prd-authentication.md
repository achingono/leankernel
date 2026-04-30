# PRD: Authentication & Authorization

## 1. Overview

LeanKernel currently exposes all Host and API endpoints without application-level authentication or authorization. This PRD defines a simple default auth model that works out-of-the-box for single-admin deployments, while supporting pluggable higher-complexity providers (OIDC/OAuth) without reworking controllers or UI routing.

## 2. Goals

- **Secure by default**: Fresh installs require auth setup before the app is usable.
- **Simple baseline**: Single passcode login for UI + generated API tokens for programmatic access.
- **Pluggable providers**: Generic OIDC/OAuth support without hard-coding specific IdPs.
- **Migration-safe**: Existing installations upgrade gracefully without lockout.
- **Defense in depth**: Server-enforced authorization on every endpoint, not just UI gating.

## 3. Non-Goals (v1)

- Multi-user identity management (future).
- Fine-grained RBAC beyond `admin` / `api_client` (future).
- OAuth2 resource-server semantics for third-party consumers (future).
- Federated identity linking across providers (future).

---

## 4. Authentication Modes

| Mode | Description | Default |
|------|-------------|---------|
| `LocalPasscode` | Admin passcode → cookie session; API tokens for machines | ✅ |
| `Oidc` | OpenID Connect challenge/callback; claims → LeanKernel roles | — |
| `Disabled` | No auth enforcement (dev-only; restricted to `ASPNETCORE_ENVIRONMENT=Development`) | — |

### 4.1 Local Passcode Mode

- Interactive login via passcode → secure HttpOnly cookie session.
- Passcode stored using **Argon2id** (or PBKDF2 with ≥100k iterations) with per-secret salt.
- Timing-safe comparison for passcode verification.
- API token generation for programmatic clients:
  - Raw token displayed exactly once on creation.
  - Only hash + metadata stored at rest.
  - Supports list, revoke, rotate operations.

### 4.2 OIDC Mode

- Standard OpenID Connect Authorization Code flow.
- Configuration: authority URL, client ID, client secret, callback path, scopes, claim mappings.
- Admin identity whitelisting: exact `issuer + subject` pair (or exact email claim) must match configured value; all other identities rejected.
- Bundled local dev provider (`plainscope/simple-oidc-provider`) for testing.

### 4.3 Disabled Mode

- Only available when `ASPNETCORE_ENVIRONMENT=Development`.
- Ignored in Production/Staging — logs a warning and falls back to `LocalPasscode`.
- Intended for local development iteration only.

---

## 5. Authorization Model

### 5.1 Roles

| Role | Source |
|------|--------|
| `admin` | Local passcode session, or mapped OIDC admin claim |
| `api_client` | Valid API bearer token |

### 5.2 Policies

| Policy | Required Claims |
|--------|----------------|
| `UiAuthenticated` | Cookie-authenticated user (admin role) |
| `AdminOnly` | `admin` role via any scheme |
| `ApiAccess` | Valid API token **only** (bearer scheme required) |

### 5.3 Endpoint Policy Mapping

| Endpoint Group | Policy | Notes |
|---------------|--------|-------|
| `/api/health` | Anonymous | Liveness/readiness probes |
| `/api/auth/login`, `/api/auth/challenge` | Anonymous | Login flow |
| OIDC callback endpoints | Anonymous | Provider callbacks |
| `/api/onboarding/*` | Anonymous during bootstrap; `AdminOnly` after completion | Hardened post-setup |
| `/api/config`, `/api/files`, `/api/logs`, `/api/stats` | `AdminOnly` | Sensitive admin APIs |
| `/api/chat/*`, `/api/sessions/*` | `AdminOnly` | Chat session management |
| `/v1/*` (OpenAI-compatible) | `ApiAccess` (bearer token only) | See §6.1 |
| `/_blazor` (SignalR hub) | `UiAuthenticated` | Circuit auth |

### 5.4 CSRF Protection Strategy

**Decision: `/v1/*` is bearer-token only (no cookie auth).**

This simplifies CSRF handling:
- All cookie-authenticated state-changing endpoints enforce ASP.NET Core antiforgery validation.
- `/v1/*` requires bearer token in `Authorization` header — not susceptible to CSRF.
- Blazor Server handles antiforgery internally for form posts.

---

## 6. Security Requirements

### 6.1 Cookie Configuration

| Setting | Value | Notes |
|---------|-------|-------|
| `HttpOnly` | `true` | Prevent XSS token theft |
| `SameSite` | `Lax` | Block cross-origin form POSTs |
| `Secure` | Environment-aware | `Always` when HTTPS detected or forwarded headers indicate HTTPS; `SameAsRequest` for plain HTTP dev |
| `Path` | `/` | Scoped to app root |
| `Expiration` | 8 hours (sliding) | Configurable via `LeanKernelConfig.Auth.SessionDurationMinutes` |

### 6.2 Session Revocation & Security Stamp

- Store a **security stamp** (random value) in persistent auth state.
- Include stamp in cookie claims and validate on every request via `CookieAuthenticationEvents.OnValidatePrincipal`.
- Bump stamp on: passcode change, auth mode switch, explicit "revoke all sessions", token purge.
- Forces all existing cookies to become invalid immediately.

### 6.3 Blazor Circuit Revalidation

- Implement `RevalidatingServerAuthenticationStateProvider` with a 30-second revalidation interval.
- On revalidation failure (stamp mismatch, cookie expired): force circuit disconnect and redirect to login.
- On passcode change or logout-everywhere: bump stamp → all circuits invalidated within 30s.

### 6.4 Data Protection Key Persistence

- ASP.NET Data Protection keys persisted to `/app/data/.keys/` directory.
- Mounted via Docker volume alongside other persistent data.
- Prevents cookie invalidation on container recreation/redeploy.

### 6.5 Rate Limiting

- Login endpoint (`POST /api/auth/login`):
  - Per-IP: 5 attempts per minute, 20 per hour.
  - Global: 50 attempts per minute.
  - Exponential backoff response (429 with `Retry-After` header).
- Token creation: 10 per hour per authenticated session.

### 6.6 CORS Policy

- Default: same-origin only (no `AllowAnyOrigin`).
- Configurable allowed origins list in `LeanKernelConfig.Auth.AllowedOrigins` for reverse-proxy setups.
- `/v1/*` may have a separate, stricter CORS policy if needed for API consumers.

### 6.7 Forwarded Headers

- Configure `ForwardedHeadersOptions` before auth middleware.
- Required for correct `Secure` cookie policy and OIDC redirect URIs behind reverse proxies.

---

## 7. API Surface

### 7.1 Auth Controller (`/api/auth`)

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/api/auth/login` | Anonymous (rate-limited) | Passcode login → set cookie |
| POST | `/api/auth/logout` | `UiAuthenticated` | Clear session cookie |
| GET | `/api/auth/me` | `AdminOnly` | Current principal info |
| POST | `/api/auth/passcode` | `AdminOnly` | Change passcode (requires current) |
| GET | `/api/auth/tokens` | `AdminOnly` | List API tokens (metadata only) |
| POST | `/api/auth/tokens` | `AdminOnly` | Create new API token (returns raw once) |
| DELETE | `/api/auth/tokens/{id}` | `AdminOnly` | Revoke specific token |
| POST | `/api/auth/revoke-sessions` | `AdminOnly` | Bump security stamp, invalidate all sessions |

### 7.2 API Token Schema (Persisted)

```json
{
  "id": "tok_abc123",
  "name": "CI Pipeline",
  "hash": "<argon2id hash of raw token>",
  "createdAt": "2026-04-30T12:00:00Z",
  "lastUsedAt": "2026-04-30T13:45:00Z",
  "expiresAt": "2026-07-30T12:00:00Z",
  "revokedAt": null
}
```

- Default expiration: 90 days (configurable, nullable for non-expiring).
- `lastUsedAt` updated on each successful authentication.

---

## 8. Migration & Upgrade Path

### 8.1 Existing Installations (No Auth State)

When an upgraded LeanKernel instance starts and detects:
- Onboarding is complete (existing install), AND
- No auth state file exists

**Behavior:**
1. Start in `LocalPasscode` mode with a **one-time bootstrap token** written to container logs and `/app/data/.auth-bootstrap-token`.
2. Display a prominent "Secure your instance" banner in the UI.
3. First use of the bootstrap token sets the initial passcode (same flow as onboarding auth bootstrap).
4. Bootstrap token is invalidated after first use.
5. Normal auth enforcement begins.

This prevents lockout while ensuring security isn't silently disabled.

### 8.2 Auth Mode Transitions

When auth mode changes (e.g., `LocalPasscode` → `Oidc`):
1. Security stamp is bumped (all cookie sessions invalidated).
2. API tokens remain valid (they are mode-independent).
3. Change takes effect immediately (no restart required via `IOptionsMonitor`).
4. Audit log entry recorded.

---

## 9. Onboarding Integration

### 9.1 New Installations

The onboarding wizard gains an **"Authentication"** step (after service config, before completion):
1. Set initial admin passcode (required, minimum 8 characters).
2. Optionally generate first API token.
3. Optionally configure OIDC provider (advanced).

### 9.2 Post-Onboarding Security

After onboarding completion:
- All `/api/onboarding/*` endpoints require `AdminOnly` policy.
- Re-entering onboarding (e.g., for reconfiguration) requires authenticated admin session.

---

## 10. Observability & Audit

### 10.1 Auth Events Logged

| Event | Level | Data |
|-------|-------|------|
| Login success | Information | timestamp, source IP |
| Login failure | Warning | timestamp, source IP, reason |
| Logout | Information | timestamp |
| Passcode changed | Warning | timestamp |
| Token created | Information | token name, expiry |
| Token revoked | Information | token name |
| Auth mode changed | Warning | old mode → new mode |
| Sessions revoked | Warning | timestamp, reason |
| Rate limit triggered | Warning | source IP, endpoint |

### 10.2 Health Check

`/api/health` remains anonymous and reports auth subsystem readiness (configured, state file accessible) without exposing sensitive details.

---

## 11. Configuration Model

```csharp
public sealed class AuthConfig
{
    public AuthMode Mode { get; set; } = AuthMode.LocalPasscode;
    public int SessionDurationMinutes { get; set; } = 480; // 8 hours
    public int TokenDefaultExpirationDays { get; set; } = 90;
    public string[] AllowedOrigins { get; set; } = [];
    public LocalPasscodeConfig Local { get; set; } = new();
    public OidcConfig Oidc { get; set; } = new();
    public RateLimitConfig RateLimit { get; set; } = new();
}

public enum AuthMode { LocalPasscode, Oidc, Disabled }

public sealed class LocalPasscodeConfig
{
    public int MinLength { get; set; } = 8;
    public int MaxFailedAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
}

public sealed class OidcConfig
{
    public string Authority { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string CallbackPath { get; set; } = "/auth/oidc/callback";
    public string[] Scopes { get; set; } = ["openid", "profile", "email"];
    public string AdminSubjectClaim { get; set; } = ""; // Required: issuer+sub or email
    public string AdminClaimType { get; set; } = "sub"; // Which claim to match
}

public sealed class RateLimitConfig
{
    public int LoginPerMinutePerIp { get; set; } = 5;
    public int LoginPerHourPerIp { get; set; } = 20;
    public int LoginPerMinuteGlobal { get; set; } = 50;
    public int TokenCreationPerHour { get; set; } = 10;
}
```

---

## 12. Implementation Plan

### Phase 1: Foundation

| # | Todo | Description | Depends On |
|---|------|-------------|------------|
| 1 | `auth-config-contracts` | `AuthConfig` model, service interfaces, auth mode enum, security stamp model | — |
| 2 | `auth-local-provider` | Passcode service (Argon2id hashing, timing-safe verify), token store (hash + metadata, CRUD), security stamp management | 1 |
| 3 | `auth-data-protection` | Persist Data Protection keys to `/app/data/.keys/`, configure in `Program.cs` | — |

### Phase 2: Middleware & Endpoints

| # | Todo | Description | Depends On |
|---|------|-------------|------------|
| 4 | `auth-middleware` | Cookie scheme, bearer scheme, policy definitions, `OnValidatePrincipal` stamp check, forwarded headers, CORS tightening | 1, 2, 3 |
| 5 | `auth-controller` | Login/logout/me/passcode-change/token-CRUD/revoke-sessions endpoints, rate limiting on login | 2, 4 |
| 6 | `auth-endpoint-policies` | Apply `[Authorize]` + policies across all controllers, harden onboarding endpoints post-setup, explicit `[AllowAnonymous]` allowlist | 4, 5 |

### Phase 3: UI & Onboarding

| # | Todo | Description | Depends On |
|---|------|-------------|------------|
| 7 | `auth-blazor-flow` | Login page, `RevalidatingServerAuthenticationStateProvider` (30s interval), `AuthorizeRouteView`, layout auth gating, logout action | 4, 5, 6 |
| 8 | `auth-onboarding` | Passcode setup step in wizard, bootstrap token for upgrades, post-setup endpoint hardening | 2, 5, 7 |

### Phase 4: Extensibility

| # | Todo | Description | Depends On |
|---|------|-------------|------------|
| 9 | `auth-oidc` | OIDC scheme registration, admin identity whitelist validation, claim mapping, bundled dev provider config | 1, 4, 6 |

### Phase 5: Quality

| # | Todo | Description | Depends On |
|---|------|-------------|------------|
| 10 | `auth-tests` | Unit tests (passcode service, token store, stamp validation, policy evaluation, controller paths), integration tests (unauthorized/forbidden checks), rate limit tests | All |
| 11 | `auth-audit-docs` | Auth event logging implementation, README updates, configuration examples | All |

---

## 13. Streaming & Long-Lived Connections

- `/v1/*` streaming (SSE/chunked responses): Auth validated at connection start only.
- Expired tokens do not terminate in-flight streams (accepted trade-off).
- Auth re-evaluated on reconnection/new request.
- Documented as known behavior.

---

## 14. Open Questions

| # | Question | Default if unresolved |
|---|----------|----------------------|
| 1 | Should non-expiring API tokens be allowed? | Yes, but visually flagged in UI |
| 2 | Maximum number of API tokens per installation? | 50 (configurable) |
| 3 | Should failed login attempts be surfaced in UI? | Yes, last 5 shown on login page |

---

## 15. Success Criteria

- [ ] Fresh install requires passcode setup during onboarding before app is usable.
- [ ] All API endpoints return 401/403 without valid credentials.
- [ ] API tokens work for `/v1/*` without cookie/CSRF overhead.
- [ ] Existing installations upgrade without lockout (bootstrap token flow).
- [ ] Passcode change invalidates all active sessions within 30 seconds.
- [ ] OIDC login works with a standards-compliant provider.
- [ ] Docker container recreation preserves auth state and session validity.
- [ ] All auth events are logged for auditability.
