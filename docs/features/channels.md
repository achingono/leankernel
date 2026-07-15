# Channels

The gateway now supports channel-first identity and transport integration through standalone channel terminals.

## Terminal model

- Signal and Teams adapters run as out-of-process clients under `src/Terminals/LeanKernel.Channels.Signal` and `src/Terminals/LeanKernel.Channels.Teams`.
- Terminals forward inbound messages to the Gateway OpenAI-compatible endpoint and return the response to the native channel transport.
- Inbound messages without provisioned credentials are rejected fail-closed before they are sent to the Gateway.
- Terminal code uses transport-neutral class names (`TerminalService`, `GatewayClient`, `Settings`) and channel-specific project boundaries rather than duplicated `Signal*`/`Teams*` type prefixes.

## Session decisions captured

- Signal transport uses a sidecar daemon (`bbernhard/signal-cli-rest-api`, `MODE=json-rpc`) instead of embedding `signal-cli` inside the terminal image.
- Signal terminal is multi-account: it discovers accounts from `signal-cli` (`GET /v1/accounts`) and cycles receives across discovered accounts.
- Both terminals resolve channel bearer tokens from PostgreSQL (`ChannelSenderBindings`) rather than static appsettings token maps.
- Terminal configuration is flattened to top-level sections (`Gateway`, `Signal`, `Bot`) with no `Terminal` wrapper and no `TERMINAL__` environment-variable prefix.
- Terminal requests to `/v1/responses` include `agent.name` in addition to `model` and `input`; omitting `agent.name` is rejected by gateway as `missing_required_parameter`.

## Gateway identity resolution for channels

- `TenantResolutionMiddleware` accepts channel claims (`lk_tenant_id`, `lk_channel`, `lk_sender_iss`, `lk_sender_sub`).
- Tenant and channel are resolved from claims, not HTTP host or hardcoded `openai-http`.
- Sender identity resolves to an existing persisted user by issuer/subject.
- Sender access is fail-closed via active sender-binding validation against persisted `ChannelSenderBindingEntity` rows.

## Sender bindings

- `ChannelSenderBindingEntity` persists pre-provisioned sender mappings: sender issuer/subject to tenant, user, and channel.
- `ChannelSenderBindingEntity` persists the pre-provisioned `BearerToken` used by terminals.
- Bindings are revocable through `IsActive`.
- Gateway startup validation rejects invalid binding references and empty binding tokens.

## Terminal token resolution

- Signal terminal queries active bindings by channel `signal`, sender issuer `signal`, and sender subject.
- Teams terminal queries active bindings by channel `teams`, sender issuer `teams`, and sender subject.
- Missing token lookup is logged and the inbound message is fail-closed.
- Signal sender extraction requires `envelope.sourceNumber` (phone number); messages without it are rejected.
- Signal message extraction reads `envelope.dataMessage.message` and supports `envelope.syncMessage.sentMessage.message` for sync events.

## Teams ingress hardening

- `/api/messages` requires bearer authentication; unauthenticated requests are rejected before enqueue.
- Teams bearer validation uses Bot Framework OpenID metadata and validates issuer, audience (`Bot:AppId`), and lifetime.
- If token `serviceurl` claim is present and differs from payload `serviceUrl`, the request is rejected.
- Teams reply path sends only to trusted HTTPS `ServiceUrl` hosts configured in `Bot:AllowedServiceUrlHostSuffixes`.

## Container and compose topology

- `signal-terminal` image contains only terminal runtime; `signal-cli` is provided by compose sidecar.
- `signal-cli` persists account state in `signal-data` (`/home/.local/share/signal-cli` in sidecar).
- `signal-terminal` and `teams-terminal` both require `ConnectionStrings__Postgres` for token resolution.
- `teams-terminal` hosts `/api/messages` (Bot Framework ingress) and `/health` (container health probe).

## Operational notes from implementation

- `ChannelSenderBindings.BearerToken` must contain a JWT-shaped token (`header.payload.signature`); non-JWT strings are rejected by gateway bearer auth.
- Channel-terminal gateway auth requires channel claims in the bearer token (`lk_tenant_id`, `lk_channel`) in addition to sender claims (`iss`/`sub` or `lk_sender_iss`/`lk_sender_sub`).
- Signal sender-binding integrity must stay aligned: binding `UserId` should reference the same issuer/subject identity represented by the token sender claims.
- Some Signal receive events are non-text (for example typing/receipt envelopes) and are intentionally ignored by the terminal parser.

## Memory sharing policy model

- `ChannelMemoryPolicyEntity` stores tenant-scoped per-channel override lists: `ShareList` and `AccessList`.
- Defaults come from `Agents:Channels:MemoryPolicyDefaults` with wildcard (`*`) default for both directions.
- `ChannelMemoryPolicyResolver` computes:
  - effective readable channels (directional AND: target shares to source AND source accesses target)
  - mutually visible channels (readable in both directions)
- Startup normalization collapses explicit wildcard usage (for example `*,teams` -> `*`) and rejects unknown channel names.
- Explicit empty lists are preserved as empty (deny-all); they are not rewritten to wildcard.
