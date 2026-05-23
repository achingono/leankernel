# Phase 3 Response Enhancement PRD

- **Status:** Reviewed
- **Audience:** LeanKernel maintainers implementing synchronous post-model response enhancement before delivery.
- **Document type:** Product requirements document
- **Phase goal:** Add a deterministic, timeout-bounded response enhancement pipeline that can enrich model output with retrieved knowledge hints, soften false refusals, and optionally inject citations without blocking delivery.
- **Plan review:** Reviewed by `gpt-5.4-mini`. Review outcome: proceed with a dedicated enhancement pipeline as the sole `IResponseEnhancer`, keep identity writeback on a separate best-effort hook, ensure partial enhancement outputs never leak after failure or timeout, and document that `dotnet`/Sonar validation is unavailable in this environment.

## Problem statement

LeanKernel already has an optional post-invocation `IResponseEnhancer` hook in `TurnPipeline`, but the current implementation shape does not support composable, traceable enhancement steps with bounded execution time. Phase 3 needs a deterministic enhancement layer that runs synchronously after model invocation and before persistence/delivery so the runtime can append source cues, intercept benign false refusals, and optionally inject inline citations while preserving fast fallback to the original response when enhancement is disabled, times out, or fails.

## Scope

This task will:

1. Add `EnhancementConfig` to `LeanKernel.Abstractions.Configuration` and wire it into `LeanKernelConfig`.
2. Add `EnhancementResult` and `EnhancementStepResult` models plus `IEnhancementStep`, `EnhancementStepInput`, and `EnhancementStepOutput` contracts.
3. Update `IResponseEnhancer` so the enhancement pipeline returns structured traceability instead of a bare string.
4. Implement `ResponseEnhancementPipeline` and deterministic enhancement steps in `src/LeanKernel.Agents/Enhancement/`.
5. Update `TurnPipeline` to build `EnhancementStepInput`, call the pipeline, persist the enhanced response, and emit response-enhancement diagnostics.
6. Preserve Phase 2 identity writeback by keeping `IdentityUpdateProjector` as a separate best-effort hook instead of an enhancement step.
7. Register enhancement services in DI so only config-enabled steps are active.
8. Add focused unit tests for pipeline ordering, timeout/failure fallback, and each concrete step.
9. Update appsettings and contributor-facing docs for the new `LeanKernel:Enhancement` block and synchronous post-processing flow.

## Out of scope

- Re-invoking a model during enhancement.
- Any side-effecting enhancement step.
- Making enhancement mandatory for response delivery.
- Semantic or LLM-based citation/refusal detection.
- Converting identity writeback into a response enhancement step.
- Running `dotnet restore`, `dotnet build`, `dotnet test`, or Sonar locally when the toolchain is unavailable.

## Primary files

- `src/LeanKernel.Abstractions/Configuration/EnhancementConfig.cs`
- `src/LeanKernel.Abstractions/Configuration/LeanKernelConfig.cs`
- `src/LeanKernel.Abstractions/Interfaces/IEnhancementStep.cs`
- `src/LeanKernel.Abstractions/Interfaces/IResponseEnhancer.cs`
- `src/LeanKernel.Abstractions/Models/EnhancementResult.cs`
- `src/LeanKernel.Agents/Enhancement/ResponseEnhancementPipeline.cs`
- `src/LeanKernel.Agents/Enhancement/KnowledgeSynthesisStep.cs`
- `src/LeanKernel.Agents/Enhancement/RefusalInterceptionStep.cs`
- `src/LeanKernel.Agents/Enhancement/CitationInjectionStep.cs`
- `src/LeanKernel.Agents/AgentsServiceCollectionExtensions.cs`
- `src/LeanKernel.Agents/TurnPipeline.cs`
- `src/LeanKernel.Diagnostics/DiagnosticsCollector.cs`
- `src/LeanKernel.Context/Identity/IdentityUpdateProjector.cs`
- `src/LeanKernel.Context/ContextServiceCollectionExtensions.cs`
- `src/LeanKernel.Gateway/Program.cs`
- `src/LeanKernel.Gateway/appsettings.json`
- `src/LeanKernel.Tests.Unit/Agents/Enhancement/*.cs`
- `src/LeanKernel.Tests.Unit/Agents/TurnPipelineTests.cs`
- `src/LeanKernel.Tests.Unit/Context/ContextBudgetTests.cs`
- `README.md`
- `docs/configuration/phase-3-config.md`
- `docs/features/turn-pipeline.md`

## Functional requirements

### FR-1 Enhancement contracts

- `EnhancementConfig` must expose the requested defaults for enabling the pipeline, individual steps, and maximum runtime.
- `EnhancementResult` must include the original response, final enhanced response, whether any modification was retained, per-step results, and total duration.
- `IEnhancementStep` must accept the response-in-progress, user message, optional session id, and retrieved knowledge.
- `IResponseEnhancer` must return `EnhancementResult` from `EnhanceAsync(EnhancementStepInput, CancellationToken)`.

### FR-2 Pipeline behavior

- `ResponseEnhancementPipeline` must run enabled steps in ascending `Order`.
- Each step must receive the previous step’s response text.
- The timeout budget is pipeline-wide, not per-step.
- If a step throws or the timeout elapses, the pipeline must return the original unenhanced response and must not leak partial modifications from earlier steps.
- Caller-initiated cancellation must still propagate.

### FR-3 Enhancement steps

- `KnowledgeSynthesisStep` (order `10`) must append a brief `Sources:` note only when retrieved knowledge clearly overlaps with the response and the note is not already present.
- `RefusalInterceptionStep` (order `20`) must detect configured refusal phrases and append a retry-oriented note only when the user request appears benign.
- `CitationInjectionStep` (order `30`) must inject `[source: page-key]` markers only when the response sentence clearly overlaps with a retrieval candidate and no citation is already present.
- All steps must be deterministic, idempotent, and side-effect-free.

### FR-4 Turn pipeline integration

- `TurnPipeline` must still allow best-effort identity writeback without relying on `IResponseEnhancer`.
- The enhancement pipeline input must include the raw response, user message, session id, and retrieved knowledge from the gated context.
- The persisted assistant turn, published `TurnEvent`, and returned string must all use `EnhancementResult.EnhancedResponse`.
- Response enhancement diagnostics must be recorded when a diagnostics collector is available.

### FR-5 Dependency injection

- `ResponseEnhancementPipeline` must be the only `IResponseEnhancer` registration.
- DI must register only the enhancement steps enabled by `LeanKernel:Enhancement`.
- Existing agent strategy registrations and runtime service lifetimes must remain intact.

### FR-6 Tests and documentation

- Add unit tests for ordered execution, no-op behavior, failure fallback, and timeout fallback.
- Add focused tests for knowledge-source appending, refusal interception, and inline citation injection.
- Update docs to describe the new configuration block and the synchronous enhancement stage in the turn pipeline.

## Design constraints

- Use file-scoped namespaces and nullable reference types.
- Keep enhancement logic in `LeanKernel.Agents`; only contracts/config/models belong in `LeanKernel.Abstractions`.
- Keep matching heuristics lightweight, deterministic, and easy to test.
- Do not reintroduce side effects into enhancement steps.
- Preserve delivery even when enhancement fails.

## Validation plan

1. Review touched files for contract consistency, DI wiring, and backward-compatible turn flow.
2. Add and inspect unit tests for the new enhancement pipeline and updated pipeline integration.
3. Do not run `dotnet restore`, `dotnet build`, `dotnet test`, or Sonar locally because the user explicitly stated the `dotnet` tool is unavailable in this environment.
4. Report the validation limitation clearly in the final summary.

## Acceptance criteria

- `LeanKernelConfig` exposes the requested `Enhancement` block.
- Structured enhancement result and step contracts exist in `LeanKernel.Abstractions`.
- `ResponseEnhancementPipeline` runs enabled steps in order and returns the original response on timeout/failure.
- `TurnPipeline` uses `EnhancedResponse` as the final response and records enhancement diagnostics.
- `ResponseEnhancementPipeline` is the sole `IResponseEnhancer` registration.
- Unit tests cover ordering, timeout/failure fallback, knowledge synthesis, refusal interception, and citation injection.
- Gateway appsettings and contributor docs describe the new enhancement configuration.
- The final report notes that `dotnet` and Sonar validation were skipped because the environment lacks the required tooling.
