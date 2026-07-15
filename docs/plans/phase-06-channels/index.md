# Phase 06 Channels

## Companion Documents
- [Inputs](inputs.md)
- [Activities](activities.md)
- [Outputs](outputs.md)
- [Exit Criteria](exit-criteria.md)
- [Risk Register](risk-register.md)
- [Evidence](evidence.md)

## Objective
Add a channel abstraction to the rebuild so the agent can serve inbound/outbound conversations over transports beyond the OpenAI-compatible HTTP surface, starting with a Signal adapter. This ports the source repo's `LeanKernel.Channels` behavior — a hosted channel service, channel router, fail-closed per-channel sender authentication, typing-indicator keep-alive, and attachment parsing — while reusing the rebuild's identity partitioning (tenant/user/channel) and turn runtime. As part of the channel abstraction, this phase also defines and persists a **per-channel memory sharing/isolation policy** — a configurable, directional model that decides which channels may read a channel's memory and which channels' memory it may read — that Phase 10 enforces in the memory scoping engine.

## Scope
This phase introduces the channel transport layer and one concrete adapter (Signal). It reuses the existing identity resolver's channel concept and the turn runtime to process messages. It also introduces the **channel memory sharing policy** as configuration and persisted state (schema, defaults, validation, storage, and the resolution contract consumed by Phase 10) but does not itself change how memory is read or written — that enforcement is Phase 10. It does not add new tools, model routing, learning, scheduler, diagnostics UI, or Blazor UI. Additional channel adapters beyond Signal are out of scope for this phase.

## In Scope
- A channel abstraction: hosted service, channel router, and a channel adapter contract for send/receive/reconnect.
- A Signal adapter that polls a Signal daemon, receives messages, sends responses, and reconnects on failure.
- Fail-closed per-channel sender authentication with configurable allowlists (unknown senders are rejected).
- Typing-indicator keep-alive so long-running turns keep the channel session alive.
- Attachment parsing for inbound channel messages (e.g., Signal attachment directives).
- Mapping inbound channel identity to the existing tenant/user/channel partitioning and routing turns through the turn runtime.
- A **per-channel memory sharing/isolation policy model**: two directional allow-lists per channel — `Share` (which other channels may read this channel's memory) and `Access` (which other channels' memory this channel may read) — each supporting a wildcard (`*`) entry, with wildcard being the default for both directions (i.e., full cross-channel sharing by default). This includes the policy schema, per-channel persisted overrides, tenant-level defaults in configuration, and a resolution contract/service that yields both the effective readable-channel set and the mutually visible set (the boundary Phase 10 uses for 5W1H fact reconciliation), which Phase 10 consumes; it does not modify memory read/write behavior here.
- Configuration for channel enablement, Signal endpoint/account, sender allowlists, and the memory sharing policy defaults under a channel configuration section consistent with the current config shape; startup validation.
- Tests for routing, fail-closed auth, keep-alive signaling, attachment parsing, identity mapping, and memory-policy resolution (wildcard default, explicit share/access lists, directional intersection).

## Out of Scope
- Non-Signal adapters (e.g., Slack, Teams) in this phase.
- The turn pipeline itself (Phase 03 dependency for long-running/keep-alive behavior).
- Enforcing the memory sharing policy in the read/write path, and cross-channel 5W1H fact reconciliation (Phase 10 owns memory scoping, enforcement, and reconciliation).
- UI for channel management (later, if needed).

## Entry Criteria
- Identity partitioning with a channel concept exists (`IdentityResolver` channel resolution, `ChannelEntity`).
- Turn runtime is available to process channel-originated turns (Phase 03 recommended, not strictly required for basic echo).
- Source references captured as behavioral targets: `~/source/repos/leankernel/src/LeanKernel.Channels/ChannelHostedService.cs`, `ChannelRouter.cs`, `ChannelAuthenticator.cs`, `SignalChannel.cs`, `TypingIndicatorKeepAlive.cs`, `SignalAttachmentParser.cs`.

## Exit Criteria
Inbound Signal messages from allowlisted senders are routed through the runtime with correct identity partitioning, responses are delivered, long-running turns keep the session alive, unknown senders are rejected fail-closed, and the per-channel memory sharing/isolation policy resolves correctly (wildcard default = full sharing; explicit lists and directional AND intersection honored) and is available for Phase 10 to enforce. See `exit-criteria.md`.

## Roles
- Owner: Rebuild maintainer
- Reviewer: Separate agent session / model review
- Approver: Repository owner
