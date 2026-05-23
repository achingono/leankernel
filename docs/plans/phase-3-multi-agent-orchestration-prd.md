# Phase 3 Multi-Agent Orchestration PRD

- **Status:** Reviewed
- **Audience:** LeanKernel maintainers and contributors implementing Phase 3 orchestration behavior.
- **Document type:** Product requirements document
- **Phase goal:** Add coordinator-worker orchestration with scoped worker tools, agent-as-tool delegation, and structured orchestration traceability while preserving the existing single-agent path when orchestration is disabled.
- **Plan review:** Reviewed by `claude-sonnet-4.6`. Review outcome: proceed after treating `ToolDefinition` to `AITool` adaptation as an explicit prerequisite, avoiding singleton-to-scoped worker DI capture by constructing workers via a factory pattern, and explicitly handling coordinator `ChatOptions.Tools` population instead of assuming `AgentInvocationBuilder` will do it.

## Problem statement

LeanKernel currently supports single-model execution through `StaticAgentStrategy` and conditional model selection through `RoutedAgentStrategy`, but it does not yet support coordinator-worker delegation for complex requests. Phase 3 requires the platform to expose specialized workers as callable tools, constrain worker context and tool access, and capture which worker outputs contributed to the final result, all while keeping orchestration disabled by default for safe rollout.

## Scope

This task will:

1. Add `OrchestrationConfig` and `WorkerDefinition` to `LeanKernel.Abstractions.Configuration` and surface them through `LeanKernelConfig`.
2. Add `OrchestrationResult` and `WorkerContribution` models to `LeanKernel.Abstractions.Models`.
3. Extend strategy and event contracts so orchestration runs can return plain text responses while preserving structured traceability for diagnostics and downstream consumers.
4. Add orchestration diagnostics support for structured contribution persistence when diagnostics are enabled.
5. Implement orchestration collaborators in `src/LeanKernel.Agents/Orchestration/`:
   - `OrchestrationDecision`
   - `OrchestrationDecider`
   - `ToolDefinitionAIToolAdapter`
   - `WorkerAgent`
   - `WorkerAsToolAdapter`
   - `OrchestratedAgentStrategy`
6. Reuse `AgentFactory.GetChatClientForModel` for worker-specific models.
7. Enforce worker tool scoping through `ToolVisibilityContext` and `ToolGovernancePolicy`.
8. Enforce orchestration depth, concurrency, and timeout limits from configuration.
9. Update dependency injection so orchestration has highest strategy priority when enabled, otherwise routing or static execution continues unchanged.
10. Add unit tests for orchestration decisions, worker scoping, worker-as-tool adaptation, strategy coordination flow, and DI selection.
11. Update `src/LeanKernel.Gateway/appsettings.json` with the disabled-by-default orchestration section and example workers.
12. Record the validation limitation caused by the unavailable `dotnet` and Sonar tooling in this environment.

## Out of scope

- Enabling orchestration by default.
- Persisting worker definitions in Postgres or building a worker-definition store.
- Arbitrary recursive sub-workflows beyond the configured coordinator-worker depth guard.
- Adding new external tools, transports, or model providers.
- Full MAF graph workflow persistence beyond structured run tracing for the current turn.
- Running restore/build/test/Sonar locally when `dotnet` is unavailable in this environment.

## Source files

Primary implementation and validation targets:

- `docs/plans/phase-3-multi-agent-orchestration-prd.md`
- `docs/plans/index.md`
- `src/LeanKernel.Abstractions/Configuration/LeanKernelConfig.cs`
- `src/LeanKernel.Abstractions/Configuration/OrchestrationConfig.cs`
- `src/LeanKernel.Abstractions/Enums/DiagnosticCategory.cs`
- `src/LeanKernel.Abstractions/Models/OrchestrationResult.cs`
- `src/LeanKernel.Abstractions/Models/TurnEvent.cs`
- `src/LeanKernel.Agents/AgentsServiceCollectionExtensions.cs`
- `src/LeanKernel.Agents/TurnPipeline.cs`
- `src/LeanKernel.Agents/Strategies/AgentStrategyContext.cs`
- `src/LeanKernel.Agents/Orchestration/*`
- `src/LeanKernel.Diagnostics/DiagnosticsCollector.cs`
- `src/LeanKernel.Gateway/appsettings.json`
- `src/LeanKernel.Tests.Unit/Agents/AgentRuntimeTests.cs`
- `src/LeanKernel.Tests.Unit/Agents/Orchestration/*`
- `src/LeanKernel.Tests.Unit/Diagnostics/DiagnosticsCollectorTests.cs`

## Functional requirements

### FR-1 Configuration

- `OrchestrationConfig` must expose the requested defaults:
  - `Enabled = false`
  - `MaxWorkerConcurrency = 3`
  - `MaxOrchestrationDepth = 2`
  - `WorkerTimeout = 00:01:00`
  - `Workers = []`
- Each `WorkerDefinition` must expose name, description, model, system prompt, allowed tools, allowed categories, and optional scope.
- Orchestration remains disabled unless `Enabled` is explicitly set to `true`.

### FR-2 Structured orchestration traceability

- `OrchestrationResult` must capture coordinator response, worker contributions, total duration, and total worker invocations.
- Each `WorkerContribution` must capture worker name, task, response, duration, success, and optional error.
- `AgentStrategyContext` must be able to carry an optional orchestration result without changing the `IAgentStrategy` return type.
- `TurnEvent` and diagnostics output should surface orchestration results when present without breaking non-orchestrated turns.

### FR-3 Orchestration decision heuristics

- `OrchestrationDecider` must return a structured decision with `ShouldOrchestrate` and a human-readable reason.
- Heuristics must include:
  - multi-step indicators such as ordered steps and phrases like `first`, `then`, `next`, `finally`
  - explicit delegation or coordination requests
  - complexity score thresholds derived from current turn context
- When orchestration is unavailable or rejected, the system must fall back safely to the existing routed/static flow.

### FR-4 Tool adaptation prerequisites

- `ToolDefinition` instances must be adaptable into `AITool` instances for worker execution.
- The adapter must map parameter metadata from `ToolParameter` into callable function arguments compatible with `Microsoft.Extensions.AI` agent tooling.
- Tool adaptation must be implemented as an explicit orchestration prerequisite rather than hidden inside worker execution.
- Worker-exposed tools must never automatically include coordinator worker-tools, preventing uncontrolled recursion.

### FR-5 Worker execution

- `WorkerAgent` must execute a single delegated task with:
  - its configured model
  - its configured system prompt
  - scoped tool visibility derived from `AllowedTools`, `AllowedCategories`, and optional `Scope`
  - a per-worker timeout enforced with linked cancellation
  - no inherited coordinator history by default
- Worker execution must carry parent `SessionId` and `TurnId` for trace correlation.
- Worker execution must fail safely with structured contribution records when timeouts or exceptions occur.
- Orchestration depth must be bounded by configuration to prevent accidental recursion.

### FR-6 Worker-as-tool delegation

- `WorkerAsToolAdapter` must expose each worker as an `AITool`/`AIFunction` callable by the coordinator.
- The coordinator must pass a task description string to the worker tool.
- Each invocation must append a `WorkerContribution` to a concurrency-safe collection for the current orchestration run.
- Tool results returned to the coordinator should be useful plain text, even when the worker fails.

### FR-7 Coordinator strategy behavior

- `OrchestratedAgentStrategy` must implement `IAgentStrategy` with `Name = "orchestrated"`.
- Flow:
  1. Decide whether orchestration should run.
  2. Fall back to routed/static execution when orchestration should not run.
  3. Build coordinator worker tools explicitly into `ChatOptions.Tools`.
  4. Invoke the coordinator model.
  5. Collect worker contributions and total duration.
  6. Populate `context.OrchestrationResult`, `context.ModelUsed`, and return the coordinator response text.
- Coordinator execution must respect `MaxWorkerConcurrency`.
- Empty worker configuration must degrade gracefully rather than throw.

### FR-8 Dependency injection and compatibility

- `AddLeanKernelAgents` must continue registering `AgentFactory`, `ITurnPipeline`, and `IAgentRuntime`.
- `OrchestrationDecider` and orchestration collaborators must be registered with lifetimes that avoid captive dependencies.
- Strategy selection priority must be:
  1. `OrchestratedAgentStrategy` when orchestration is enabled
  2. `RoutedAgentStrategy` when routing is enabled
  3. `StaticAgentStrategy` otherwise

### FR-9 Unit tests

- Add tests for orchestration heuristics and explicit delegation triggers.
- Add tests for worker tool/category scoping, timeout behavior, and depth enforcement.
- Add tests for worker-as-tool contribution capture and tool-result text.
- Add tests for orchestrated strategy fallback, successful worker delegation, empty-worker graceful behavior, and DI strategy selection.
- Add tests for orchestration diagnostics persistence.

## Design constraints

- Follow file-scoped namespaces, nullable reference types, XML documentation for new public surface area, and existing dependency-injection patterns in this repository.
- Keep orchestration logic in `LeanKernel.Agents`; composition-only changes belong in service registration and configuration.
- Reuse existing `ToolVisibilityContext`, `ToolGovernancePolicy`, and `AgentFactory` seams instead of introducing duplicate policy layers.
- Preserve the current routed/static behavior for disabled orchestration.
- Do not log raw prompts, system messages, or history content in orchestration diagnostics.

## Diagnostics and safety requirements

- Structured orchestration diagnostics must capture worker count, elapsed duration, success/failure per worker, and contribution details.
- Worker contributions must be recorded in a concurrency-safe collection.
- Worker failures and timeouts must not crash the coordinator path; they should surface as failed contributions and coordinator-readable tool output.
- Cancellation must propagate from the parent turn into worker execution.

## Validation plan

1. Review affected files for compile-time consistency and backward-compatible contract changes.
2. Add and inspect unit tests covering worker scoping, tool adaptation, decider heuristics, diagnostics emission, and DI strategy selection.
3. Do not run `dotnet restore`, `dotnet build`, `dotnet test`, or Sonar scripts locally because the user explicitly stated `dotnet` is unavailable in this environment.
4. Report the validation limitation clearly in the final summary.

## Acceptance criteria

- `OrchestrationConfig` and `WorkerDefinition` exist with the requested defaults and are reachable from `LeanKernelConfig`.
- `OrchestrationResult` and `WorkerContribution` exist and are surfaced through orchestration-aware execution contracts.
- Tool definitions can be adapted into callable `AITool` instances for worker execution.
- `OrchestrationDecider` deterministically identifies multi-step or delegation-worthy requests.
- `WorkerAgent` enforces worker-specific models, tool visibility, timeout, and correlation metadata.
- `WorkerAsToolAdapter` records worker contributions for every invocation.
- `OrchestratedAgentStrategy` performs coordinator-worker execution when enabled and falls back safely otherwise.
- DI resolves `OrchestratedAgentStrategy` over routed/static when orchestration is enabled.
- `appsettings.json` contains the requested disabled-by-default orchestration section and example workers.
- The final report states that `dotnet` and Sonar validation were skipped because the required tooling is unavailable in this environment.
