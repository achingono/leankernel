# Phase 15 Risk Register

## Risks

| ID | Risk | Impact | Mitigation | Status |
| --- | --- | --- | --- | --- |
| R1 | Unverified auto-mapping binds a spoofed number to a user | Account takeover | Require verification for claims; admin-only pre-provisioning; no trust of unverified sender ids | Open |
| R2 | Inconsistent normalization causes duplicate/missed users | Fragmented identity | Canonical normalization on write + lookup; normalization tests | Open |
| R3 | Cross-tenant identifier collision | Isolation breach | Tenant-scoped uniqueness; tenant in directory key | Open |
| R4 | Spoofable sender ids from the channel transport | Impersonation | Rely on transport-authenticated sender; treat weak ids as unverified | Open |
| R5 | Auto-provision creates unbounded junk users | Data bloat | Rate limits + known-only default for sensitive channels | Open |
| R6 | Guest-to-known promotion loses prior guest memory | Context loss | Deterministic guest->known merge path (with Phase 10) | Open |

## Open Decisions
- Default unknown-sender policy per channel (known-only vs guest fallback).
- Whether channel transport sender ids are trusted as verified (channel-dependent).
- Verification channel for first-contact claims.
