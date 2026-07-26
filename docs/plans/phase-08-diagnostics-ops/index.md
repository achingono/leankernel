# Phase 08 Diagnostics And Production Operations

## Companion Documents
- [Inputs](inputs.md)
- [Activities](activities.md)
- [Outputs](outputs.md)
- [Exit Criteria](exit-criteria.md)
- [Risk Register](risk-register.md)
- [Evidence](evidence.md)

## Objective
Give the rebuild production-grade observability and hardening by **leveraging LiteLLM's built-in telemetry** and filling the remaining gaps: per-turn context diagnostics with a query API, gateway hardening middleware (correlation IDs, API-key/open-mode protection), lifecycle tracing across ingestion/enrichment/Dream/retrieval, and memory-quality metrics. Do **not** reinvent spend tracking, health probes, Prometheus metrics, or rate limiting that LiteLLM already provides.

## Guiding Principle: LiteLLM First
Before building any diagnostics feature, check whether LiteLLM already covers it:

| LiteLLM Built-In | LeanKernel Action |
|---|---|
| Spend/cost tracking per model/user (`/spend`, budget enforcement, DB logging) | **Consume** via LiteLLM proxy admin API; do not build custom `SpendTracker` |
| Health endpoints (`/health/readiness`, `/health/liveliness`, `/health/services`) | **Proxy** LiteLLM health into LeanKernel's `/health` aggregate; do not build custom provider probes |
| Prometheus metrics (latency, tokens, spend, model failures at `/metrics`) | **Scrape** directly; add LeanKernel-specific counters only for gaps |
| Rate limiting (per-user/per-model in `proxy_config.yaml`) | **Extend** LiteLLM config; do not build custom rate-limiting middleware |
| Audit logging via callbacks (Postgres, custom endpoints) | **Configure** LiteLLM callback for audit trail; do not build custom sink |

LeanKernel owns what LiteLLM cannot: LeanKernel-specific turn context snapshots, correlation ID propagation, auth hardening, lifecycle correlation across LeanKernel services, and memory-quality metrics.

## Scope
This phase adds cross-cutting observability and request-hardening that the earlier runtime, model, tool, channel, and learning phases emit signals into. It does not build the Blazor diagnostics UI (Phase 09 consumes this API), nor does it change core turn/model behavior beyond adding instrumentation and guardrail hooks.

## In Scope
- A diagnostics collector emitting structured diagnostic events and tracing activities across the runtime (LeanKernel-specific signals not covered by LiteLLM).
- Context diagnostics: persisted per-turn context snapshots (admission, budget, history, retrieval) exposed via a protected diagnostics query API.
- LeanKernel health aggregation endpoint: composites PostgreSQL, GBrain, and LiteLLM health into a single `/health` surface with degradation signals for Phase 04.
- Diagnostics persistence: Postgres sink for LeanKernel-specific context-snapshot entries with EF migrations.
- Gateway hardening middleware: correlation-ID propagation and API-key/open-mode protection for API and diagnostics routes.
- Configuration for diagnostics retention, API protection mode, and LiteLLM proxy address; startup validation.
- Tests for snapshot persistence, diagnostics API, correlation propagation, and health aggregation.

### What LiteLLM Provides (Configure, Don't Build)
- **Spend tracking and budget guardrails**: configure LiteLLM proxy with budget limits, consume from LiteLLM's `/spend` endpoints and DB. Do not build a custom `SpendTracker`.
- **Rate limiting**: configure per-user/per-model limits in LiteLLM `proxy_config.yaml`. Do not build custom rate-limiting middleware.
- **OpenTelemetry / Prometheus metrics**: scrape LiteLLM's `/metrics` endpoint. Add LeanKernel-specific counters only for LeanKernel-owned signals (turn context, lifecycle spans, memory quality).
- **Provider health checking**: aggregate LiteLLM's `/health/services` into LeanKernel's health endpoint. Do not build custom LiteLLM health probes.

## Out of Scope
- The Blazor diagnostics explorer UI (Phase 09).
- Emitting the signals themselves from earlier phases (those phases own their emit points); this phase provides the collection, persistence, API, and guardrail surfaces.
- Custom spend tracker, custom rate-limiting middleware, custom LiteLLM health probes, or custom Prometheus metric exporters for signals LiteLLM already covers.

## Entry Criteria
- Runtime phases emit or can emit diagnostic signals (Phase 03 admission/budget, Phase 04 routing/shadow/degradation, Phase 05 tool/ingestion, Phase 07 learning/scheduler).
- EF persistence and health-check infrastructure exist (`EntityContext`, `HealthChecks/*`).
- LiteLLM proxy is deployed with Prometheus metrics enabled, `/health/services` endpoint accessible, and spend/budget configuration loaded.
- LiteLLM proxy address is available in configuration.

## Exit Criteria
Per-turn context snapshots are persisted and queryable via a protected API; health aggregates across PostgreSQL, GBrain, and LiteLLM; LiteLLM spend/rate-limit/Prometheus configuration is validated at startup; correlation IDs propagate end-to-end; API routes are protected by auth hardening; ingestion/enrichment/Dream/retrieval lifecycle is traceable with shared correlation IDs; memory-quality metrics are exported. See `exit-criteria.md`.

## Design Delta: Intelligent Brain Track
- Add ingest-to-enrichment-to-retrieval lifecycle spans with shared correlation ids across queue jobs and Dream runs.
- Add memory quality metrics: freshness lag, contradiction rate, grounded-answer rate, and recall-at-k evaluation feeds (consumed from LiteLLM metrics + LeanKernel context snapshots).
- Add diagnostics query surfaces for enrichment outcomes and Dream phase-level errors to support replay and remediation.

## Roles
- Owner: Rebuild maintainer
- Reviewer: Separate agent session / model review
- Approver: Repository owner
