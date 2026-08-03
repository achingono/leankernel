# Phase 2026-08-03 Signal Channel Reliability Evidence

## Evidence Log

| Item | Reference | Notes |
| --- | --- | --- |
| Production baseline | Swarm logs for `leankernel_signal-channel`, `leankernel_signal-cli`, and `leankernel_gateway` captured on 2026-08-03 | Receive requests often lasted minutes or hours (defect A). Sequential multi-account receive allowed one stall to block the other account (defect B). Reply path was unreliable end-to-end. |
| Direct Signal send baseline | `signal-cli` log entry for `POST /v2/send` returning HTTP 201 | Confirms the Signal REST send endpoint works independently of the gateway/channel reply path. |
| Non-message envelopes | `signal-channel` warning/log: `no 'dataMessage' or 'syncMessage.sentMessage' in envelope` | Receipts and similar envelopes are received and correctly non-actionable; these are not themselves proof that user text messages never arrive. |
| Gateway involvement | Gateway logs around the incident window | At least some authenticated Signal `/v1/responses` traffic occurred (for example around `01:17:05`). Gateway is not wholly offline. Dominant failure is unreliable receive/dispatch/send completion, not “gateway never works.” |
| Auth/send ambiguity | Gateway `401` on `/v1/responses` observed in the window | A gateway non-success that returns reply text would still attempt Signal send today. Absence of `/v2/send` more strongly indicates the message was never accepted into processing, or the process died mid-turn. |
| Restart / DNS failure | `signal-channel` errors: `Name or service not known (signal-cli:8080)` after signal-cli task replacement | DNS loss is a recoverable deployment/restart condition that reconnect logic must cover. |
| Current source behavior (defect B) | `SocketTransportClient.ReceiveAsync` rotates accounts sequentially and awaits one receive before selecting the next | Cross-account starvation mechanism. |
| Current source behavior (defect A) | Receive WebSocket/HTTP wait has no independent client deadline beyond server query timeout | Single-account hang mechanism. |
| Current send behavior | `SocketTransportClient.SendAsync` returns `Task` and only logs non-success; `TerminalService` cannot observe failure | Delivery failures need explicit propagation. |
| Current lifecycle gap | `SocketTransportClient` is a singleton with no hosted start/stop; only `TerminalService` is a `BackgroundService` | Workers need explicit lifecycle ownership to avoid orphans/double-start. |
| Phase 06 open gates | `docs/plans/phase-06-channels/exit-criteria.md` | Reconnect/retry and terminal transport tests still open; this plan closes the Signal half. |
| Sonar exclusion | `scripts/quality/sonarqube-scan.sh` excludes `src/Terminals/LeanKernel.Channels.Signal/**` | Static analysis may not cover changed terminal code unless exclusion is adjusted; record chosen gate strategy. |
| No existing Signal unit tests | `test/` has no `*Signal*` terminal transport tests | New tests must be added under `test/LeanKernel.Tests.Unit` or a dedicated terminal test project. |
| Deploy path | Swarm repo `~/source/repos/swarm/deploy/leankernel` | Production build/deploy scripts live in swarm, not in the leankernel app repo. |
| Transport probe | To be added during implementation | Must record HTTP status, upgrade status, framing, timeout, concurrency rules, and restart behavior for `/v1/receive/{account}`. |
| Automated test report | To be added during implementation | Include targeted tests, full suite result, and modified-file coverage. |
| Quality-gate record | To be added during implementation | Sonar result and/or exclusion + deep-review outcome. |
| Deployment report or deferral | To be added during implementation | Unique image tag and service health, or explicit deferred ops verification. |
| Live end-to-end report or deferral | To be added during implementation | One successful flow per account and recovery after signal-cli restart/interruption, or deferral note. |
