# Phase 08 Exit Criteria

## Gate Checklist
- [ ] Structured diagnostic events and tracing activities are emitted across runtime stages.
- [ ] Per-turn context snapshots are persisted and queryable via a protected diagnostics API.
- [ ] Provider health (PostgreSQL/LiteLLM/GBrain) is tracked and feeds health endpoints + degradation.
- [ ] Spend tracking produces warn/block decisions that gate expensive turns deterministically.
- [ ] OpenTelemetry metrics/traces and correlation-enriched logs are exported.
- [ ] Ingest/enrichment/Dream/retrieval lifecycle is traceable end-to-end with shared correlation IDs.
- [ ] Memory-quality telemetry (freshness lag, contradiction rate, grounded-answer rate) is queryable and alertable.
- [ ] Correlation IDs propagate end-to-end; rate limiting and API-key/open-mode protection are enforced.
- [ ] Diagnostics persistence has a valid EF migration and respects retention/partitioning.
- [ ] Unit + integration tests cover snapshots, API, spend, rate limiting, and correlation.

## Approval Table

| Role | Name | Status | Notes |
| --- | --- | --- | --- |
| Owner | | Pending | |
| Reviewer | | Pending | |
| Approver | | Pending | |
