# Identity Partitioning

Identity partitioning is a core runtime feature of the current implementation.

## Current Model

The runtime resolves and uses persisted identities for:

- tenant
- person
- user
- channel

Anonymous traffic also uses the ASP.NET session id as an additional isolation dimension.

## Resolution Path

`TenantResolutionMiddleware` resolves and stores:

1. tenant from request host
2. user from authenticated claims or a guest-user fallback
3. person from the resolved user (`UserEntity.PersonId`, defaulting to the user id for unlinked identities)
4. channel from the request surface

`RequestContextPermit` then reads these resolved values from `HttpContext.Items` and exposes them via `IPermit`.

Code anchors:

- [`../../src/Services/LeanKernel.Gateway/Providers/RequestContextPermit.cs`](../../src/Services/LeanKernel.Gateway/Providers/RequestContextPermit.cs)
- [`../../src/Services/LeanKernel.Gateway/Providers/TenantResolutionMiddleware.cs`](../../src/Services/LeanKernel.Gateway/Providers/TenantResolutionMiddleware.cs)
- [`../../src/Common/LeanKernel.Logic/Providers/IdentityResolver.cs`](../../src/Common/LeanKernel.Logic/Providers/IdentityResolver.cs)
- [`../../src/Services/LeanKernel.Gateway/Providers/IdentityIsolationKeyProvider.cs`](../../src/Services/LeanKernel.Gateway/Providers/IdentityIsolationKeyProvider.cs)

## Where It Is Enforced

- chat history ownership checks
- agent state storage keys
- memory retrieval and persistence scope
- startup seeding of tenant/channel records

These boundaries are enforced through different runtime paths:

- `IdentityIsolationKeyProvider` scopes agent-session storage keys
- `MemoryScope` and `IMemoryClient` scope memory retrieval and persistence
- `DbChatHistoryProvider` verifies transcript ownership against the current permit

## Cross-Channel Memory Identity

- Memory now scopes by `tenant/person/channel` and keeps channel as a first-class dimension.
- `TenantResolutionMiddleware` writes both `LK.UserId` and `LK.PersonId` into `HttpContext.Items`.
- Linking/unlinking is implemented at the identity resolver layer by assigning users to a shared `PersonId`.
- Agent-session isolation remains `tenant/channel/user` (plus session for anonymous requests); this phase does not repurpose session isolation for memory scoping.

## Identity Claims Context

- Authenticated identity claims are persisted as a durable profile on `UserEntity` and refreshed on each authenticated resolution.
- The profile captures an allowlisted set of fields (name, email, preferred username, locale, timezone, organization, roles/groups) plus configured custom claims.
- Identity context is rendered deterministically by `IdentityContextAssembler` and injected by `MemoryProvider` as an AI context message each turn.
- Prompt rendering is bounded by `Identity:ClaimsContext:MaxPromptTokens`; oversized blocks are truncated to the configured budget.
- Custom claims remain deny-by-default through `Identity:ClaimsContext:AllowedCustomClaims`, and only fields listed in `Identity:ClaimsContext:PromptFields` are rendered.

Code anchors:

- [`../../src/Common/LeanKernel.Logic/Providers/IdentityResolver.cs`](../../src/Common/LeanKernel.Logic/Providers/IdentityResolver.cs)
- [`../../src/Common/LeanKernel.Logic/Providers/IdentityContextAssembler.cs`](../../src/Common/LeanKernel.Logic/Providers/IdentityContextAssembler.cs)
- [`../../src/Common/LeanKernel.Logic/Providers/MemoryProvider.cs`](../../src/Common/LeanKernel.Logic/Providers/MemoryProvider.cs)
- [`../../src/Common/LeanKernel.Core/Entities/UserEntity.cs`](../../src/Common/LeanKernel.Core/Entities/UserEntity.cs)

## Why It Matters

This keeps transcript data, runtime state, and memory context isolated per tenant/person/channel boundary (with user-level isolation preserved for transcript/session paths) instead of relying on raw claims or host strings alone.
