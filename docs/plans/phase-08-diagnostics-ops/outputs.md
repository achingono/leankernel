# Phase 08 Outputs

## Configured (Not Built)
These are provided by LiteLLM proxy configuration — LeanKernel configures and validates them at startup but does not implement them:

- Spend tracking and budget guardrails (LiteLLM `/spend` + `proxy_config.yaml`)
- Rate limiting (LiteLLM per-user/per-model limits)
- Prometheus metrics export (LiteLLM `/metrics`)
- Provider health endpoint (LiteLLM `/health/services`)

## Mandatory Outputs

| Output | Description | Format |
| --- | --- | --- |
| LiteLLM config validation | Startup validation that LiteLLM health/spend/metrics endpoints are reachable and configured | C# source |
| Diagnostics collector | Structured events + activity source for LeanKernel-specific signals | C# source |
| Context diagnostics | Per-turn snapshot capture (admission, budget, history, retrieval) + query API | C# source |
| Diagnostics persistence | Entities + Postgres sink + migration | C# + EF migration |
| Health aggregation | Composite `/health` endpoint (PostgreSQL + GBrain + LiteLLM `/health/services`) | C# source |
| Lifecycle telemetry | Correlated ingest/enrichment/Dream/retrieval spans + LeanKernel-specific counters | C# source |
| Gateway hardening | Correlation-ID propagation + API-key/open-mode protection (no custom rate limiting) | C# middleware |
| Configuration + validation | Retention, API protection mode, LiteLLM proxy address | C# + appsettings |
| Tests | Snapshot, API, health aggregation, correlation coverage | xUnit projects |
| Documentation | Operations + diagnostics docs + LiteLLM config reference | Markdown |

## Optional Outputs
- Run-replay/provenance foundation for future work.

## Output Quality Checklist
- [ ] All mandatory outputs produced
- [ ] No output duplicates LiteLLM built-in functionality
- [ ] All outputs reviewed before gate
- [ ] Evidence log updated with output references
