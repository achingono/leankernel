# Phase 2026-08-03 Signal Channel Reliability

## Companion Documents
- [Inputs](inputs.md)
- [Activities](activities.md)
- [Outputs](outputs.md)
- [Exit Criteria](exit-criteria.md)
- [Risk Register](risk-register.md)
- [Evidence](evidence.md)

## Objective
Make the Signal channel reliably receive messages for every configured account, invoke the gateway, and deliver typing indicators and replies. Production evidence shows two co-equal defects: (A) receive operations lack a client-side deadline and can hang for minutes or hours, and (B) sequential multi-account rotation lets one hung account starve every other account. This phase fixes both, confirms the deployed signal-cli transport contract first, and makes gateway/Signal delivery failures observable without leaking secrets.

## Relationship to Phase 06
This plan closes the Signal half of the open Phase 06 gates for transport reconnect/retry and terminal-specific transport tests. Teams transport reliability remains out of scope here.

## Implementation Approach

### Root causes (co-equal)
1. **No client-side receive deadline.** A single `/v1/receive` wait can hang far beyond the configured server `timeout`, blocking progress even in a single-account deployment.
2. **Sequential multi-account rotation.** `SocketTransportClient.ReceiveAsync` awaits one account receive before selecting the next, so one hung account starves the other.

### Accepted architecture (closed for this phase)
- **Transport abstraction:** keep receive behind an abstraction that can support HTTP long-polling and/or WebSocket once the deployed contract is confirmed. Do not hard-code a single mode before the probe.
- **Lifecycle ownership:** promote the Signal transport to an explicit hosted lifecycle (`IHostedService` and/or `IAsyncDisposable`). Workers start with the host and stop on shutdown. Do not rely on lazy start inside `ReceiveAsync` without exclusive initialization.
- **One supervised worker per account:** exactly one active receive consumer per Signal number. No dual WebSocket + HTTP receive. Stop the prior worker fully before starting a replacement.
- **Bounded in-memory queue:** accepted messages (token resolved, fail-closed) enter a bounded channel. Durable queueing is out of scope for this phase.
- **Queue-full policy:** capacity is configurable (default 100). On full, **drop newest**, log a structured drop event (`account`, `sender`, `reason=queue_full`), and continue receiving. Do not block the receive worker indefinitely.
- **Single sequential dispatcher:** keep `TerminalService` as the sole consumer of `ITransportClient.ReceiveAsync`. One in-flight gateway turn at a time is intentional; no concurrent turns per sender/account in this phase. Typing keep-alive remains tied to the in-flight turn.
- **Send/result contract:** change `SendAsync` to return an explicit success/failure result (for example `Task<bool>` or a small result type). `TerminalService` logs non-success and continues the loop; it does not crash. Gateway non-success that already yields reply text may still attempt Signal send; true transport send failures must be visible.
- **Token resolution:** remain on the accept path before enqueue. Unauthenticated senders are rejected fail-closed and never queued.
- **Observability:** log only account, sender, status codes, latency, and drop reasons. Never log bearer tokens, message text, attachments, or secret-bearing bodies.

### Config knobs to add or surface
- Client receive deadline (independent of server `timeout` query param)
- Reconnect base delay, max delay, and jitter
- Bounded queue capacity
- Account refresh interval (replace the hardcoded 30s refresh)

## Scope

## In Scope
- Validate whether `/v1/receive/{account}` is HTTP long-polling, WebSocket, or version-dependent, using the deployed signal-cli image and a local test fixture.
- Fix client-side receive deadlines (defect A) and sequential multi-account starvation (defect B).
- Implement independently supervised receive workers (one per account) with hosted lifecycle ownership.
- Add bounded in-memory queue with explicit drop-newest + log semantics.
- Add receive deadlines, reconnect backoff, cancellation, and service-restart recovery.
- Preserve valid `dataMessage` and `syncMessage.sentMessage` parsing.
- Distinguish receipts, typing events, sync events, and malformed envelopes in logs.
- Make gateway and Signal send failures visible and testable via an explicit send result contract.
- Add unit/integration tests under `test/LeanKernel.Tests.Unit` (or a dedicated terminal test project if unit placement is impractical), plus optional deployed smoke tests.
- Close the Signal reconnect/retry and Signal transport-test gaps left open in Phase 06.

## Out of Scope
- Changing Signal account registration or linked-device provisioning.
- Changing gateway authentication, sender-binding data, or model behavior.
- Replacing signal-cli-rest-api with another Signal implementation.
- Redesigning the shared channel abstraction across terminals.
- Durable/out-of-process inbound queueing.
- Concurrent multi-turn dispatch inside `TerminalService`.
- Teams channel reliability (separate work).
- Production alerting threshold finalization (recorded as a follow-up decision).

## Entry Criteria
- Current production evidence is captured: long-lived `/v1/receive` requests, non-message envelope rejects, intermittent gateway Signal turns, and unreliable reply delivery.
- `signal-channel` source is available at `src/Terminals/LeanKernel.Channels.Signal`.
- A verification path exists: either (a) the swarm deployment environment (`~/source/repos/swarm/deploy/leankernel`) or (b) a local lab fixture that can exercise receive/send contracts without production phones.
- The signal-cli image version and API behavior can be reproduced in a controlled environment.
- Plan reviewed by a separate model/session before implementation.

## Exit Criteria
All **code-complete** checks in [exit-criteria.md](exit-criteria.md) are complete. **Ops-verified** checks are required only when a deployment environment is available; otherwise they are deferred and documented.

## Roles
- Owner: Coding agent
- Reviewer: Separate model/session reviewer
- Approver: Repository maintainer
