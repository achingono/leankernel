# ADR 0002: Partition runtime state by persisted tenant, user, and channel identities

- Status: Accepted
- Date: 2026-07-13

## Context

The planning logs initially considered partitioning around request host plus claim-derived identity. That approach was rejected during review and implementation because it broke down under common OIDC shapes and made anonymous handling inconsistent.

The decisive issues captured in the logs were:

- OIDC `sub` values are often opaque strings, not GUIDs.
- Deriving GUIDs from non-GUID claims would introduce a synthetic identity layer with collision and normalization risks.
- Hostname is input metadata, but once `TenantEntity` exists it becomes better to partition on persisted tenant identity.
- `SessionEntity.UserId` should remain non-nullable, which requires an explicit anonymous-user strategy rather than null ownership.

## Decision

All durable runtime partitioning will use persisted domain identities.

Canonical partitioning model:

- Authenticated traffic: `(TenantId, UserId, ChannelId)`
- Anonymous traffic: `(TenantId, UserId, SessionId, ChannelId)`

Additional rules:

- Request hostname is used only to resolve `TenantEntity`; `TenantId` is the durable partition key.
- Authenticated principals resolve to persisted `UserEntity.Id`; the system does not use claim-derived GUIDs as canonical user identity.
- The HTTP/OpenAI surface resolves to a persisted `ChannelEntity.Id`.
- Anonymous requests resolve to a persisted guest user so `SessionEntity.UserId` stays non-null.
- ASP.NET session id remains an extra isolation dimension for anonymous requests, not a substitute for `UserId`.

## Consequences

Positive:

- Partitioning is stable across providers, auth issuers, and request transports.
- The data model remains relationally clean because foreign keys stay non-null.
- Tenant, user, and channel become first-class concepts for authorization, auditing, and filtering.

Tradeoffs:

- Every request needs a resolution step before accessing state.
- Guest-user creation and lookup must be concurrency-safe.
- Reverse-proxy host handling must be correct, otherwise tenant resolution can drift.

## Evidence From Session Logs

- OpenCode session `ses_0abffac42ffeqKxiSWd1Vg90bD`, `2026-07-12`, "PRD implementation and architecture gap review"
  - Confirmed gaps around forwarded headers, GUID-only claim parsing, undefined channel identity, and conversation/session key semantics.
  - Recommended using non-GUID identities externally and persisted GUIDs internally after user/channel entities existed.
  - Explicitly pivoted the plan from `(HostName, UserId)` to `(TenantId, UserId, ChannelId)` and then to `(TenantId, UserId, SessionId, ChannelId)` for anonymous flows.
  - Updated the plan to make `SessionEntity.UserId` non-nullable with a tenant-scoped guest-user strategy.
- OpenCode session `ses_0a7203802ffeCCK07rFnwepiBK`, `2026-07-13`, "Configure gateway service health in docker-compose.yml"
  - Rejected singleton-scoped identity shortcuts and kept scoped identity resolution for correct tenant/user isolation.
