# Phase 06 Activities

## Step-By-Step Activities
1. Define the channel abstraction: an adapter contract (receive loop, send, reconnect) and a `ChannelRouter` that maps inbound messages to identity + turn invocation.
2. Implement a `ChannelHostedService` that hosts enabled channel adapters as background services with lifecycle management.
3. Implement fail-closed `ChannelAuthenticator`: per-channel sender allowlists where unknown senders are rejected and never processed.
4. Implement the Signal adapter: poll the Signal daemon, parse inbound messages and attachments, dispatch through the router, and send responses; reconnect with backoff on failure.
5. Implement typing-indicator keep-alive so long-running turns periodically signal activity on the channel.
6. Map inbound channel sender/account to the existing tenant/user/channel partitioning via the identity resolver, creating channel-scoped identities as needed.
7. Add configuration (channel enable flags, Signal endpoint/account, sender allowlists) consistent with the current config shape; validate at startup.
8. Add tests: routing dispatch, fail-closed auth, keep-alive timing/signaling, attachment parsing, identity mapping, and reconnect behavior.
9. Document the channel abstraction and Signal adapter in `docs/features/`.

## Review Focus
- Auth is genuinely fail-closed (default reject) with no bypass path.
- Inbound identity mapping preserves tenant/user/channel isolation.
- Reconnect logic does not busy-loop or drop messages silently.
- Keep-alive does not interfere with turn output ordering.
- Attachment parsing handles malformed input safely.
