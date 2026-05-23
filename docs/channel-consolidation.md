# Channel Model

This document describes the current channel abstraction used by `LeanKernel.Commander`.

`LeanKernel.Core.Interfaces.IChannel` is the canonical contract for:

- Inbound lifecycle (`StartAsync`, `StopAsync`, `OnMessageReceived`)
- Sender authorization (`IsAuthorizedSender`)
- Direct send (`SendAsync`)
- Delivery with status (`DeliverAsync` -> `ChannelDeliveryResult`)
- Identity metadata (`ChannelId`, `Name`, `IsConfigured`)

`DeliverAsync` has a default behavior that calls `SendAsync`, and adapters can override it for richer retry/diagnostic behavior.

## Registered Adapters

Current Commander registrations:

| Adapter | Channel ID | Notes |
| --- | --- | --- |
| `SignalChannel` | `signal` | Supports signal-cli local mode or HTTP daemon mode based on `Signal.DaemonBaseUrl` |
| `DiscordChannelAdapter` | `discord` | Outbound adapter using Discord REST API |

## Routing and Delivery

- `ChannelRouter` subscribes to each configured channel’s inbound events.
- Inbound messages are normalized, passed to `IThinkerService`, then sent back through the originating channel.
- Direct response sends have a timeout (`35s` default).
- On direct-send failure, `ChannelRouter` can enqueue urgent retries into `IMessageQueue` when available.

## Durable Queue Integration

Commander uses:

- `MessageQueueService` (in-memory queue)
- `PersistentMessageQueueService` (SQLite-backed durability via `messagequeue.db`)
- `MessageProcessingBackgroundService` for queued delivery processing
