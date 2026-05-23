# Phase 3 Production Hardening PRD

- **Status:** Reviewed
- **Audience:** LeanKernel maintainers implementing production hardening for provider resilience, spend controls, rate limiting, tracing, and operational health.
- **Document type:** Product requirements document
- **Phase goal:** Add independently configurable production-hardening features that improve reliability and observability without breaking the current LeanKernel runtime path.
- **Plan review:** Reviewed by `gpt-5.4-mini`. Review outcome: proceed after keeping probe implementations feature-local, treating spend/session fallbacks as node-local safeguards, preserving `/api/health` compatibility, and designing degraded behavior explicitly instead of silently masking persistence loss.

## Problem statement

LeanKernel now has routed execution, quality gates, orchestration, enhancement, learning, and scheduling, but it still lacks the runtime hardening needed for sustained production use. Provider outages, uncontrolled spend, bursty traffic, weak request correlation, and partial tracing can all turn valid feature behavior into an unreliable service experience.

## Scope

This task will:

1. Add a new `LeanKernel:Hardening` configuration block in abstractions and gateway appsettings.
2. Add provider health state tracking, ASP.NET health-check integration, and health-driven metrics.
3. Add spend tracking and spend guard decisions with clear allow/warn/block outcomes.
4. Add request correlation and gateway rate limiting.
5. Add graceful degradation policies for LiteLLM, GBrain, and database outages.
6. Add full OpenTelemetry wiring for inbound HTTP, outbound HTTP, diagnostics activities, metrics, and optional exporters.
7. Add Docker health checks and runtime documentation updates.
8. Add focused unit tests for the hardening behaviors.

## Out of scope

- Cross-node distributed spend ledgers or globally consistent quota enforcement.
- Billing reconciliation with provider invoices.
- UI or admin-console surfaces for hardening state.
- Replacing existing routing, diagnostics, or persistence behavior outside the new hardening paths.
- Running local `dotnet` or Sonar validation when the toolchain is unavailable in this environment.

## Primary files

- `src/LeanKernel.Abstractions/Configuration/HardeningConfig.cs`
- `src/LeanKernel.Abstractions/Configuration/LeanKernelConfig.cs`
- `src/LeanKernel.Abstractions/Interfaces/IProviderHealthTracker.cs`
- `src/LeanKernel.Abstractions/Models/*Hardening*.cs`
- `src/LeanKernel.Diagnostics/LeanKernelMetrics.cs`
- `src/LeanKernel.Diagnostics/Health/*`
- `src/LeanKernel.Diagnostics/SpendGuard/*`
- `src/LeanKernel.Agents/Resilience/*`
- `src/LeanKernel.Agents/TurnPipeline.cs`
- `src/LeanKernel.Knowledge/KnowledgeServiceCollectionExtensions.cs`
- `src/LeanKernel.Knowledge/*Resilient*.cs`
- `src/LeanKernel.Persistence/PersistenceServiceCollectionExtensions.cs`
- `src/LeanKernel.Persistence/*Tracing*.cs`
- `src/LeanKernel.Gateway/Middleware/*`
- `src/LeanKernel.Gateway/Program.cs`
- `src/LeanKernel.Gateway/Endpoints.cs`
- `src/LeanKernel.Gateway/LeanKernel.Gateway.csproj`
- `src/LeanKernel.Gateway/appsettings.json`
- `src/LeanKernel.Tests.Unit/Hardening/*`
- `README.md`
- `docs/configuration/phase-3-config.md`
- `docs/features/diagnostics.md`
- `docs/features/gateway-api.md`
- `docker-compose.yml`
- `Dockerfile`

## Functional requirements

### FR-1 Hardening configuration

- Add `HardeningConfig` with nested `SpendGuardConfig`, `RateLimitConfig`, `HealthTrackingConfig`, and `ResilienceConfig` exactly as requested.
- Add `Hardening` to `LeanKernelConfig` with default construction so config binding remains backward compatible.
- Keep all hardening features independently configurable.

### FR-2 Provider health tracking

- Add provider health contracts in abstractions so multiple modules can depend on them without new circular references.
- Implement a tracker that keeps provider state for `database`, `litellm`, and `gbrain`.
- Use consecutive success/failure thresholds from config to transition between healthy and unhealthy states.
- Expose snapshots for gateway health responses, graceful degradation, and metrics.
- Keep provider-specific probing logic feature-local wherever possible; avoid coupling `Diagnostics` directly to persistence internals beyond safe dependencies.

### FR-3 ASP.NET health checks

- Register ASP.NET Core health checks and implement a provider health check that reports per-provider status details.
- Preserve `/api/health` as the main JSON endpoint used by the Docker stack while sourcing data from the health-check framework and current tracker state.
- Report degraded/unhealthy status when providers cross configured thresholds.

### FR-4 Spend guard and spend tracking

- Add a `SpendTracker` with in-memory daily, monthly, and per-session totals.
- Add a `SpendGuardService` that estimates spend from model tier and token counts, then returns `Allow`, `Warn`, or `Block` decisions.
- Leave spend guard disabled by default.
- Persist spend snapshots through diagnostics entries when a sink is available, but document that enforcement is node-local in this implementation.
- Integrate spend checks into turn execution so blocked turns return a clear user-facing message instead of throwing.

### FR-5 Graceful degradation

- Add a `GracefulDegradationPolicy` that never throws and always returns a usable action plan.
- LiteLLM unhealthy: short-circuit with a clear user-facing error message.
- GBrain unhealthy: skip live retrieval and use cached last-known knowledge where available; otherwise continue without retrieval.
- Database unhealthy: continue in explicit degraded mode with fallback session/history behavior and a warning rather than silent durability assumptions.
- Resilience behavior must integrate with provider health state.

### FR-6 Gateway rate limiting and correlation

- Add a correlation-id middleware that reads `X-Correlation-Id` or creates a new one, stores it on the response, and enriches log scope/context.
- Add a rate-limiting middleware under `LeanKernel.Gateway/Middleware` that enforces per-identity sliding-window minute/hour limits plus concurrent-request caps.
- Use IP address or API key partitioning.
- Return HTTP `429 Too Many Requests` when limits are exceeded and emit rate-limit metrics.

### FR-7 OpenTelemetry wiring

- Add the requested OpenTelemetry hosting/instrumentation/exporter packages to the gateway project.
- Configure tracing and metrics in `Program.cs` for ASP.NET Core requests, outbound HTTP clients, LeanKernel activities, and LeanKernel metrics.
- Keep exporters opt-in: if no OTLP endpoint or console exporter is configured, the runtime should not add unnecessary exporter overhead.
- Add database command tracing through a local EF Core interceptor rather than forcing extra instrumentation packages.

### FR-8 Metrics

Add these metrics to `LeanKernelMetrics`:

- `leankernel.requests.total`
- `leankernel.requests.duration`
- `leankernel.requests.errors`
- `leankernel.spend.total_usd`
- `leankernel.providers.health`
- `leankernel.ratelimit.rejected`

The spend and provider-health metrics should use observable gauges backed by current in-memory state.

### FR-9 DI registration

- Add a single `AddLeanKernelHardening(this IServiceCollection services, HardeningConfig config)` extension to register hardening services.
- Register inner concrete services before wrapper/decorator services so DI resolution is deterministic.
- Keep gateway composition changes explicit in `Program.cs`.

### FR-10 Unit coverage

Add focused tests for:

- spend limit enforcement and warnings
- rate-limit sliding windows and concurrent limits
- provider health transition thresholds
- graceful degradation decisions
- updated metric methods where needed

## Design constraints

- Use file-scoped namespaces and nullable reference types.
- Keep shared contracts in `LeanKernel.Abstractions`; keep provider-specific behavior in the owning feature project when possible.
- Do not silently swallow production-hardening failures; log actionable warnings and degrade explicitly.
- Graceful degradation must never throw.
- Spend guard remains disabled by default.
- `/api/health` must remain usable for Docker health checks.

## Validation plan

1. Review touched files for namespace, DI, and dependency correctness.
2. Add or update unit tests for hardening flows.
3. Run only non-`dotnet` validation available in this environment (diff review, targeted static inspection).
4. Do not run `dotnet restore`, `dotnet build`, `dotnet test`, or Sonar locally because the user explicitly stated `dotnet` is unavailable.
5. Report that validation limitation in the final summary.

## Acceptance criteria

- `LeanKernel:Hardening` binds successfully into `LeanKernelConfig`.
- Provider health state is tracked for database, LiteLLM, and GBrain with configurable thresholds.
- `/api/health` reports integrated provider status and gateway liveness.
- Spend guard can allow, warn, and block, and blocked turns return a clear response.
- Gateway rate limiting returns `429` and tracks rejections.
- Correlation IDs flow through the request pipeline.
- OpenTelemetry tracing/metrics registration includes LeanKernel activities and metrics with opt-in exporters.
- Docker and container health checks target live runtime endpoints.
- Unit tests cover the requested hardening behaviors.
