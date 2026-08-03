# Phase 2026-08-03 Activities

## Step-By-Step Activities

1. **Reproduce and baseline.** Against swarm (if available) or a local signal-cli fixture, exercise one and two accounts. Record receive duration, account rotation, inbound acceptance vs receipt ignores, gateway calls, typing requests, and `/v2/send` calls. Capture both defect A (deadline hang) and defect B (cross-account starvation).

2. **Confirm the transport contract.** Probe `/v1/receive/{account}` with curl and a WebSocket client against the same image/mode as production (`json-rpc-native` in swarm). Verify status codes, upgrade headers, response framing, timeout behavior, and concurrent-request rules. Document the result in evidence. Keep the receive implementation behind a transport abstraction; select HTTP long-polling and/or WebSocket based on the probe. Enforce **exactly one receive consumer per account**.

3. **Define lifecycle and ownership.** Promote the Signal transport to an explicit hosted lifecycle (`IHostedService` and/or `IAsyncDisposable`). Keep `ITransportClient` as the boundary and `TerminalService` as the sole sequential dispatcher via `ReceiveAsync`. Workers must not be double-started. Host shutdown must cancel every account loop and dispose sockets/HTTP calls.

4. **Implement independent account workers + bounded queue.**
   - One supervised cancellation/reconnect loop per discovered account.
   - Refresh accounts periodically (configurable interval).
   - Start/stop workers as accounts appear or disappear; stop stale workers fully before replacement.
   - Resolve bearer tokens on the accept path (fail-closed); only accepted messages enter the queue.
   - Bounded in-memory queue (default capacity 100). On full: **drop newest**, log `reason=queue_full`, continue receiving.
   - Document restart/drop semantics for in-flight dispatcher work and queued items that cannot be preserved.

5. **Implement client-side receive deadlines and bounded reconnect.** Apply a client deadline independent of the server timeout query param. Handle normal close, cancellation, DNS loss, and API errors with exponential backoff + jitter and a max delay. Reset backoff after a successful receive. Avoid tight failure loops.

6. **Preserve and harden parsing.** Keep support for `dataMessage` and `syncMessage.sentMessage`. Classify receipts, typing, and other non-message envelopes at debug/trace without false warning noise. Add safe redacted diagnostics for malformed payloads only.

7. **Harden processing and send contracts.**
   - Log accepted inbound messages with account + sender only.
   - Log gateway status and latency (no secret bodies).
   - Log typing-indicator outcomes at debug for non-success.
   - Change `ITransportClient.SendAsync` to return explicit success/failure; `TerminalService` logs failure and continues.
   - Gateway non-success that already returns reply text may still attempt Signal send; transport send failure must not be silent.

8. **Add tests** in `test/LeanKernel.Tests.Unit` (or a dedicated terminal test project if required):
   - Transport framing (HTTP and/or WebSocket per probe).
   - Client-side deadline cancellation vs normal long-poll completion.
   - Multi-account concurrency / non-starvation.
   - One-consumer-per-account and worker replace-without-overlap.
   - Queue-full drop-newest + log.
   - Restart/shutdown lifecycle (no orphan tasks).
   - Envelope parsing (data, sync-sent, receipt, malformed).
   - Gateway failure visibility and Signal send failure result propagation.
   - Typing start only after accept; stop after processing.

9. **Run quality gates.**
   - Targeted tests + full `dotnet test`.
   - Coverage ≥80% on modified files (repository gate).
   - Note: `scripts/quality/sonarqube-scan.sh` currently excludes `src/Terminals/LeanKernel.Channels.Signal/**`. Either adjust exclusions for this change set or treat unit/integration tests + deep review as the primary static-quality gates and record the Sonar exclusion in evidence.
   - Separate implementation review.

10. **Deploy when an environment is available.**
    - Production path (swarm repo):  
      `deploy/scripts/build.sh leankernel signal-channel` then  
      `./deploy/leankernel/scripts/deploy.sh` from `~/source/repos/swarm`.
    - Confirm the unique image tag is active on the Swarm service.
    - If no deployment environment is available, skip this step and record deferred ops verification in evidence.

11. **Ops verification (environment-dependent).** When swarm/lab with real or disposable accounts is available: send to each account while the other is idle; restart signal-cli; interrupt receive; confirm receive → typing → gateway → `/v2/send` → delivery. If unavailable, document deferral; do not block code-complete closure.

12. **Update operational documentation.** Record transport contract, lifecycle model, queue-full/drop policy, send-result contract, diagnostic fields, recovery procedure, and verification commands. Note relationship to Phase 06 open reconnect/retry gate (Signal half closed by this work).

## Review Focus
- Whether the selected receive protocol matches the deployed signal-cli image.
- Whether client-side deadlines fix hang defect A even for a single account.
- Whether account isolation prevents starvation defect B and duplicate consumers.
- Whether lifecycle ownership prevents orphan tasks and overlapping receive streams.
- Whether queue-full drop-newest semantics are implemented and tested.
- Whether `SendAsync` failure propagates to `TerminalService` without crashing the loop.
- Whether cancellation/reconnect behavior is bounded and shutdown-safe.
- Whether gateway and Signal delivery failures are observable without exposing secrets.
- Whether tests live in the chosen test project and cover restart, DNS outage, deadline, malformed envelopes, queue-full, and multiple accounts.
