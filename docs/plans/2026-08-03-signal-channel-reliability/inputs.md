# Phase 2026-08-03 Inputs

## Required Inputs

| Input | Source | Owner |
|---|---|---|
| Current Signal channel implementation | `src/Terminals/LeanKernel.Channels.Signal` | Coding agent |
| Signal transport interface and dispatcher | `ITransportClient.cs`, `TerminalService.cs`, `Program.cs` | Coding agent |
| Production / swarm deployment configuration | `~/source/repos/swarm/deploy/leankernel/docker-stack.yml`, `scripts/deploy.sh`, `scripts/build` wrappers; local leankernel compose only if used as a lab fixture | Repository maintainer |
| Production log evidence | `gateway`, `signal-channel`, and `signal-cli` service logs from the 2026-08-03 incident | Operations owner |
| signal-cli REST API contract for the deployed image | Container image docs + controlled endpoint probe (`MODE=json-rpc-native` in swarm stack) | Coding agent |
| Existing test conventions | `test/LeanKernel.Tests.Unit`, Phase 06 plan open gates for reconnect/retry and terminal transport tests | Coding agent |
| Quality-gate constraints | `AGENTS.md` (≥80% coverage), `scripts/quality/sonarqube-scan.sh` (note: Signal terminal path is currently Sonar-excluded) | Coding agent |

## Optional Inputs
- Captured redacted Signal envelopes for text, receipts, typing events, sync messages, and attachments.
- A second configured Signal account for starvation and concurrency testing.
- A disposable signal-cli account for end-to-end tests.
- Local docker-compose signal-cli fixture when swarm is unavailable.

## Input Validation Checklist
- [ ] All required inputs are current (not from a superseded version)
- [ ] No required input is missing or in draft state
- [ ] The receive endpoint transport contract is confirmed before implementation choices are finalized
- [ ] Logs and captured payloads contain no secret tokens or message contents that should be committed
- [ ] Verification path is chosen up front: swarm ops-verify **or** lab-fixture code-complete with deferred ops gates
- [ ] Test project placement is decided (`test/LeanKernel.Tests.Unit` preferred)
