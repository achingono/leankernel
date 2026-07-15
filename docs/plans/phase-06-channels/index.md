# Phase 06 Channels

## Companion Documents
- [Inputs](inputs.md)
- [Activities](activities.md)
- [Outputs](outputs.md)
- [Exit Criteria](exit-criteria.md)
- [Risk Register](risk-register.md)
- [Evidence](evidence.md)

## Objective
Add a channel abstraction to the rebuild so the agent can serve inbound/outbound conversations over transports beyond the OpenAI-compatible HTTP surface, delivered as **channel terminals** — standalone client processes that bridge an external transport (Signal daemon, Microsoft Teams Bot Framework) to the Gateway's HTTP endpoints, supplying **pre-provisioned claims** so the Gateway's existing identity/turn pipeline resolves tenant/user/channel and runs the turn. This ports the source repo's `LeanKernel.Channels` behavior — channel router, fail-closed per-channel sender authentication, typing-indicator keep-alive, and attachment parsing — while reusing the rebuild's identity partitioning (tenant/user/channel) and turn runtime. As part of the channel abstraction, this phase also defines and persists a **per-channel memory sharing/isolation policy** — a configurable, directional model that decides which channels may read a channel's memory and which channels' memory it may read — that Phase 10 enforces in the memory scoping engine.

## Scope
This phase introduces the channel terminal layer and two concrete adapters (**Signal** and **Microsoft Teams**), each a project under `src/Terminals/LeanKernel.Channels.{Signal,Teams}`. Terminals are out-of-process clients that authenticate to the Gateway over HTTP with provisioned claims (**refined Option B**); the Gateway's existing middleware (`TenantResolutionMiddleware`) and turn pipeline process the message, so identity resolution and turn execution stay single-sourced in the Gateway. This phase also introduces the **channel memory sharing policy** as configuration and persisted state (schema, defaults, validation, storage, and the resolution contract consumed by Phase 10) but does not itself change how memory is read or written — that enforcement is Phase 10. It does not add new tools, model routing, learning, scheduler, diagnostics UI, or Blazor UI. Channel adapters beyond Signal and Teams are out of scope for this phase.

## In Scope
- A channel terminal abstraction: a channel adapter contract (receive/send/reconnect against the native transport) plus a thin Gateway HTTP client that submits turns with provisioned claims and consumes the streaming response.
- Two concrete terminals under `src/Terminals`: `LeanKernel.Channels.Signal` (polls a Signal daemon) and `LeanKernel.Channels.Teams` (receives Microsoft Teams Bot Framework activities), each sending responses and reconnecting/retrying on failure.
- **Refined Option B integration**: terminals call Gateway HTTP endpoints supplying **pre-provisioned per-binding credentials** (a JWT provisioned during channel/user configuration). The Gateway trusts these channel-issued claims, and its existing middleware resolves tenant/user/channel and runs the turn — one identity/authz path shared with the web surface.
- Gateway-side changes to support channel claims: accept and validate the channel token issuer(s); resolve `ChannelId` from the authenticated claims (rather than the hardcoded `openai-http`); and resolve `TenantId` from the token/binding rather than the HTTP host.
- Fail-closed per-channel sender authentication **by construction**: only senders with a pre-provisioned binding are accepted. Unknown/unconfigured senders are rejected at the terminal (no token to present) and again at the Gateway (no matching user), with no auto-provisioning.
- Typing-indicator keep-alive so long-running turns keep the channel session alive; the terminal derives liveness from the Gateway's streaming (SSE) response.
- Attachment parsing for inbound channel messages (e.g., Signal attachment directives, Teams attachments).
- A **tenant scope for channels**: each channel/account binding is associated with a tenant, so an inbound message resolves its `TenantId` from that binding (carried in the provisioned claims), not from an HTTP host. Cross-tenant resolution must be impossible.
- **Inbound sender-to-user resolution**: map each channel-native sender identifier (e.g., a Signal phone number, a Teams AAD object id) to a specific persisted `UserEntity` that **already exists** (created first via the HTTP/OIDC surface). Reuse the issuer/subject model (`IdentityResolver.ResolveOrCreateUserAsync`, resolve-not-create for channels) by treating the channel as the issuer and the native identifier as the subject (e.g., `iss=signal`, `sub=+15551234`); the same sender resolves to the same user across turns. The sender→user/tenant binding and its credential are established during channel/user configuration.
- A **per-channel memory sharing/isolation policy model**: two directional allow-lists per channel — `Share` (which other channels may read this channel's memory) and `Access` (which other channels' memory this channel may read) — each supporting a wildcard (`*`) entry, with wildcard being the default for both directions (i.e., full cross-channel sharing by default). This includes the policy schema, per-channel persisted overrides, tenant-level defaults in configuration, and a resolution contract/service that yields both the effective readable-channel set and the mutually visible set (the boundary Phase 10 uses for 5W1H fact reconciliation), which Phase 10 consumes; it does not modify memory read/write behavior here.
- Configuration for channel enablement, Signal daemon endpoint/account, Teams Bot Framework settings, per-binding credential/JWT provisioning, sender bindings, and the memory sharing policy defaults under a channel configuration section consistent with the current config shape; startup validation.
- Tests for routing, fail-closed auth (pre-provisioned bindings), keep-alive signaling over SSE, attachment parsing, tenant/user resolution from claims, Gateway channel-claim trust/`ChannelId` resolution, and memory-policy resolution (wildcard default, explicit share/access lists, directional intersection).

## Out of Scope
- Channel adapters beyond Signal and Teams (e.g., Slack) in this phase.
- The turn pipeline itself (Phase 03 dependency for long-running/keep-alive behavior).
- Channel-first onboarding of brand-new users with no pre-existing user entity (sender must be pre-provisioned; a new sender is rejected until configured).
- Enforcing the memory sharing policy in the read/write path, and cross-channel 5W1H fact reconciliation (Phase 10 owns memory scoping, enforcement, and reconciliation).
- UI for channel management (later, if needed).

## Entry Criteria
- Identity partitioning with a channel concept exists (`IdentityResolver` channel resolution, `ChannelEntity`).
- Tenant/user resolution primitives exist and are reusable from claims: `IdentityResolver.ResolveTenantAsync`, `ResolveOrCreateUserAsync` (issuer/subject), and `TenantResolutionMiddleware` as the current HTTP resolution reference.
- Gateway HTTP endpoint(s) and bearer/JWT authentication exist to receive channel turns (`app.MapOpenAIResponses()`, `Identity.Token`/`OpenId` settings).
- `src/Terminals` exists as the home for client/edge terminal projects.
- Turn runtime is available to process channel-originated turns (Phase 03 recommended, not strictly required for basic echo).
- Source references captured as behavioral targets: `~/source/repos/leankernel/src/LeanKernel.Channels/ChannelHostedService.cs`, `ChannelRouter.cs`, `ChannelAuthenticator.cs`, `SignalChannel.cs`, `TypingIndicatorKeepAlive.cs`, `SignalAttachmentParser.cs`.

## Exit Criteria
Inbound Signal and Teams messages from pre-provisioned senders authenticate to the Gateway with provisioned claims, resolve the correct tenant (from the binding) and the correct existing persisted user (from the sender identifier), are processed by the Gateway's turn pipeline with the correct `ChannelId`, responses are delivered, long-running turns keep the session alive via the streaming response, unknown/unconfigured senders are rejected fail-closed, and the per-channel memory sharing/isolation policy resolves correctly (wildcard default = full sharing; explicit lists and directional AND intersection honored) and is available for Phase 10 to enforce. See `exit-criteria.md`.

## Roles
- Owner: Rebuild maintainer
- Reviewer: Separate agent session / model review
- Approver: Repository owner
