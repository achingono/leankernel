# Phase 2026-08-03 Outputs

## Mandatory Outputs

| Output | Description | Format |
| --- | --- | --- |
| Transport decision record | Confirmed `/v1/receive` protocol, framing, timeout semantics, concurrency rules, and compatibility with the deployed image/mode | Markdown in implementation notes or evidence |
| Architecture decision record | Worker lifecycle ownership, one-consumer-per-account rule, bounded queue capacity + drop-newest policy, sequential dispatcher choice, and `SendAsync` result contract | Markdown in implementation notes or evidence |
| Receive-worker implementation | Hosted per-account receive/reconnect loops, client deadlines, and clean shutdown | C# source and configuration |
| Delivery observability | Structured logs for receive, accept, queue drop, gateway, typing, and send stages with secret-safe fields | C# source and log examples |
| Automated test coverage | Tests under `test/LeanKernel.Tests.Unit` (or dedicated terminal test project) for protocol, deadlines, concurrency, queue-full, lifecycle, parsing, and delivery failures | Test files and test report |
| Quality-gate record | Full `dotnet test`, coverage on modified files, and either Sonar results or documented Sonar exclusion for Signal terminals plus deep-review outcome | Evidence log |
| Deployment verification **or** deferral note | Unique image build + Swarm/task health + live probes when environment exists; otherwise explicit deferred ops verification | Evidence log and command output |
| Operational documentation | Diagnosis, recovery, queue-drop meaning, and verification commands | Markdown documentation |

## Optional Outputs
- A reusable fake signal-cli transport fixture for future channel integration tests.
- Metrics or dashboard panels for receive age, reconnect count, queue drops, gateway latency, and send failures.
- Sonar exclusion adjustment so Signal terminal code is analyzed going forward.

## Output Quality Checklist
- [ ] All mandatory outputs produced
- [ ] Code-complete vs ops-verified outputs clearly distinguished
- [ ] All outputs reviewed before gate
- [ ] Evidence log updated with output references
