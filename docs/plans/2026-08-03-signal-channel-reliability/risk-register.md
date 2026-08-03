# Phase 2026-08-03 Signal Channel Reliability Risk Register

## Risks

| ID | Risk | Impact | Mitigation | Status |
| --- | --- | --- | --- | --- |
| R1 | The deployed signal-cli image does not support the transport currently assumed by the client. | Messages are never received or receive connections remain stuck. | Confirm protocol and framing before refactoring; use a fixture matching the deployed image/mode. | Open |
| R2 | Concurrent account workers create duplicate receive consumers or duplicate message delivery. | Duplicate gateway turns or duplicate Signal replies. | Exactly one worker per account; stop-before-start replacement; no dual WebSocket+HTTP receive; lifecycle tests. | Open |
| R3 | A receive worker reconnects too aggressively during signal-cli or DNS outages. | CPU/log amplification and unnecessary service load. | Exponential backoff with jitter, bounded maximum delay, and success-based reset. | Open |
| R4 | A receive timeout cancels a valid long-running request incorrectly. | Lost inbound messages or excessive reconnects. | Derive client deadline from endpoint contract; test normal completion separately from cancellation. | Open |
| R5 | Gateway calls succeed but Signal sends fail silently. | User sees no response and diagnosis remains ambiguous. | Explicit `SendAsync` result; dispatcher logs status/latency; send-failure tests. | Open |
| R6 | Diagnostic logging leaks credentials or private message data. | Security and privacy incident. | Identifiers and status fields only; redact payloads; log-review checks. | Open |
| R7 | Signal account refresh races with worker shutdown or startup. | Missing account coverage or leaked tasks. | Serialize worker-set reconciliation; exclusive start; shutdown tests. | Open |
| R8 | Swarm task replacement temporarily removes the signal-cli DNS endpoint. | Transient channel failures during deployment or restart. | Treat DNS failures as recoverable; verify both services after convergence. | Open |
| R9 | Queue overflow or worker restarts drop messages without clear semantics. | Lost messages or confusing operational behavior. | Drop-newest on full; structured drop logs; document in-flight vs queued drop on restart; tests. | Open |
| R10 | Sonar excludes Signal terminal sources, so static analysis gates look green while terminal code is unanalyzed. | Quality regressions slip through. | Prefer unit coverage + deep review; optionally remove Sonar exclusion for this path; record choice in evidence. | Open |
| R11 | Deploy scripts are cited from the wrong repo and ops verification is treated as always required. | False failures or blocked closure. | Split code-complete vs ops-verified gates; use swarm paths only when deploying to swarm. | Open |

## Accepted decisions (closed for this phase)
- **Transport:** abstraction supporting the probed mode (HTTP long-poll and/or WebSocket); final mode chosen after probe, not up front.
- **Queueing:** bounded **in-memory** queue only; durable queueing is out of scope.
- **Queue full:** drop newest + structured log; default capacity 100 (configurable).
- **Dispatch:** single sequential `TerminalService` consumer; no concurrent multi-turn dispatch in this phase.
- **Lifecycle:** hosted transport ownership with one receive consumer per account.
- **Send contract:** `SendAsync` returns explicit success/failure; dispatcher logs and continues.

## Follow-up decisions (not blocking this phase)
- Production receive-latency, reconnect-count, and queue-drop alert thresholds.
- Whether to remove Sonar exclusions for `LeanKernel.Channels.Signal` permanently.
- Whether a later phase should add durable inbound queueing or concurrent dispatch.
