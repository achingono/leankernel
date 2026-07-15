# Phase 15 Activities

## Step-By-Step Activities
1. Confirm the current fallback: channel/unknown senders resolve to `anonymous` guests via `ResolveGuestUserAsync`; document where inbound channel senders currently would land.
2. Define a channel-identity directory entity mapping `(tenantId, channelType, normalizedIdentifier) -> userId`, with a uniqueness constraint and normalized identifier storage; add EF entity + migration.
3. Implement identifier normalization (E.164 for phone numbers, lowercased/trimmed email, etc.) applied consistently on write and lookup.
4. Extend `IdentityResolver` with a channel-identity resolution method: given `(channelType, rawIdentifier)`, normalize, look up the directory, and return the mapped known `UserEntity`; represent channel users via `Issuer=<channelType>`/`Subject=<normalizedIdentifier>`.
5. Implement the unknown-sender policy (configurable per channel): known-only (reject/hold), auto-provision a distinct known user, or guest fallback.
6. Implement provisioning paths: admin/pre-provisioned association of an identifier with an existing user, and a first-contact claim/verification flow (one-time code) to bind a new sender to or create a user.
7. Integrate with Phase 10 so a resolved channel user maps to the canonical person and, once linked, shares cross-channel memory.
8. Wire resolution into the channel inbound path (Phase 06) so the permit reflects the known user instead of an anonymous guest.
9. Add configuration (normalization options, unknown-sender policy, provisioning mode) and startup validation.
10. Add tests: normalization correctness, known vs unknown resolution, provisioning/claim, uniqueness/collision handling, and tenant isolation.
11. Document channel identity mapping in `docs/features/` (identity partitioning / channels).

## Review Focus
- A mapped sender always resolves to the same known user (stable, normalized).
- Unknown-sender policy is enforced consistently and safely.
- Directory mappings are tenant-isolated; identifiers cannot collide across tenants.
- Claiming/verification cannot bind another person's identifier without proof.
- Resolved channel users integrate with the person layer and person-scoped memory.
