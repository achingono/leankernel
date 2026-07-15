# Phase 06 Exit Criteria

## Gate Checklist
- [ ] Enabled channel adapters run as managed hosted services with clean lifecycle.
- [ ] Inbound Signal messages from allowlisted senders are routed through the runtime and answered.
- [ ] Unknown/unlisted senders are rejected fail-closed and never processed.
- [ ] Inbound identity maps correctly to tenant/user/channel partitioning.
- [ ] Long-running turns keep the channel session alive via typing-indicator keep-alive.
- [ ] Adapter reconnects on daemon failure without dropping or duplicating messages.
- [ ] Attachment parsing handles valid and malformed inputs safely.
- [ ] Per-channel memory sharing policy exists with directional `Share`/`Access` allow-lists, wildcard (`*`) support, and wildcard as the default for both directions.
- [ ] Policy resolution yields the correct effective readable-channel set using directional AND semantics (X readable in C iff X shares to C and C accesses X), and self is always readable.
- [ ] Explicit isolation is achievable (empty/self-only lists) and startup validation rejects unknown-channel references.
- [ ] The policy resolution contract exposes both the effective readable-channel set and the mutually visible set (the Phase 10 5W1H reconciliation boundary), and is available for Phase 10 to enforce (no memory read/write behavior changed in this phase).
- [ ] Unit + integration tests cover routing, auth, keep-alive, attachments, identity mapping, and memory-policy resolution.

## Approval Table

| Role | Name | Status | Notes |
| --- | --- | --- | --- |
| Owner | | Pending | |
| Reviewer | | Pending | |
| Approver | | Pending | |
