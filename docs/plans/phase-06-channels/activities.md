# Phase 06 Activities

## Step-By-Step Activities
1. Define the channel abstraction: an adapter contract (receive loop, send, reconnect) and a `ChannelRouter` that maps inbound messages to identity + turn invocation.
2. Implement a `ChannelHostedService` that hosts enabled channel adapters as background services with lifecycle management.
3. Implement fail-closed `ChannelAuthenticator`: per-channel sender allowlists where unknown senders are rejected and never processed.
4. Implement the Signal adapter: poll the Signal daemon, parse inbound messages and attachments, dispatch through the router, and send responses; reconnect with backoff on failure.
5. Implement typing-indicator keep-alive so long-running turns periodically signal activity on the channel.
6. Map inbound channel sender/account to the existing tenant/user/channel partitioning via the identity resolver, creating channel-scoped identities as needed.
7. Define the per-channel memory sharing/isolation policy: a `Share` allow-list (channels permitted to read this channel's memory) and an `Access` allow-list (channels whose memory this channel may read), each accepting explicit channel names or the wildcard `*`, with `*` as the default for both directions. Persist per-channel overrides, load tenant-level defaults from configuration, and expose a resolution service/contract that yields, for a given channel, both (a) the effective set of channels whose memory it may read (directional AND: channel X's memory is readable in channel C iff X shares to C **and** C accesses X) and (b) the *mutually visible* set (channels visible to each other) that Phase 10 uses as the 5W1H fact-reconciliation boundary. Signal policy changes so Phase 10 can reconcile facts when sharing is widened. Enforcement and reconciliation are deferred to Phase 10.
8. Add configuration (channel enable flags, Signal endpoint/account, sender allowlists, memory sharing policy defaults) consistent with the current config shape; validate at startup, including rejecting policy entries that reference unknown channels and normalizing wildcard usage.
9. Add tests: routing dispatch, fail-closed auth, keep-alive timing/signaling, attachment parsing, identity mapping, reconnect behavior, and memory-policy resolution (wildcard default, explicit lists, directional AND intersection, isolation via empty lists).
10. Document the channel abstraction, Signal adapter, and the memory sharing/isolation policy model in `docs/features/`.

## Review Focus
- Auth is genuinely fail-closed (default reject) with no bypass path.
- Inbound identity mapping preserves tenant/user/channel isolation.
- Reconnect logic does not busy-loop or drop messages silently.
- Keep-alive does not interfere with turn output ordering.
- Attachment parsing handles malformed input safely.
- Memory sharing policy defaults to wildcard (full sharing), supports explicit isolation, and resolves with correct directional AND semantics.
