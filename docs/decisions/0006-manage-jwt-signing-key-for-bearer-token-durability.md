# ADR 0006: Manage JWT signing key for bearer token durability

- Status: Accepted
- Date: 2026-07-29

## Context

Channel sender bindings store pre-provisioned bearer tokens that let channel terminals (Signal, Teams) authenticate against the Gateway API. These tokens are JWTs signed with a symmetric key.

`JwtSecurityTokenGenerator` reads `Identity:Token:SecretKey` from configuration. When the key is empty (the default), it falls back to `DevSecretKey` — a `static readonly byte[]` initialized once per process with `RandomNumberGenerator.GetBytes(32)`.

This means every process restart generates a new key, invalidating every bearer token in the database. The Gateway container restarts on any deployment or host recycle, so tokens routinely become stale.

When JWT validation fails, ASP.NET's bearer handler sets `context.User` to an unauthenticated principal. `TenantResolutionMiddleware` does not treat this as an error — it silently falls through to Path C (anonymous/guest), creating a guest user with `FullName = "anonymous"`. The identity context block injected into the model prompt reads `full_name: anonymous`, and the model responds as though the user's name is "anonymous".

The failure is not surfaced to the caller or logged as an error, making it difficult to diagnose.

## Decision

Set `Identity:Token:SecretKey` to a fixed value in any deployment where bearer tokens must survive process restarts. This can be done:

- In `appsettings.json` or `appsettings.Development.json` for local development
- Via the `Identity__Token__SecretKey` environment variable in Docker Compose or production orchestration

The key should be a Base64-encoded 256-bit (or longer) value. Example generation:

```bash
openssl rand -base64 32
```

## Consequences

Positive:

- Bearer tokens remain valid across process restarts, deployments, and scale-out events.
- Channel terminals (Signal, Teams) maintain uninterrupted authentication without token regeneration.

Tradeoffs:

- The signing key becomes a secret that must be managed (stored securely, rotated periodically).
- If the key is compromised, all bearer tokens must be regenerated.
- Multi-instance deployments must share the same key.

## Debugging Bearer Token Failures

When a token is invalid due to a key mismatch, the symptom is a model response that treats the user as "anonymous". To confirm:

1. Decode the JWT payload from the binding record's `BearerToken` column.
2. Compare the `sid` (or `nameid`) claim against the `UserId` in persisted turn records.
3. A mismatch indicates the request was resolved through the anonymous/guest path rather than through the binding.

## Evidence From Session Logs

- OpenCode session `ses_05062dc27ffepsLooAb5paduHx`, `2026-07-29`:
  - User reported model responding "your name is anonymous" when sending a message with a bearer token from binding record `923366c0-bc99-4dcc-816b-24afb1ec4caa`.
  - Database inspection showed the guest user `de91a3c8-f892-4f8f-895f-913151dff49e` had `FullName = "anonymous"`, `IsGuest = true`.
  - The binding's linked user `6e355fd9-2f27-467c-8ed2-56cedb9e65aa` had `FullName = "Alfero Chingono"` — correct identity, never reached.
  - Logged turn events showed `authorName: "Anonymous User"` and identity context `full_name: anonymous`.
  - Gateway container had restarted 17 minutes before the test, generating a new `DevSecretKey` and invalidating the stored bearer token.