# Phase 06 Risk Register

## Risks

| ID | Risk | Impact | Mitigation | Status |
| --- | --- | --- | --- | --- |
| R1 | Sender auth is not truly fail-closed | Unauthorized access to the agent | Default-reject design + explicit allowlist tests | Open |
| R2 | Inbound identity mapping crosses partitions | Isolation breach | Reuse identity resolver + isolation keys; mapping tests | Open |
| R3 | Signal daemon disconnects cause message loss | Missed conversations | Backoff reconnect + acknowledgement/replay handling | Open |
| R4 | Keep-alive races with response delivery | Garbled output ordering | Serialize keep-alive vs send; ordering tests | Open |
| R5 | Malformed attachments crash the receive loop | Channel outage | Defensive parsing + isolation of parse failures | Open |
| R6 | Wildcard-default sharing policy unexpectedly exposes memory across channels | Privacy surprise | Document default as open sharing (`*`/`*`); provide restrictive rollout defaults for sensitive tenants; make isolation easy to configure; when policy is narrowed, Phase 10 directional AND requires bilateral grants | Open |
| R7 | Policy schema references unknown/renamed channels | Silent misconfiguration | Startup validation rejects unknown-channel references; normalize wildcard usage | Open |

## Open Decisions
- Signal daemon deployment (bundled container vs external service).
- Whether channel identities are auto-provisioned or require onboarding.
- Whether memory sharing policy is configured per-tenant, per-person, or both (defaults in config; per-channel persisted overrides). Coordinate with Phase 10 identity model.
