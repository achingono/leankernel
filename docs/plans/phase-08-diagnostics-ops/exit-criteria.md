# Phase 08 Exit Criteria

## Gate Checklist

### LiteLLM Configuration
- [ ] LiteLLM proxy is configured with spend/budget tracking and `/spend` endpoint is accessible.
- [ ] LiteLLM proxy is configured with per-user/per-model rate limits in `proxy_config.yaml`; LeanKernel does not build custom rate-limiting middleware.
- [ ] LiteLLM `/health/services` endpoint is reachable and aggregated into LeanKernel's `/health` surface.
- [ ] LiteLLM `/metrics` endpoint is exposed and Prometheus-compatible.
- [ ] LiteLLM configuration is validated at startup; missing or misconfigured LiteLLM proxy causes a hard failure.

### LeanKernel-Specific Diagnostics
- [ ] LeanKernel-specific structured diagnostic events are emitted across runtime stages (turn admission, budget, history, retrieval).
- [ ] Per-turn context snapshots are persisted and queryable via a protected diagnostics API.
- [ ] Diagnostics persistence has a valid EF migration and respects retention/partitioning.
- [ ] LeanKernel `/health` aggregates PostgreSQL, GBrain, and LiteLLM `/health/services` into a single endpoint with degradation signals for Phase 04.

### Lifecycle & Quality
- [ ] Ingest/enrichment/Dream/retrieval lifecycle is traceable end-to-end with shared correlation IDs.
- [ ] Memory-quality telemetry (freshness lag, contradiction rate, grounded-answer rate) is queryable.

### Hardening
- [ ] Correlation IDs propagate end-to-end (request → logs → diagnostics).
- [ ] API-key/open-mode protection is enforced on API and diagnostics routes (no custom rate-limiting — LiteLLM owns that).

### Testing
- [ ] Unit + integration tests cover snapshot persistence, diagnostics API, health aggregation, and correlation propagation.

## LiteLLM Coverage Assertion
- [ ] No custom spend tracker, rate limiter, LiteLLM health probe, or Prometheus metric exporter duplicates functionality LiteLLM already provides.

## Approval Table

| Role | Name | Status | Notes |
| --- | --- | --- | --- |
| Owner | | Pending | |
| Reviewer | | Pending | |
| Approver | | Pending | |
