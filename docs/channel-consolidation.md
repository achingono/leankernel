# Channel Consolidation

This reference explains the target channel architecture used during the Phase 2 refactor.

## Goal

LeanKernel should have one channel abstraction. `LeanKernel.Core.Interfaces.IChannel` is the canonical contract for inbound messages, outbound delivery, lifecycle management, sender authorization, and delivery diagnostics.

## Canonical contract

`IChannel` now covers both older channel shapes:

| Capability | Canonical member |
| --- | --- |
| Stable channel identifier | `ChannelId` |
| Display name for queue lookup and diagnostics | `Name` |
| Configuration readiness | `IsConfigured` |
| Inbound lifecycle | `StartAsync`, `StopAsync`, `OnMessageReceived` |
| Sender authorization | `IsAuthorizedSender` |
| Fire-and-forget response send | `SendAsync` |
| Delivery with status and retry metadata | `DeliverAsync` returning `ChannelDeliveryResult` |

`DeliverAsync` has a default implementation that calls `SendAsync` and returns success. Adapters that can report richer delivery state should override it.

## Migration path

1. Move Host-only channel adapters into `LeanKernel.Commander.Adapters`.
2. Make every adapter implement `IChannel` directly.
3. Replace `ChannelRegistry` lookups with `ChannelRouter` channel resolution.
4. Remove `Host.Services.Channels.IMessageChannel` after all queued delivery callers use `IChannel.DeliverAsync`.
5. Remove the temporary `LeanKernel:Channels:UseUnifiedStack` feature flag after one stable release.

## Non-goals

- This refactor does not change channel protocols.
- This refactor does not add new providers.
- This refactor does not change sender authorization rules.
