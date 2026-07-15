# Phase 11 Activities

## Step-By-Step Activities
1. Define the connector abstraction: a provider descriptor (auth type, scopes, endpoints) and a runtime contract for making authorized calls on behalf of a person.
2. Implement OAuth 2.0 authorization-code + refresh flows and API-key/PAT connectors; handle redirect callback, state/PKCE, and error paths.
3. Implement a per-person encrypted credential vault: store access/refresh tokens keyed by person + provider, encrypted at rest, with metadata (scopes, expiry).
4. Implement token lifecycle: proactive refresh, expiry handling, scope tracking, and re-consent prompts when scopes are insufficient or revoked upstream.
5. Implement a connector registry that exposes provider operations as governed MAF tools, applying tool governance and egress rules; default writes to conservative/gated behavior.
6. Implement connect/disconnect management: initiate authorization, persist the grant, list connected accounts, and revoke (clearing vault entries and upstream tokens where supported).
7. Build one reference connector against a minimal read scope to validate the full flow end to end.
8. Add configuration (provider client id/secret from secrets, redirect URIs, enabled providers) and startup validation.
9. Add tests: authorization flow, token refresh/expiry, encryption round-trip, revocation, per-person isolation, and governed tool invocation.
10. Document the connector framework and how to add a provider in `docs/features/` and `docs/configuration/`.

## Review Focus
- Tokens are encrypted at rest and never logged.
- Credentials are strictly person-scoped and tenant-isolated.
- OAuth state/PKCE prevents CSRF/authorization-code injection.
- Revocation fully clears local and (where possible) upstream grants.
- Connector tools honor governance and egress; writes are gated pending Phase 14.
