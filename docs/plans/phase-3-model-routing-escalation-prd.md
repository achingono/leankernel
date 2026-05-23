# Phase 3 Model Routing & Escalation PRD

- **Status:** Reviewed
- **Audience:** LeanKernel maintainers and contributors implementing Phase 3 routing behavior.
- **Document type:** Product requirements document
- **Phase goal:** Add deterministic task complexity scoring, policy-based model selection, and capped escalation while preserving the existing single-model path when routing is disabled.
- **Plan review:** Reviewed by `gpt-5-mini`. Review outcome: proceed with routing abstractions, keep diagnostics backward-compatible, avoid logging prompt content, preserve testability by mocking chat clients/factory seams, and document that `dotnet`/Sonar validation is unavailable in this environment.

## Problem statement

LeanKernel currently invokes a single configured LiteLLM model through `StaticAgentStrategy`, which means every turn receives the same model tier regardless of task complexity, context size, or quality outcomes. Phase 3 requires deterministic routing that can choose an economy, standard, or premium model based on workload complexity and optionally escalate to a higher tier when quality gates fail, while keeping routing disabled by default for safe rollout.

## Scope

This task will:

1. Expand `RoutingConfig` with per-tier model settings, shadow-routing placeholders, and complexity scoring settings.
2. Add a `RoutingDecision` model for diagnostics and event payloads.
3. Implement routing collaborators in `src/LeanKernel.Agents/Routing/`:
   - `TaskComplexityScorer`
   - `PolicyModelSelector`
   - `EscalationPolicy`
   - `RoutedAgentStrategy`
4. Extend `AgentFactory` so callers can request an `IChatClient` for a specific model while preserving the current default client behavior.
5. Update DI registration so `IAgentStrategy` resolves to `RoutedAgentStrategy` only when `LeanKernel:Routing:Enabled` is true.
6. Surface routing decisions through turn diagnostics/event output without breaking existing static-routing flows.
7. Add unit coverage for routing scoring, tier selection, escalation, and routed strategy behavior.
8. Update `src/LeanKernel.Gateway/appsettings.json` with the new disabled-by-default routing configuration.
9. Record validation limits caused by the missing `dotnet` tool in this environment.

## Out of scope

- Enabling routing by default.
- Implementing real shadow-routing execution beyond config placeholders.
- Changing provider authentication or LiteLLM transport semantics beyond selecting a model name.
- Adding new persistence schema specifically for routing decisions.
- Running restore/build/test/Sonar locally when the required tooling is unavailable.

## Source files

Primary implementation and validation targets:

- `src/LeanKernel.Abstractions/Configuration/RoutingConfig.cs`
- `src/LeanKernel.Abstractions/Configuration/LiteLlmConfig.cs`
- `src/LeanKernel.Abstractions/Configuration/LeanKernelConfig.cs`
- `src/LeanKernel.Abstractions/Enums/ModelTier.cs`
- `src/LeanKernel.Abstractions/Enums/QualityOutcome.cs`
- `src/LeanKernel.Abstractions/Models/TurnEvent.cs`
- `src/LeanKernel.Agents/AgentFactory.cs`
- `src/LeanKernel.Agents/AgentsServiceCollectionExtensions.cs`
- `src/LeanKernel.Agents/TurnPipeline.cs`
- `src/LeanKernel.Agents/Strategies/IAgentStrategy.cs`
- `src/LeanKernel.Agents/Strategies/AgentStrategyContext.cs`
- `src/LeanKernel.Agents/Strategies/StaticAgentStrategy.cs`
- `src/LeanKernel.Diagnostics/DiagnosticsCollector.cs`
- `src/LeanKernel.Gateway/appsettings.json`
- `src/LeanKernel.Tests.Unit/Agents/AgentRuntimeTests.cs`
- `src/LeanKernel.Tests.Unit/Agents/StaticAgentStrategyTests.cs`
- `src/LeanKernel.Tests.Unit/Agents/TurnPipelineTests.cs`
- `src/LeanKernel.Tests.Unit/Diagnostics/DiagnosticsCollectorTests.cs`

## Functional requirements

### FR-1 Routing configuration

- `RoutingConfig` must expose:
  - `ShadowModel`
  - `Economy`, `Standard`, and `Premium` tier settings
  - `Scoring` thresholds and boosts
- Defaults must match the requested Phase 3 values.
- Routing remains disabled unless `Enabled` is explicitly set to `true`.

### FR-2 Deterministic complexity scoring

- `TaskComplexityScorer` must produce a deterministic `double` score in the `0.0` to `1.0` range.
- Scoring inputs must be derived only from the provided strategy context, not external state.
- Scoring factors must include prompt size, available tools, history length, multi-step instructions, and system prompt complexity.
- The scorer must also return a stable list of contributing factors for diagnostics.

### FR-3 Policy-based model selection

- `PolicyModelSelector` must map score ranges to tiers:
  - `< 0.3` → `Economy`
  - `0.3` to `0.7` inclusive → `Standard`
  - `> 0.7` → `Premium`
- The selector must return the configured model name and a human-readable reason.
- Tier-to-model mapping must come from `RoutingConfig`, not hard-coded runtime logic.

### FR-4 Escalation policy

- `EscalationPolicy` must permit only forward movement through the tier ladder:
  - `Economy` → `Standard` → `Premium`
- Escalation must stop when:
  - the selected tier is already `Premium`, or
  - `MaxEscalationAttempts` has been reached.
- Escalation decisions must populate `EscalatedFrom` and increment `EscalationAttempt`.

### FR-5 Routed strategy behavior

- `RoutedAgentStrategy` must implement `IAgentStrategy` and report `Name = "routed"`.
- Flow:
  1. Score complexity.
  2. Select a model.
  3. Invoke LiteLLM using a client created for that model.
  4. Run quality checks.
  5. Escalate when allowed and needed.
  6. Return the final model text.
- Quality evaluation must at minimum cover empty, too-short, and refusal-like outputs based on current routing settings and `QualityOutcome`.
- The strategy must log routing decisions without logging raw prompt content.

### FR-6 Diagnostics and event output

- Routing decisions must be emitted to diagnostics/event output as optional metadata.
- Existing consumers that only expect `ModelUsed` must continue to function.
- `DiagnosticsCollector.RecordModelRoutingAsync` should accept enough information to persist the structured routing decision.

### FR-7 Dependency injection and compatibility

- `AddLeanKernelAgents` must continue registering `AgentFactory`, `ITurnPipeline`, and `IAgentRuntime`.
- Routing services (`TaskComplexityScorer`, `PolicyModelSelector`, `EscalationPolicy`) must be registered with appropriate lifetimes.
- The active `IAgentStrategy` implementation must be selected from config so existing deployments keep `StaticAgentStrategy` until routing is enabled.

### FR-8 Unit tests

- Add unit tests for simple, medium, and complex scoring scenarios.
- Add selector tests for all score boundaries.
- Add escalation tests for progression and hard limits.
- Add routed strategy tests using mocked chat clients/factory seams to avoid network access.
- Update existing tests where diagnostics contracts or DI registration change.

## Design constraints

- Follow file-scoped namespaces, nullable reference types, constructor DI, and existing .NET patterns in this repository.
- Keep routing logic outside `LeanKernel.Gateway`; composition-only changes belong in service registration and config.
- Prefer deterministic, side-effect-light collaborators for easy unit testing.
- Preserve the current `StaticAgentStrategy` path for disabled routing.
- Reuse `Microsoft.Extensions.AI` `IChatClient` via `AgentFactory` rather than introducing a new transport layer.

## Diagnostics and safety requirements

- Log selected tier, selected model, score, escalation attempt, and factor summaries.
- Do not log user prompts, system prompts, or history content in routing diagnostics.
- Represent routing diagnostics with optional fields so static-routing events remain valid.

## Validation plan

1. Review all affected source files for compile-time consistency and backward-compatible contracts.
2. Add and inspect unit tests covering the new routing collaborators and updated diagnostics/event contracts.
3. Do not run `dotnet restore`, `dotnet build`, `dotnet test`, or Sonar scripts locally because the user explicitly stated `dotnet` is unavailable in this environment.
4. Report the validation limitation clearly in the final summary.

## Acceptance criteria

- Phase 3 routing configuration models exist with the requested defaults.
- A `RoutingDecision` record exists and is used by routed execution diagnostics.
- Routing-enabled DI resolves `RoutedAgentStrategy`; routing-disabled DI resolves `StaticAgentStrategy`.
- `AgentFactory` can create chat clients for arbitrary LiteLLM model names.
- `RoutedAgentStrategy` performs deterministic scoring, model selection, quality checks, and capped escalation.
- Turn diagnostics/event output includes routing metadata when available.
- Unit tests cover routing scoring, selection, escalation, and routed strategy flow.
- `appsettings.json` contains the expanded disabled-by-default routing section.
- The final report states that `dotnet` and Sonar validation were skipped because the environment lacks the required tooling.
