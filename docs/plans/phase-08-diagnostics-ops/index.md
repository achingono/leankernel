# Phase 08 Diagnostics And Production Operations

## Companion Documents
- [Inputs](inputs.md)
- [Activities](activities.md)
- [Outputs](outputs.md)
- [Exit Criteria](exit-criteria.md)
- [Risk Register](risk-register.md)
- [Evidence](evidence.md)

## Objective
Give the rebuild production-grade observability and hardening: a structured diagnostics collector, per-turn context diagnostics with a query API, provider health tracking, spend tracking with guardrails, OpenTelemetry metrics/tracing, durable diagnostics persistence, and gateway hardening middleware (correlation IDs, rate limiting, API-key/open-mode protection). This ports the source repo's `LeanKernel.Diagnostics`, its persistence sinks, and the gateway middleware.

## Scope
This phase adds cross-cutting observability and request-hardening that the earlier runtime, model, tool, channel, and learning phases emit signals into. It does not build the Blazor diagnostics UI (Phase 09 consumes this API), nor does it change core turn/model behavior beyond adding instrumentation and guardrail hooks.

## In Scope
- A diagnostics collector emitting structured diagnostic events and tracing activities across the runtime.
- Context diagnostics: persisted per-turn context snapshots (admission, budget, history, retrieval) exposed via a diagnostics query API.
- Provider health tracking for PostgreSQL, LiteLLM, and GBrain, feeding the Phase 04 degradation policy and health endpoints.
- Spend tracking and guardrails: spend snapshots plus warn/block decisions that can gate expensive turns.
- OpenTelemetry integration: metrics, an activity source, runtime counters, and a log enricher.
- Diagnostics persistence: a Postgres diagnostics sink and diagnostic-entry entities with migrations.
- Gateway hardening middleware: correlation-ID propagation, rate limiting, and API-key/open-mode protection for API and diagnostics routes.
- Configuration for diagnostics retention, spend thresholds, rate limits, and API protection; startup validation.
- Tests for snapshot persistence, diagnostics API, spend gating, rate limiting, and correlation propagation.

## Out of Scope
- The Blazor diagnostics explorer UI (Phase 09).
- Emitting the signals themselves from earlier phases (those phases own their emit points); this phase provides the collection, persistence, API, and guardrail surfaces.

## Entry Criteria
- Runtime phases emit or can emit diagnostic signals (Phase 03 admission/budget, Phase 04 routing/shadow/degradation, Phase 05 tool/ingestion, Phase 07 learning/scheduler).
- EF persistence and health-check infrastructure exist (`EntityContext`, `HealthChecks/*`).
- Source references captured as behavioral targets: `~/source/repos/leankernel/src/LeanKernel.Diagnostics/{DiagnosticsCollector,ContextDiagnosticsService,LeanKernelMetrics,LeanKernelLogEnricher}.cs`, `Health/{ProviderHealthTracker,ProviderHealthCheck}.cs`, `SpendGuard/{SpendTracker,SpendGuardService}.cs`; `src/LeanKernel.Persistence/{PostgresDiagnosticsSink,Entities/DiagnosticEntryEntity}.cs`, `Tracing/DbCommandActivityInterceptor.cs`; `src/LeanKernel.Gateway/Middleware/{CorrelationIdMiddleware,RateLimitingMiddleware}.cs`, `Auth/ForwardedAuthHandler.cs`.

## Exit Criteria
The runtime emits structured diagnostics, persists per-turn context snapshots queryable via API, tracks provider health and spend with enforceable guardrails, exports OpenTelemetry metrics/traces, and protects/hardens API routes with correlation IDs, rate limiting, and API-key/open-mode auth. See `exit-criteria.md`.

## Roles
- Owner: Rebuild maintainer
- Reviewer: Separate agent session / model review
- Approver: Repository owner
