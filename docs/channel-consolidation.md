# Channel Model

This document describes the current Phase 2 channel abstraction used by `LeanKernel.Channels`.

`LeanKernel.Abstractions.Interfaces.IChannel` is the canonical contract for:

- inbound lifecycle (`StartAsync`, `StopAsync`)
- connectivity state (`IsConnected`)
- direct outbound send (`SendAsync`)
- inbound message delivery via `MessageReceived`
- stable channel identity through `ChannelId`

`ChannelMessage` is the normalized inbound payload shared by all channel adapters. `ChannelRouter` converts it to `LeanKernelMessage`, enforces per-channel auth from configuration, calls `IAgentRuntime.RunTurnAsync`, and sends the response through the originating adapter.

## Registered adapters

| Adapter | Channel ID | Notes |
| --- | --- | --- |
| `SignalChannel` | `signal` | Disabled by default. Polls the Signal HTTP daemon at `/v1/receive/{account}` and sends replies through `/v2/send`. |

## Routing model

- `ChannelHostedService` subscribes to each registered adapter's `MessageReceived` event.
- `ChannelAuthenticator` fail-closes when no auth config exists for a channel.
- Authorized inbound messages reuse the same runtime path as the HTTP gateway.
- Channel-specific reasoning logic stays out of adapters and out of `LeanKernel.Gateway`.

## Current limitations

- Signal support currently uses polling rather than streaming/websockets.
- Sender authorization is allowlist-based in this slice (`AllowedSenders` + `RequireAuth`).
- Durable outbound queue behavior remains outside this Phase 2 implementation.

## Related documentation

- [Channels](features/channels.md)
- [Phase 2 Configuration](configuration/phase-2-config.md)
- [Solution Structure](architecture/solution-structure.md)
