# Phase 08 Outputs

## Mandatory Outputs

| Output | Description | Format |
| --- | --- | --- |
| Diagnostics collector | Structured events + activity source | C# source |
| Context diagnostics | Per-turn snapshot capture + query API | C# source |
| Diagnostics persistence | Entities + Postgres sink + migration + DB tracing | C# + EF migration |
| Provider health tracking | PostgreSQL/LiteLLM/GBrain tracker feeding degradation | C# source |
| Spend guardrails | Spend tracker + warn/block guard | C# source |
| OpenTelemetry | Metrics, counters, log enricher | C# source |
| Intelligence lifecycle telemetry | Correlated ingest/enrichment/Dream/retrieval spans and metrics | C# source + OTel wiring |
| Gateway hardening | Correlation ID + rate limiting + API-key/open-mode | C# middleware |
| Configuration + validation | Retention/spend/rate-limit/auth settings | C# + appsettings |
| Tests | Snapshot, API, spend, rate-limit, correlation coverage | xUnit projects |
| Documentation | Operations + diagnostics docs | Markdown |

## Optional Outputs
- Run-replay/provenance foundation for future work.

## Output Quality Checklist
- [ ] All mandatory outputs produced
- [ ] All outputs reviewed before gate
- [ ] Evidence log updated with output references
