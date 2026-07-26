# Phase 08 Activities

## Principle
Before building any item below, confirm LiteLLM does not already provide it. LiteLLM covers: spend tracking (`/spend`), budget enforcement, Prometheus metrics (`/metrics`), health endpoints (`/health/services`), rate limiting (proxy config), and audit logging (callbacks). Configure those — do not rebuild them.

## Step-By-Step Activities

### Configure LiteLLM Built-Ins
1. Configure LiteLLM proxy with spend/budget tracking, Prometheus metrics export, `/health/services` endpoint, and per-user/per-model rate limits in `proxy_config.yaml`.
2. Validate LiteLLM configuration at startup: verify `/health/services` is reachable, `/metrics` is exposed, and spend/budget settings are loaded.

### Build LeanKernel-Specific Gaps
3. Implement a diagnostics collector with a structured event model and an activity source for LeanKernel-specific signals (turn admission, budget allocation, history shaping, retrieval hits) — signals LiteLLM has no knowledge of.
4. Implement context diagnostics: capture per-turn snapshots (admission decisions, budget usage, history window, retrieval hits) and persist them to Postgres.
5. Add diagnostics persistence: diagnostic-entry entities, a Postgres sink, and EF migrations.
6. Expose a protected diagnostics query API (per-turn context snapshots only — spend/rate-limit data comes from LiteLLM's own API, not duplicated here).
7. Implement LeanKernel health aggregation: composite PostgreSQL, GBrain, and LiteLLM `/health/services` into a single `/health` endpoint with degradation signals for Phase 04. Probe LiteLLM health via its endpoint rather than building a separate health check.
8. Implement gateway hardening middleware: correlation-ID propagation and API-key/open-mode protection for API and diagnostics routes. Do not build custom rate-limiting middleware (LiteLLM handles it).
9. Add configuration (diagnostics retention, API protection mode, LiteLLM proxy address) and startup validation.
10. Add tests: snapshot persistence, diagnostics API auth/results, correlation propagation, and health aggregation.

### Intelligent Brain Delta Activities
11. Add lifecycle tracing for `ingest -> enrichment queue -> Dream run -> retrieval` with shared correlation ids.
12. Add LeanKernel-specific counters (memory freshness lag, contradiction rate, grounded-answer rate) exported alongside LiteLLM's existing metrics.
13. Expose diagnostics query endpoints for Dream and enrichment run statuses.
14. Document diagnostics, health aggregation, and production hardening in `docs/operations/` and `docs/features/`.

## Review Focus
- Diagnostics capture never blocks or meaningfully slows the turn path.
- Snapshot persistence respects partitioning and retention limits.
- LiteLLM is configured, not duplicated. Any new metric/handler/guard must justify why LiteLLM's equivalent is insufficient.
- API-key/open-mode protection cannot be trivially bypassed.
- Correlation IDs propagate end-to-end (request -> logs -> diagnostics).
- No broad exception swallowing; actionable context logged.
