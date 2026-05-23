# Phase 3 Shadow Routing PRD

- **Status:** Reviewed
- **Audience:** LeanKernel maintainers and contributors implementing Phase 3 shadow routing.
- **Document type:** Product requirements document
- **Phase goal:** Invoke a secondary model in parallel for comparison while preserving the primary response path as the sole user-facing result.
- **Plan review:** Reviewed by `gpt-5.4`. Review outcome: proceed after tightening decorator DI wiring, making diagnostics best-effort, treating cancellation separately from shadow failures, and avoiding unsupported assumptions about primary token usage.

## Problem statement

LeanKernel Phase 3 already supports deterministic model routing and escalation, but the existing implementation cannot compare alternate-model behavior on live traffic without changing what the user sees. Phase 3 shadow routing must let operators run a configured shadow model alongside the authoritative strategy, persist structured comparison data, and keep shadow execution completely non-authoritative so rollout risk stays low.

## Scope

This task will:

1. Add structured shadow-routing result models in `src/LeanKernel.Abstractions/Models/`.
2. Add a shadow diagnostics category for persisted comparison records.
3. Implement a reusable `ShadowComparer` in `src/LeanKernel.Agents/Routing/`.
4. Implement `ShadowRoutingStrategy` as an `IAgentStrategy` decorator over the resolved primary strategy.
5. Update agent DI registration so shadow routing wraps the active strategy only when `LeanKernel:Routing:ShadowRoutingEnabled=true` and `ShadowModel` is configured.
6. Persist best-effort shadow comparison diagnostics through `IDiagnosticsSink` without affecting the primary response path.
7. Add unit tests for comparison logic, parallel execution behavior, error isolation, and decorator registration behavior.
8. Update contributor-facing docs so shadow routing is no longer described as config-only.

## Out of scope

- Changing the authoritative routing/escalation semantics of `RoutedAgentStrategy`.
- Returning shadow output to the caller.
- Adding new storage schema beyond existing diagnostics persistence.
- Introducing provider-specific token accounting dependencies when metadata is unavailable.
- Running `dotnet` restore/build/test or Sonar locally when the toolchain is unavailable in this environment.

## Source files

Primary implementation and documentation targets:

- `src/LeanKernel.Abstractions/Enums/DiagnosticCategory.cs`
- `src/LeanKernel.Abstractions/Models/ShadowRoutingResult.cs`
- `src/LeanKernel.Agents/AgentsServiceCollectionExtensions.cs`
- `src/LeanKernel.Agents/Routing/ShadowComparer.cs`
- `src/LeanKernel.Agents/Routing/ShadowRoutingStrategy.cs`
- `src/LeanKernel.Tests.Unit/Agents/Routing/ShadowComparerTests.cs`
- `src/LeanKernel.Tests.Unit/Agents/Routing/ShadowRoutingStrategyTests.cs`
- `README.md`
- `docs/features/intelligent-model-routing.md`
- `docs/configuration/phase-1-config.md`
- `docs/plans/index.md`

## Functional requirements

### FR-1 Shadow result models

- Add `ShadowRoutingResult` with primary/shadow model names, responses, latencies, token counts, and optional comparison metadata.
- Add `ShadowComparison` with length ratio, non-empty flags, refusal flags, and optional notes.
- Use file-scoped namespaces, nullable reference types, and XML documentation on new public types and properties.

### FR-2 Shadow comparison

- `ShadowComparer` must compute deterministic comparison output from primary and shadow response text only.
- Refusal detection must be heuristic and case-insensitive.
- Notes should highlight materially different outcomes such as significant length divergence or mismatched refusal behavior.

### FR-3 Decorator behavior

- `ShadowRoutingStrategy` must implement `IAgentStrategy` and wrap an inner authoritative strategy.
- The primary invocation must execute through `_inner.InvokeAsync(context, ct)`.
- The shadow invocation must run in parallel using `AgentFactory.GetChatClientForModel(shadowModel)` and the same message/options shape built from the incoming strategy context.
- The decorator must await both operations before logging the final comparison record.
- The method must always return the exact primary response when the primary path succeeds.

### FR-4 Failure isolation and cancellation

- Shadow invocation failures must be caught, logged, and converted into diagnostic notes; they must never be thrown to the caller.
- Diagnostic persistence failures must also be caught and logged so shadow logging stays best-effort.
- `OperationCanceledException` from the shared cancellation token must still flow to the caller rather than being misreported as a shadow failure.

### FR-5 Diagnostics

- Persist a `DiagnosticEntry` with category `Shadow` when a diagnostics sink is available.
- Do not log prompt, history, or tool payload content.
- Record only model names, response text, latency, token counts, and derived comparison metadata needed for later offline analysis.

### FR-6 Dependency injection

- DI must continue to resolve `StaticAgentStrategy` when routing is disabled and `RoutedAgentStrategy` when routing is enabled.
- Shadow routing must wrap whichever strategy is already authoritative instead of duplicating routing logic.
- Registration must avoid circular self-resolution by constructing the inner strategy first and decorating it afterward.

### FR-7 Unit tests

- Add comparer tests for empty strings, refusal detection, and significant length differences.
- Add strategy tests proving primary and shadow start in parallel, shadow failures are non-blocking, diagnostics failures are isolated, and the primary response remains authoritative.
- Add DI tests proving the decorator is only applied when the shadow feature is configured.

## Design constraints

- Keep routing behavior inside `LeanKernel.Agents`; composition-only changes belong in service registration.
- Preserve existing contracts unless a change is required for this slice.
- Prefer small deterministic helpers over expanding orchestration classes.
- Use best-effort token extraction; when token metadata is unavailable, persist `0`.
- Do not assume the decorator can recover authoritative token usage from arbitrary inner strategies beyond what their public contract exposes.

## Validation plan

1. Review affected source files for compile-time consistency and DI correctness.
2. Add targeted unit tests for comparison and decorator behavior.
3. Do not run `dotnet restore`, `dotnet build`, `dotnet test`, or Sonar scripts locally because the user explicitly stated `dotnet` is unavailable in this environment.
4. Report the validation limitation clearly in the final summary.

## Acceptance criteria

- Shadow-routing result models exist with the requested fields.
- `DiagnosticCategory` includes `Shadow` and diagnostics entries can use that category.
- `ShadowComparer` produces deterministic comparison output with refusal and note heuristics.
- `ShadowRoutingStrategy` runs primary and shadow work in parallel, returns only the primary response, and isolates shadow/diagnostic failures.
- DI applies the decorator only when shadow routing is configured.
- Unit tests cover comparison logic, error isolation, and decorator behavior.
- README and docs no longer describe shadow routing as config-only.
- The final report states that `dotnet` and Sonar validation were skipped because the environment lacks the required tooling.
