# Phase 08 Risk Register

## Risks

| ID | Risk | Impact | Mitigation | Status |
| --- | --- | --- | --- | --- |
| R1 | Diagnostics capture slows the turn path | Latency regression | Async/buffered sink; sampling; off-path persistence | Open |
| R2 | Snapshot persistence leaks cross-partition context | Privacy breach | Partition-scoped writes + retention; isolation tests | Open |
| R3 | LiteLLM version upgrade breaks spend/Prometheus/health contract | Broken telemetry | Pin LiteLLM version in deploy config; integration tests against known version | Open |
| R4 | LiteLLM rate-limit config is bypassed or misconfigured | Abuse or false blocks | Config validation at startup; regular reconciliation of proxy_config.yaml | Open |
| R5 | API-key/open-mode misconfig exposes diagnostics | Data exposure | Fail-safe default; explicit open-mode opt-in | Open |
| R6 | Lifecycle correlation gaps hide enrichment/Dream failures | Slow incident response | Standardize correlation IDs across ingestion, enrichment, and scheduler paths | Open |
| R7 | Memory-quality metrics are noisy without stable baselines | False alerts and alert fatigue | Pair metrics rollout with Phase 23 replay baselines and staged thresholds | Open |
| R8 | LeanKernel team duplicates LiteLLM functionality | Wasted effort, maintenance burden | Design review gate that asks "does LiteLLM already provide this?" before any new diagnostics feature | Open |

## Open Decisions
- Diagnostics retention policy and storage growth strategy.
- Which memory-quality metrics are hard alerts vs advisory dashboards.
- LiteLLM proxy version pinning policy.
