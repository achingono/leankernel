# Phase 06 Exit Criteria

## Gate Checklist
- [x] Signal and Teams terminals run as standalone `src/Terminals/LeanKernel.Channels.{Signal,Teams}` client processes with clean lifecycle.
- [x] Terminals call the Gateway over HTTP with pre-provisioned claims; the Gateway's existing middleware and turn pipeline process the message (single identity/authz path).
- [x] Inbound messages from pre-provisioned senders are answered end-to-end on both Signal and Teams.
- [x] Unknown/unconfigured senders are rejected fail-closed at the terminal (no token) and at the Gateway (no matching user/binding); no auto-provisioning occurs.
- [x] Provisioned credentials are least-privilege and revocable; a leaked credential can only assert pre-bound identities and can never cross tenants.
- [x] Inbound messages resolve `TenantId` from the binding/claims (not an HTTP host), and cross-tenant resolution is impossible.
- [x] The Gateway resolves `ChannelId` from the authenticated claims (not the hardcoded `openai-http`).
- [x] Inbound sender identifiers resolve to the correct **existing** persisted `UserEntity` (stable across turns) via issuer/subject (resolve-not-create for channels).
- [x] Long-running turns keep the channel session alive via typing-indicator keep-alive derived from the streaming response.
- [ ] Terminals reconnect/retry on transport failure without dropping or duplicating messages. (Signal has fixed-delay retry; Teams main loop lacks exception handling.)
- [x] Attachment parsing handles valid and malformed inputs safely (Signal + Teams).
- [x] Per-channel memory sharing policy exists with directional `Share`/`Access` allow-lists, wildcard (`*`) support, and wildcard as the default for both directions.
- [x] Policy resolution yields the correct effective readable-channel set using directional AND semantics (X readable in C iff X shares to C and C accesses X), and self is always readable.
- [ ] Explicit isolation is achievable (empty/self-only lists) and startup validation rejects unknown-channel references and bindings referencing unknown tenants/users. (Startup validation not yet implemented.)
- [x] The policy resolution contract exposes both the effective readable-channel set and the mutually visible set (the Phase 10 5W1H reconciliation boundary), and is available for Phase 10 to enforce (no memory read/write behavior changed in this phase).
- [ ] Unit + integration tests cover channel-claim trust, tenant/user/channel resolution from claims, fail-closed rejection, Signal + Teams transport, keep-alive, attachments, and memory-policy resolution. (Channel memory policy tests exist; terminal-specific transport tests pending.)

## Approval Table

| Role | Name | Status | Notes |
| --- | --- | --- | --- |
| Owner | | Pending | |
| Reviewer | | Pending | |
| Approver | | Pending | |
