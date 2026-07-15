# Phase 08 Activities

## Step-By-Step Activities
1. Implement a diagnostics collector with a structured event model and an activity source for tracing across runtime stages.
2. Implement context diagnostics: capture per-turn snapshots (admission decisions, budget usage, history window, retrieval hits) and persist them.
3. Add diagnostics persistence: diagnostic-entry entities, a Postgres sink, and EF migrations; add a DB command activity interceptor for query tracing.
4. Expose a diagnostics query API (per-turn/context/health/spend) protected by the hardening auth below.
5. Implement provider health tracking (PostgreSQL, LiteLLM, GBrain) and wire it into health endpoints and the Phase 04 degradation policy.
6. Implement spend tracking and a spend-guard service producing warn/block decisions that can gate expensive turns.
7. Integrate OpenTelemetry: metrics, counters, activity export, and a log enricher for correlation.
8. Implement gateway hardening middleware: correlation-ID propagation, rate limiting, and API-key/open-mode protection for API/diagnostics routes.
9. Add configuration (retention, spend thresholds, rate limits, API protection mode) and startup validation.
10. Add tests: snapshot persistence, diagnostics API auth/results, spend gating, rate-limit enforcement, correlation propagation, and health tracking.
11. Document diagnostics, spend guardrails, and production hardening in `docs/operations/` and `docs/features/`.

## Review Focus
- Diagnostics capture never blocks or meaningfully slows the turn path.
- Snapshot persistence respects partitioning and retention limits.
- Spend guardrails enforce warn/block deterministically without false blocks.
- Rate limiting and API-key protection cannot be trivially bypassed.
- Correlation IDs propagate end-to-end (request -> logs -> diagnostics).
- No broad exception swallowing; actionable context logged.
