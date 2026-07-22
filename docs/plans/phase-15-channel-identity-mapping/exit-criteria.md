# Phase 15 Exit Criteria

## Gate Checklist
- [ ] A channel-identity directory maps `(tenant, channelType, normalizedIdentifier)` to a `UserEntity` (current sender bindings key by raw `Issuer`/`Subject`; normalization remains open).
- [x] An inbound Signal message from a mapped phone number resolves to the known user (not anonymous).
- [ ] Identifiers are normalized consistently (E.164 phone, lowercased email) on write and lookup.
- [x] Channel users are represented via `Issuer`/`Subject` (e.g., `signal`/`+15551234567`).
- [ ] The unknown-sender policy (known-only / auto-provision / guest) is enforced per channel config.
- [ ] Provisioning (admin pre-provision) and first-contact claim/verification both work and are spoof-resistant.
- [x] Mappings are tenant-isolated with no cross-tenant identifier collisions.
- [ ] Resolved channel users map to the canonical person and share cross-channel memory once linked.
- [ ] Unit + integration tests cover normalization, resolution, provisioning, and isolation.

## Approval Table

| Role | Name | Status | Notes |
| --- | --- | --- | --- |
| Owner | | Pending | |
| Reviewer | | Pending | |
| Approver | | Pending | |
