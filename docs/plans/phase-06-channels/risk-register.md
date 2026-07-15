# Phase 06 Risk Register

## Risks

| ID | Risk | Impact | Mitigation | Status |
| --- | --- | --- | --- | --- |
| R1 | Sender auth is not truly fail-closed | Unauthorized access to the agent | Default-reject design + explicit allowlist tests | Open |
| R2 | Inbound identity mapping crosses partitions | Isolation breach | Reuse identity resolver + isolation keys; mapping tests | Open |
| R3 | Signal daemon disconnects cause message loss | Missed conversations | Backoff reconnect + acknowledgement/replay handling | Open |
| R4 | Keep-alive races with response delivery | Garbled output ordering | Serialize keep-alive vs send; ordering tests | Open |
| R5 | Malformed attachments crash the receive loop | Channel outage | Defensive parsing + isolation of parse failures | Open |

## Open Decisions
- Signal daemon deployment (bundled container vs external service).
- Whether channel identities are auto-provisioned or require onboarding.
