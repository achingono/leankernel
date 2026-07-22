# Phase 15 Channel Identity Mapping

## Companion Documents
- [Inputs](inputs.md)
- [Activities](activities.md)
- [Outputs](outputs.md)
- [Exit Criteria](exit-criteria.md)
- [Risk Register](risk-register.md)
- [Evidence](evidence.md)

## Objective
Resolve inbound channel-native identifiers to known user identities so that a request arriving over a non-HTTP channel — for example a Signal message from a phone number — is associated with a real `UserEntity` in the system rather than being treated as an anonymous guest. This provides the directory and resolution logic that maps `(channel, native-identifier)` (e.g., `signal:+15551234567`) to a persisted, non-anonymous user, using the existing `UserEntity.Issuer`/`Subject` model and feeding the person layer and person-scoped memory from Phase 10.

## Scope
This phase delivers the channel-identity directory, the resolution logic that turns a channel sender into a known user, and the provisioning/claim flows that populate the directory. It is the resolution counterpart to Phase 10's verified linking: Phase 10 defines the person model and cross-channel memory scope; this phase ensures a channel request resolves to a user at all. It does not implement channel transports (Phase 06) or the OAuth connector hub (Phase 11).

## In Scope
- A channel-identity directory: persisted mappings from `(tenantId, channelType, native-identifier)` to a `UserEntity`, normalized (e.g., E.164 for phone numbers, lowercased email) with uniqueness constraints.
- Resolution logic extending `IdentityResolver` so a channel sender resolves to a mapped known user; only unmapped senders fall back to a channel-scoped guest (or are rejected, per policy).
- Representation via the existing `UserEntity.Issuer`/`Subject` model (e.g., `Issuer="signal"`, `Subject="+15551234567"`) so channel identities are first-class users, not `anonymous`.
- Provisioning paths: admin/pre-provisioned mappings (associate a known number with an existing user) and first-contact claim/verification (a new sender proves ownership to bind to or create a user).
- Policy for unknown senders: known-only (reject/hold), auto-provision as a distinct known user, or guest fallback — configurable per channel.
- Integration with Phase 10 so a resolved channel user maps to the canonical person and shares cross-channel memory once linked.
- Configuration for identifier normalization, unknown-sender policy, and provisioning mode; startup validation.
- Tests for normalization, resolution (known vs unknown), provisioning/claim, uniqueness/collision handling, and tenant isolation.

## Out of Scope
- Channel transport/adapters themselves (Phase 06) — this phase consumes the sender identifier they provide.
- The person model, verified cross-identity linking, and person-scoped memory migration (Phase 10) — this phase depends on and feeds them.
- Third-party OAuth account authorization (Phase 11).

## Entry Criteria
- Identity resolution exists with the `Issuer`/`Subject` user model and guest fallback (`IdentityResolver.ResolveGuestUserAsync`, `ResolveOrCreateChannelAsync`).
- Phase 06 provides (or will provide) the channel sender's native identifier on inbound messages.
- Phase 10 person model is available or sequenced so channel users can map to a person.

## Exit Criteria
An inbound channel request carrying a native identifier (e.g., a Signal phone number) resolves to a known, non-anonymous `UserEntity` when a mapping exists, unmapped senders are handled per the configured policy, and the mapping is tenant-isolated and normalized. See `exit-criteria.md`.

## Status
**Partial** — sender-binding directory primitives and known-sender resolution are in place via `ChannelSenderBindings` and channel bearer tokens. Identifier normalization, configurable unknown-sender policy, and first-contact claim/verification flows remain open.

## Roles
- Owner: Rebuild maintainer
- Reviewer: Separate agent session / model review
- Approver: Repository owner
