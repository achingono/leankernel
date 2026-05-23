# Phase 3 Quality Gates PRD

- **Status:** Reviewed
- **Audience:** LeanKernel maintainers implementing deterministic response quality gates for routed model execution.
- **Document type:** Product requirements document
- **Phase goal:** Add deterministic response quality evaluation that can trigger bounded model escalation before delivery while never blocking the final response.
- **Plan review:** Reviewed by `gpt-5-mini`. Review outcome: proceed with a dedicated abstractions-level quality context/result model, deterministic normalized matching for constraint coverage, structured diagnostics for final quality outcomes, and explicit acknowledgement that `dotnet`/Sonar validation is unavailable in this environment.

## Problem statement

Phase 3 routing can already score complexity, choose a model tier, and escalate between tiers, but response quality checks still live inline inside `RoutedAgentStrategy`. That makes the checks harder to test, harder to audit, and less reusable across strategies. LeanKernel needs deterministic quality gates that evaluate model output before delivery, record the outcome for diagnostics, and trigger capped escalation when responses are empty, too short, refusal-like, or fail simple constraint coverage.

## Scope

This task will:

1. Add abstractions-level quality models and an evaluation interface.
2. Add deterministic quality check implementations in `src/LeanKernel.Agents/Quality/`.
3. Extend routing configuration with configurable refusal patterns.
4. Refactor `RoutedAgentStrategy` to use the quality gate and escalation loop.
5. Surface final quality outcomes through strategy context and diagnostics.
6. Add unit tests for individual checks, the composite gate, routed escalation behavior, and diagnostics integration.
7. Update contributor-facing documentation/config references for the new routing quality settings.

## Out of scope

- Blocking delivery when all quality attempts fail.
- LLM-based quality judging or probabilistic scoring.
- Regex-driven or semantic refusal detection.
- Per-route quality policies beyond the shared routing config.
- Running `dotnet restore`, `dotnet build`, `dotnet test`, or Sonar locally when the toolchain is unavailable.

## Primary files

- `src/LeanKernel.Abstractions/Configuration/RoutingConfig.cs`
- `src/LeanKernel.Abstractions/Interfaces/IResponseQualityGate.cs`
- `src/LeanKernel.Abstractions/Models/QualityEvaluationContext.cs`
- `src/LeanKernel.Abstractions/Models/QualityGateResult.cs`
- `src/LeanKernel.Agents/AgentsServiceCollectionExtensions.cs`
- `src/LeanKernel.Agents/Quality/*.cs`
- `src/LeanKernel.Agents/Routing/RoutedAgentStrategy.cs`
- `src/LeanKernel.Agents/Strategies/AgentStrategyContext.cs`
- `src/LeanKernel.Agents/TurnPipeline.cs`
- `src/LeanKernel.Diagnostics/DiagnosticsCollector.cs`
- `src/LeanKernel.Gateway/appsettings.json`
- `src/LeanKernel.Tests.Unit/Agents/Quality/*.cs`
- `src/LeanKernel.Tests.Unit/Agents/Routing/RoutedAgentStrategyTests.cs`
- `src/LeanKernel.Tests.Unit/Agents/TurnPipelineTests.cs`
- `README.md`

## Functional requirements

### FR-1 Quality evaluation contracts

- Add `QualityGateResult` and `QualityCheckResult` records in `LeanKernel.Abstractions.Models`.
- Add `QualityEvaluationContext` in `LeanKernel.Abstractions.Models` so abstractions do not depend on agent strategy types.
- Add `IResponseQualityGate` in `LeanKernel.Abstractions.Interfaces` with a deterministic synchronous evaluation API over response text and quality context.

### FR-2 Deterministic checks

- Implement checks in deterministic order:
  1. Empty response check
  2. Minimum length check
  3. Refusal detection check
  4. Constraint coverage check
- Refusal detection must use simple case-insensitive string matching against configured patterns.
- Constraint coverage must use normalized word extraction from the user message, stable stop-word filtering, deduplication, and bounded coverage matching.

### FR-3 Routed strategy integration

- `RoutedAgentStrategy` must delegate quality evaluation to `IResponseQualityGate` after every model invocation.
- Failed quality results must trigger `EscalationPolicy.TryEscalate(...)` while attempts remain.
- The routed strategy must return the final response even when all quality attempts fail.
- The strategy must populate `ModelUsed`, `RoutingDecision`, `QualityOutcome`, and the final `QualityGateResult` on `AgentStrategyContext`.

### FR-4 Diagnostics and auditability

- Quality decisions must be logged without storing prompt or response content.
- `TurnPipeline` must persist final quality gate diagnostics when a collector is available.
- Quality diagnostics must include at least outcome and failure reason; routed per-attempt logs should also include model, tier, score, attempt, and check summaries.

### FR-5 Configuration and defaults

- `RoutingConfig` must expose configurable refusal patterns with deterministic defaults.
- `src/LeanKernel.Gateway/appsettings.json` must include those defaults under `LeanKernel:Routing`.
- `README.md` must mention that Phase 3 routing quality gates include configurable refusal patterns.

### FR-6 Unit coverage

- Add focused tests for each check class and the composite gate.
- Update routed strategy tests to cover pass, escalation-after-failure, and final-return-after-exhausted-escalation behavior.
- Update pipeline/diagnostics tests to verify quality diagnostics recording when a quality result is present.

## Design constraints

- Use file-scoped namespaces and nullable reference types.
- Keep quality logic feature-local to `LeanKernel.Agents`; only shared contracts live in `LeanKernel.Abstractions`.
- Keep checks deterministic and side-effect-free.
- Avoid logging raw prompts, history, or model responses.
- Prefer plain string/token matching over regex-heavy or model-based heuristics.

## Validation plan

1. Review touched files for contract consistency, namespace correctness, and DI wiring.
2. Add/inspect unit tests that express the deterministic gate behavior.
3. Do not run `dotnet` build/test/Sonar commands locally because the user explicitly stated the `dotnet` tool is unavailable in this environment.
4. Report the validation limitation in the final summary.

## Acceptance criteria

- Quality abstractions and gate result models exist in the requested namespaces.
- Routed execution uses the new quality gate instead of inline heuristics.
- Escalation occurs only when a quality gate fails and another tier is available within attempt limits.
- Final quality outcomes are recorded for diagnostics/audit without blocking delivery.
- Routing config and gateway appsettings expose refusal patterns.
- Unit tests cover individual checks, the composite gate, routed escalation flow, and diagnostics integration.
