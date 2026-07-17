# Phase 17 Risk Register

## Risks

| ID | Risk | Impact | Mitigation | Status |
| --- | --- | --- | --- | --- |
| R1 | External model backend unavailable in local docker stack | Retrieval/persistence assertions may fail | Gate execution behind explicit env var and return actionable failure details | Open |
| R2 | GBrain search indexing latency | False negatives immediately after writes | Add polling window with bounded timeout for persistence assertions | Open |
| R3 | Authenticated channel flow mismatch due claim or DB binding setup | Requests fail with 401 | Mirror middleware-required claims and DB binding fields exactly in helpers | Open |

## Open Decisions
- Keep tests in Playwright project (HTTP + DB + MCP) vs create dedicated e2e project.
