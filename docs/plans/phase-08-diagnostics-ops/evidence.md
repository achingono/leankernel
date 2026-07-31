# Phase 08 Evidence

## LiteLLM Reference
LeanKernel consumes LiteLLM's built-in telemetry rather than rebuilding it:

| LiteLLM Feature | Endpoint / Config | LeanKernel Integration |
|---|---|---|
| Spend tracking | `/spend` admin API + LiteLLM DB | Startup validation; no custom `SpendTracker` |
| Health | `/health/readiness`, `/health/liveliness`, `/health/services` | Aggregated into LeanKernel `/health` |
| Prometheus metrics | `/metrics` | Scraped directly; LeanKernel adds only gap counters |
| Rate limiting | `proxy_config.yaml` per-user/per-model | Config validation only; no custom middleware |
| Audit logging | Postgres callback in `proxy_config.yaml` | Config validation only; no custom sink |

## Evidence Log

| Item | Reference | Notes |
| --- | --- | --- |
| LiteLLM proxy config reference | LiteLLM docs (`proxy_config.yaml`) | Spend, metrics, health, rate-limit configuration |
| LeanKernel context diagnostics | Per-turn snapshot service | LeanKernel-specific — LiteLLM has no equivalent |
| Diagnostics persistence | Entities + Postgres sink + migration | LeanKernel-specific context snapshots |
| Health aggregation | Composite endpoint PostgreSQL + GBrain + LiteLLM | Probes LiteLLM `/health/services` |
| Gateway hardening | Correlation-ID middleware + API-key/open-mode | No custom rate limiting |
| Lifecycle telemetry | Ingest/enrich/Dream/retrieval spans + gap counters | LeanKernel-specific correlation |
| Rebuild health/auth | `src/Services/LeanKernel.Services.Gateway/HealthChecks/*`, `Programs.cs` | Integration point |
| Dream orchestration diagnostics dependency | `docs/plans/phase-07-learning-scheduler/` | Lifecycle metrics source |
| Memory evaluation dependency | `docs/plans/phase-23-memory-eval-replay-harness/` | Baseline/threshold source for alert tuning |
