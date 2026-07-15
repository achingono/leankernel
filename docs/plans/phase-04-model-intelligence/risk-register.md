# Phase 04 Risk Register

## Risks

| ID | Risk | Impact | Mitigation | Status |
| --- | --- | --- | --- | --- |
| R1 | Shadow routing leaks into primary response or doubles cost uncontrollably | Wrong output / cost spike | Strict out-of-band execution; sampling cap; no shared mutable state | Open |
| R2 | Quality-gate escalation loops indefinitely | Latency/cost blowup | Hard retry cap and terminal fallback | Open |
| R3 | Enhancement steps mutate content non-idempotently | Corrupted/duplicated citations | Idempotent steps; ordering tests | Open |
| R4 | Complexity scoring is non-deterministic | Flaky routing/tests | Deterministic heuristic inputs; snapshot tests | Open |
| R5 | Orchestration bypasses tool governance or partitioning | Security/isolation breach | Route worker tools through existing governance + isolation keys | Open |
| R6 | Degradation masks real provider outages silently | Hidden failures | Emit structured degradation signal for Phase 08 diagnostics | Open |

## Open Decisions
- Whether shadow comparisons are stored now or deferred until Phase 08 persistence exists.
- Escalation ladder definition (which alias escalates to which).
