# Authentication and Authorization

This document reflects the current auth implementation in `LeanKernel.Host`.

## Auth Modes

Configured via `LeanKernel:Auth:Mode`:

| Mode | Behavior |
| --- | --- |
| `LocalPasscode` | Passcode login creates cookie session; API tokens support bearer auth |
| `Oidc` | OIDC challenge/callback with mapped admin identity |
| `Disabled` | Development-only bypass; outside Development the app logs and falls back to enforced auth |

## Credential Storage

- Passcode hashing: **PBKDF2-SHA512**, 200,000 iterations, random salt, fixed-time compare (`PasscodeService`)

> **Note:** PBKDF2-SHA512 is used for compatibility and broad platform support. Argon2id is a modern alternative, but this implementation uses PBKDF2-SHA512 by design for simplicity and to avoid additional dependencies. .NET-compatible Argon2 libraries are available if future migration is desired.
- Session invalidation: security stamp claim (`LeanKernel:stamp`) checked on cookie validation
- API tokens:
  - Generated with prefix `sk-LeanKernel-`
  - Raw token shown once at create time
  - SHA-256 hash persisted in auth state
  - Revocation and expiration enforced in bearer handler

## Schemes and Policies

Schemes (`AuthConstants`):

- Cookie: `LeanKernelCookie`
- Bearer: `LeanKernelBearer`

Policies:

| Policy | Requirements |
| --- | --- |
| `UiAuthenticated` | Cookie auth + `admin` role |
| `AdminOnly` | Cookie or bearer auth + `admin` role |
| `ApiAccess` | Bearer auth + `api_client` or `admin` role |

## Endpoint Protection (Current)

Anonymous:

- `GET /api/health`
- `POST /api/auth/login`
- `POST /api/auth/login-form`
- `GET /api/auth/status`
- `POST /api/auth/bootstrap` (only until passcode is configured)
- `GET /api/onboarding/status`
- `GET /api/onboarding/agents/presets`

Admin policy (`AdminOnly` and/or onboarding-conditional admin checks):

- `/api/config` (`GET`, `PATCH`)
- `/api/files/*`, `/api/logs*`, `/api/stats`
- `/api/wiki/*`
- `/api/chat/*`
- `/api/routing-config*`
- `/api/model-limit-drift`
- most onboarding mutation routes after onboarding is complete

Bearer API policy (`ApiAccess`):

- `POST /v1/chat/completions`
- `GET /v1/models`

## Key API Endpoints

| Endpoint | Method | Description |
| --- | --- | --- |
| `/api/auth/login` | POST | Passcode login |
| `/api/auth/logout` | POST | Sign out cookie session |
| `/api/auth/me` | GET | Current principal summary |
| `/api/auth/passcode` | POST | Change passcode |
| `/api/auth/tokens` | GET/POST | List/create API tokens |
| `/api/auth/tokens/{id}` | DELETE | Revoke token |
| `/api/auth/revoke-sessions` | POST | Rotate security stamp and invalidate sessions |
| `/api/auth/bootstrap` | POST | One-time initial passcode setup |

## Config Fields (Auth)

`LeanKernel:Auth` supports:

- `Mode`
- `SessionDurationMinutes`
- `TokenDefaultExpirationDays`
- `AllowedOrigins`
- `Local` (`MinLength`, `MaxFailedAttempts`, `LockoutMinutes`)
- `Oidc` (`Authority`, `ClientId`, `ClientSecret`, `CallbackPath`, `Scopes`, `AdminSubjectClaim`, `AdminClaimType`)
- `RateLimit` (`LoginPerMinutePerIp`, `LoginPerHourPerIp`, `LoginPerMinuteGlobal`, `TokenCreationPerHour`)
