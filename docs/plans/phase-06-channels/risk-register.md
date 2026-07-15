# Phase 06 Risk Register

## Risks

| ID | Risk | Impact | Mitigation | Status |
| --- | --- | --- | --- | --- |
| R1 | Sender auth is not truly fail-closed | Unauthorized access to the agent | Fail-closed by construction: only pre-provisioned sender bindings accepted; rejected at terminal and Gateway; allowlist tests | Open |
| R2 | Inbound identity mapping crosses partitions | Isolation breach | Tenant + user resolved from provisioned claims/binding; resolve-not-create existing user; `ChannelId` from claims; mapping tests | Open |
| R3 | Signal/Teams transport disconnects cause message loss | Missed conversations | Backoff reconnect/retry + acknowledgement/replay handling | Open |
| R4 | Keep-alive races with response delivery | Garbled output ordering | Serialize keep-alive vs final send; drive keep-alive from streaming response; ordering tests | Open |
| R5 | Malformed attachments crash the receive loop | Channel outage | Defensive parsing + isolation of parse failures | Open |
| R6 | Wildcard-default sharing policy unexpectedly exposes memory across channels | Privacy surprise | Document default explicitly; make isolation easy to configure; Phase 10 enforces with directional AND so both sides must opt in | Open |
| R7 | Policy schema references unknown/renamed channels | Silent misconfiguration | Startup validation rejects unknown-channel references; normalize wildcard usage | Open |
| R8 | Inbound message resolves the wrong tenant (no HTTP host to key on) | Cross-tenant leak | Tenant carried in the provisioned binding/claims; validate mapping at startup; reject unmapped bindings | Open |
| R9 | Sender-identifier→user mapping is spoofable or collides across channels | Impersonation / wrong-user memory | Namespace subject by channel issuer (`iss=signal`/`iss=teams`, `sub=<id>`); resolve-not-create; uniqueness on (issuer, subject) | Open |
| R10 | Channel credential leak enables impersonation | Account takeover within a tenant | Least-privilege per-binding credentials; prefer short-lived tokens from a stored signing key; rotation + revocation; blast radius bounded to pre-bound identities, never cross-tenant | Open |
| R11 | Gateway hardcodes `ChannelId` to `openai-http` | Wrong channel partitioning + wrong memory scope/policy | Resolve `ChannelId` from authenticated claims on the channel path; tests asserting non-`openai-http` channel resolution | Open |

## Open Decisions
- Signal daemon deployment (bundled container vs external service).
- Teams Bot Framework registration/hosting model (Azure Bot resource, messaging endpoint exposure).
- Whether the channel→tenant scope is modeled on `ChannelEntity` directly or as a separate channel-account/binding entity (a channel type may serve multiple tenants via multiple accounts).
- Credential form for bindings: stored long-lived JWT vs short-lived token minted from a stored per-binding signing key (recommended).
- Whether memory sharing policy is configured per-tenant, per-person, or both (defaults in config; per-channel persisted overrides). Coordinate with Phase 10 identity model.

## Resolved Decisions
- **Channel-to-Gateway transport (Point 3): refined Option B** — terminals call Gateway HTTP endpoints supplying pre-provisioned per-binding claims; the Gateway's existing middleware/turn pipeline resolves identity and runs the turn. Chosen for a single identity/authz path and clean separation of concerns, with impersonation risk bounded by pre-provisioning.
- **Project placement: `src/Terminals/LeanKernel.Channels.{Signal,Teams}`** — channel adapters are client/edge terminal processes; `src/Services` stays reserved for server-side hosted services.
- **Built-in channels for this phase: Signal and Microsoft Teams.**
