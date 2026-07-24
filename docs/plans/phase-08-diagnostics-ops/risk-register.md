# Phase 08 Risk Register

## Risks

| ID | Risk | Impact | Mitigation | Status |
| --- | --- | --- | --- | --- |
| R1 | Diagnostics capture slows the turn path | Latency regression | Async/buffered sink; sampling; off-path persistence | Open |
| R2 | Snapshot persistence leaks cross-partition context | Privacy breach | Partition-scoped writes + retention; isolation tests | Open |
| R3 | Spend guard false-blocks legitimate turns | Availability loss | Conservative thresholds + override + tests | Open |
| R4 | Rate limiting misconfiguration blocks real traffic or lets abuse through | Outage or abuse | Sensible defaults + config validation + tests | Open |
| R5 | API-key/open-mode misconfig exposes diagnostics | Data exposure | Fail-safe default; explicit open-mode opt-in | Open |
| R6 | OTel exporter overhead/cardinality explosion | Cost/perf | Bounded labels; opt-in exporters | Open |
| R7 | Lifecycle correlation gaps hide enrichment/Dream failures | Slow incident response | Standardize correlation IDs across ingestion, enrichment, and scheduler paths | Open |
| R8 | Memory-quality metrics are noisy without stable baselines | False alerts and alert fatigue | Pair metrics rollout with Phase 23 replay baselines and staged thresholds | Open |

## Open Decisions
- Diagnostics retention policy and storage growth strategy.
- Whether spend guard blocks or only warns by default.
- Which memory-quality metrics are hard alerts vs advisory dashboards.
