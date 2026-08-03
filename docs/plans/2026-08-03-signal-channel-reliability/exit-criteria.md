# Phase 2026-08-03 Exit Criteria

Gates are split into **code-complete** (always required) and **ops-verified** (required only when a deployment/lab environment with Signal accounts is available). Ops gates must not block code-complete closure when deferred; deferral must be recorded in evidence.

## Code-complete gate checklist
- [ ] Receive endpoint protocol and timeout behavior are confirmed against the target signal-cli image/mode (deployed and/or local fixture matching production).
- [ ] The implementation uses a transport abstraction that can support the selected protocol without hard-coding a single mode before the probe.
- [ ] Client-side receive deadlines are enforced independently of the server timeout query param (defect A fixed).
- [ ] Each configured Signal account has exactly one independently supervised receive worker (defect B fixed; no dual consumers).
- [ ] A stalled, disconnected, or unavailable account cannot prevent another account from receiving messages.
- [ ] Transport has explicit hosted lifecycle ownership; shutdown and account refresh cancel/dispose workers without leaks, orphans, or overlapping receive streams.
- [ ] Receive reconnect uses bounded exponential backoff with jitter and resets after success.
- [ ] Bounded in-memory queue uses configured capacity (default 100) and **drop-newest + structured log** on overflow.
- [ ] Restart and worker-replacement behavior is documented and tested, including explicit drop semantics for in-flight or queued messages that cannot be preserved.
- [ ] `TerminalService` remains the sole sequential dispatcher; typing starts only after accept and stops after processing.
- [ ] Valid `dataMessage` and `syncMessage.sentMessage` envelopes are accepted.
- [ ] Receipts, typing events, and other non-message envelopes are ignored without false warning noise.
- [ ] Bearer-token resolution stays on the accept path; unauthenticated senders are fail-closed and never queued.
- [ ] `SendAsync` returns an explicit success/failure result; non-success is logged by the dispatcher and does not crash the loop.
- [ ] Gateway non-success responses and Signal `/v2/send` failures are explicitly logged and testable.
- [ ] No logs or tests expose bearer tokens, message content, attachment bytes, or other secrets.
- [ ] Unit/integration tests exist under `test/LeanKernel.Tests.Unit` (or a dedicated terminal test project) covering protocol framing, deadlines, multiple accounts, starvation, queue-full, timeout, reconnect, restart, malformed envelopes, and delivery failures.
- [ ] Full `dotnet test` passes and modified files meet the repository coverage target (≥80%).
- [ ] Static quality is satisfied via Sonar (if Signal paths are included) and/or documented Sonar exclusion plus deep review with no unresolved Blocker/Critical/Major findings.
- [ ] Operational documentation describes diagnosis, recovery, queue-drop semantics, and verification commands.
- [ ] Only files belonging to this implementation are committed.

## Ops-verified gate checklist (environment-dependent)
- [ ] Environment available: swarm (`~/source/repos/swarm/deploy/leankernel`) or equivalent lab with Signal account(s).
- [ ] `signal-channel` built with the swarm/repo build automation using a unique image tag.
- [ ] Stack deployed with the swarm/repo deploy automation; active task uses the new image.
- [ ] Live verification proves receive → typing → gateway → `/v2/send` → Signal delivery for every configured account.
- [ ] Live verification passes after restarting signal-cli and after a receive endpoint interruption.
- [ ] If no environment is available: all ops gates above are marked **Deferred** in evidence with owner and follow-up condition (not counted as implementation success, and not blocking code-complete).

## Approval Table

| Role | Name | Status | Notes |
| --- | --- | --- | --- |
| Owner | | Pending | |
| Reviewer | | Pending | |
| Approver | | Pending | |
