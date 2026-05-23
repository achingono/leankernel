# Channel Routing and Signal Integration

`LeanKernel.Channels` is the Phase 2 transport package for inbound message sources other than the HTTP gateway. It keeps channel-specific delivery concerns outside the core runtime while ensuring every inbound message still flows through `IAgentRuntime`.

## Implemented pieces

| Component | Role |
| --- | --- |
| `IChannel` | Shared abstraction for channel lifecycle, connectivity, outbound send, and inbound message events. |
| `ChannelRouter` | Authenticates inbound channel messages, normalizes them into `LeanKernelMessage`, invokes `IAgentRuntime.RunTurnAsync`, and sends the response back through the originating adapter. |
| `ChannelAuthenticator` | Applies per-channel sender allowlists from configuration. Missing channel auth config fails closed. |
| `SignalChannel` | Polling adapter for the Signal HTTP daemon (`/v1/receive/{account}` and `/v2/send`). |
| `ChannelHostedService` | Starts/stops enabled channels, subscribes to inbound messages, and routes them through `IChannelRouter`. |

## Routing flow

1. A channel adapter raises `MessageReceived` with a normalized `ChannelMessage`.
2. `ChannelHostedService` forwards the message to `IChannelRouter`.
3. `ChannelRouter` authenticates the sender using `LeanKernel:Channels:ChannelAuth`.
4. Authorized traffic is converted into `LeanKernelMessage` and passed to `IAgentRuntime.RunTurnAsync`.
5. The resulting response is sent back through the originating `IChannel` adapter.

This preserves the same runtime/session path used by `POST /api/chat`; channel adapters do not implement their own reasoning logic.

## Signal specifics

Signal support is disabled by default and requires explicit configuration:

- `LeanKernel:Channels:Signal:Enabled = true`
- `LeanKernel:Channels:Signal:PhoneNumber = "+1555..."`
- a reachable Signal daemon URL (`http://signal:8080` in Compose, `http://localhost:8080` for local development)

The adapter uses HTTP polling instead of websockets to keep the first Phase 2 transport simple and restart-friendly.

## Authentication model

Each channel can define sender rules under `LeanKernel:Channels:ChannelAuth`:

- `RequireAuth = false` allows all senders for that channel.
- `RequireAuth = true` requires the sender id to appear in `AllowedSenders`.
- Missing channel config is treated as a denial so auth cannot be bypassed by accident.

## Related documentation

- [Phase 2 Configuration](../configuration/phase-2-config.md)
- [Solution Structure](../architecture/solution-structure.md)
- [Phase 2 Channel Expansion PRD](../plans/phase-2-channel-expansion-prd.md)
