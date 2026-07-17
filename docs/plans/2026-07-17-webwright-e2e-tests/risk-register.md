# Phase 18A Risk Register

## Risks

| ID | Risk | Impact | Mitigation | Status |
| --- | --- | --- | --- | --- |
| R1 | MCP streamable response parsing differs between toolchain versions | Test instability | Support JSON and SSE response parsing in helpers | Open |
| R2 | Browser task timing exceeds local machine thresholds | False negatives | Use bounded polling window with clear timeout diagnostics | Open |
| R3 | External site dependency causes flaky navigation | Intermittent failures | Run task without external URL dependency by default | Open |
| R4 | Service health becomes ready before MCP session lifecycle is fully stable | Intermittent startup failures | Add preflight MCP initialize check and retry with bounded backoff | Open |
| R5 | Missing quality-gate artifacts causes incomplete delivery | Phase rejection | Capture coverage/Sonar/deep-review evidence in plan evidence log | Open |

## Open Decisions
- Whether to add this Webwright E2E command to CI in a later phase.
