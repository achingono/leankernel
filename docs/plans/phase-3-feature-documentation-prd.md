# Phase 3 Feature Documentation PRD

- **Status:** Reviewed
- **Audience:** LeanKernel maintainers, contributors, and operators who need implementation-accurate Phase 3 runtime explanations.
- **Document type:** Explanation (Diátaxis)
- **Phase goal:** Publish the full Phase 3 feature and configuration documentation set for reliability, optimization, orchestration, learning, scheduling, and production operations.
- **Plan review:** Reviewed by `gpt-5-mini`. Review outcome: proceed, pin configuration defaults to current source and appsettings values, keep each feature doc implementation-accurate and cross-linked, allow the configuration reference to be more detailed than the feature docs, and document validation limits because `dotnet` and Sonar are unavailable in this environment.

## Problem statement

Phase 3 implementation work added routed model selection, deterministic quality checks, shadow evaluation, coordinator-worker orchestration, synchronous response enhancement, asynchronous learning, scheduled proactive work, and operational hardening. The code exists, but the explanation-oriented feature documentation is still incomplete and the existing Phase 3 configuration reference is only partial. Contributors can read the code, but they do not yet have a coherent set of docs that explain how these features fit into the LeanKernel runtime, how they are configured, and what constraints shape their current behavior.

## Scope

This task will:

1. Create `docs/features/model-routing.md`.
2. Create `docs/features/quality-gates.md`.
3. Create `docs/features/shadow-routing.md`.
4. Create `docs/features/multi-agent.md`.
5. Create `docs/features/response-enhancement.md`.
6. Create `docs/features/learning-pipeline.md`.
7. Create `docs/features/scheduler.md`.
8. Create `docs/features/production-ops.md`.
9. Expand `docs/configuration/phase-3-config.md` into a full Phase 3 reference.
10. Update `docs/features/index.md` with the complete Phase 3 feature list.
11. Update directly related navigation such as `docs/plans/index.md` if needed so the reviewed PRD is discoverable.

## Out of scope

- Changing runtime behavior, configuration models, or defaults.
- Renaming or removing existing legacy Phase 3 placeholder docs unless directly needed for navigation.
- Rewriting unrelated documentation outside the requested feature, configuration, and directly related index files.
- Claiming roadmap behavior is implemented when the current code does not support it.
- Running `dotnet restore`, `dotnet build`, `dotnet test`, or Sonar in this environment.

## Documentation approach

### Diátaxis quadrant

Each feature file will be an **Explanation** document. The docs will clarify why the feature exists, how its collaborators fit together, where it sits in the runtime flow, what trade-offs shape the implementation, and how operators should think about configuration.

### Target audience and goal

- **Audience:** maintainers, contributors, and operators already familiar with LeanKernel concepts.
- **Goal:** understand the current implementation and its boundaries without reading every Phase 3 source file first.

### Style and structure

- Match the established feature-doc style in `docs/features/context-gating.md`, `turn-pipeline.md`, and `channels.md`.
- Use implementation-accurate names from the source.
- Prefer concise prose, Mermaid diagrams, Markdown tables, and short config examples.
- Keep feature docs roughly within the requested 80-150 line range where practical.
- Allow the configuration reference to be longer so all settings and nested keys are covered clearly.
- Cross-link related Phase 1 and Phase 2 docs with relative links.

## Source files

The new and updated docs must stay aligned to these implementation sources:

- `docs/CONTRIBUTING-DOCS.md`
- `docs/features/context-gating.md`
- `docs/features/turn-pipeline.md`
- `docs/features/channels.md`
- `docs/features/diagnostics.md`
- `docs/features/gateway-api.md`
- `docs/configuration/phase-1-config.md`
- `docs/configuration/phase-2-config.md`
- `src/LeanKernel.Abstractions/Configuration/LeanKernelConfig.cs`
- `src/LeanKernel.Abstractions/Configuration/RoutingConfig.cs`
- `src/LeanKernel.Abstractions/Configuration/OrchestrationConfig.cs`
- `src/LeanKernel.Abstractions/Configuration/EnhancementConfig.cs`
- `src/LeanKernel.Abstractions/Configuration/LearningConfig.cs`
- `src/LeanKernel.Abstractions/Configuration/SchedulerConfig.cs`
- `src/LeanKernel.Abstractions/Configuration/HardeningConfig.cs`
- `src/LeanKernel.Abstractions/Enums/QualityOutcome.cs`
- `src/LeanKernel.Abstractions/Models/RoutingDecision.cs`
- `src/LeanKernel.Abstractions/Models/QualityGateResult.cs`
- `src/LeanKernel.Abstractions/Models/ShadowRoutingResult.cs`
- `src/LeanKernel.Abstractions/Models/OrchestrationResult.cs`
- `src/LeanKernel.Abstractions/Models/EnhancementResult.cs`
- `src/LeanKernel.Abstractions/Models/TurnEvent.cs`
- `src/LeanKernel.Agents/TurnPipeline.cs`
- `src/LeanKernel.Agents/AgentsServiceCollectionExtensions.cs`
- `src/LeanKernel.Agents/Routing/TaskComplexityScorer.cs`
- `src/LeanKernel.Agents/Routing/PolicyModelSelector.cs`
- `src/LeanKernel.Agents/Routing/EscalationPolicy.cs`
- `src/LeanKernel.Agents/Routing/RoutedAgentStrategy.cs`
- `src/LeanKernel.Agents/Routing/ShadowRoutingStrategy.cs`
- `src/LeanKernel.Agents/Routing/ShadowComparer.cs`
- `src/LeanKernel.Agents/Quality/*.cs`
- `src/LeanKernel.Agents/Orchestration/*.cs`
- `src/LeanKernel.Agents/Enhancement/*.cs`
- `src/LeanKernel.Learning/*.cs`
- `src/LeanKernel.Scheduler/*.cs`
- `src/LeanKernel.Diagnostics/Health/*.cs`
- `src/LeanKernel.Diagnostics/SpendGuard/*.cs`
- `src/LeanKernel.Gateway/Middleware/*.cs`
- `src/LeanKernel.Gateway/Program.cs`
- `src/LeanKernel.Gateway/LeanKernelHardeningServiceCollectionExtensions.cs`
- `src/LeanKernel.Gateway/appsettings.json`
- `docker-compose.yml`

## Accuracy constraints

The docs must reflect these implementation details:

- Routing is disabled by default and only replaces `StaticAgentStrategy` when `LeanKernel:Routing:Enabled=true`.
- `TaskComplexityScorer` derives a deterministic `0.0-1.0` score from token estimates, tool count, history size, long-context signals, and multi-step markers.
- `PolicyModelSelector` maps score thresholds to `Economy`, `Standard`, and `Premium` tiers using configured model names.
- `ResponseQualityGate` runs four deterministic checks in order and records the first failure as the final `QualityOutcome`.
- Routed execution returns the best available response even when all quality attempts fail.
- `ShadowRoutingStrategy` runs the shadow invocation in parallel and never returns shadow output to the caller.
- Orchestration is coordinator-worker execution with scoped worker tools, bounded depth, bounded concurrency, and fallback to routed/static execution when orchestration is not used.
- Response enhancement is synchronous, timeout-bounded, and falls back to the original response on timeout or failure.
- Learning is fully asynchronous through `TurnEventQueue` and `LearningBackgroundWorker`, with drop-oldest queue behavior when capacity is reached.
- Scheduler jobs run through `IAgentRuntime` for `agent-prompt`, use Cronos for cron parsing, persist execution history, and limit concurrency.
- Production hardening includes provider health tracking, spend guard, rate limiting, graceful degradation, correlation IDs, opt-in OpenTelemetry export, and Docker health checks.

## Deliverables

### Feature docs

- `docs/features/model-routing.md` — routing, scoring, tier selection, escalation, diagnostics, and configuration.
- `docs/features/quality-gates.md` — deterministic checks, outcomes, escalation relationship, and non-blocking delivery semantics.
- `docs/features/shadow-routing.md` — decorator behavior, parallel execution, comparison metrics, and failure isolation.
- `docs/features/multi-agent.md` — coordinator-worker orchestration, worker scoping, tool adaptation, and fallback behavior.
- `docs/features/response-enhancement.md` — synchronous enhancement pipeline, step ordering, timeout fallback, and traceability.
- `docs/features/learning-pipeline.md` — asynchronous queueing, background execution, learning steps, and bounded behavior.
- `docs/features/scheduler.md` — cron scheduling, job types, runtime execution path, time boundaries, persistence, and concurrency.
- `docs/features/production-ops.md` — provider health, spend guard, rate limiting, graceful degradation, tracing, and container health.

### Configuration reference

- `docs/configuration/phase-3-config.md` — comprehensive reference for `LeanKernel:Routing`, `LeanKernel:Orchestration`, `LeanKernel:Enhancement`, `LeanKernel:Learning`, `LeanKernel:Scheduler`, and `LeanKernel:Hardening`, plus the opt-in OpenTelemetry keys used by `Program.cs`.

## Validation plan

1. Verify each document against the current source rather than roadmap-only PRDs.
2. Check Markdown structure, Mermaid blocks, tables, code fences, and relative links.
3. Ensure `docs/features/index.md` and any directly related index files point to the new docs.
4. Do not run `dotnet`-based restore/build/test/Sonar steps because the user explicitly stated `dotnet` is unavailable and build steps should be skipped.
5. Report that validation for this task is limited to source inspection and documentation consistency review.

## Acceptance criteria

- All requested Phase 3 feature docs exist at the specified paths and follow the Explanation quadrant.
- `docs/configuration/phase-3-config.md` covers every implemented Phase 3 setting and nested key requested by the user.
- `docs/features/index.md` links to the complete Phase 3 feature set.
- The reviewed PRD is saved under `docs/plans/` before the feature-documentation edits.
- The final report clearly states that `dotnet` and Sonar validation were skipped because the environment does not provide that toolchain for this task.
