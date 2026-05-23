# Phase 1 LeanKernel.Agents PRD

- **Status:** Reviewed
- **Audience:** LeanKernel maintainers and implementers
- **Phase goal:** Implement the LeanKernel.Agents rearchitecture slice so the solution has a MAF-native agent runtime, turn pipeline, and default model invocation strategy backed by LiteLLM.
- **Plan review:** Reviewed by `gpt-5-mini`. Review outcome: proceed after adding an explicit `Microsoft.Extensions.DependencyInjection.Abstractions` package reference, keeping constructor null-guards and structured logging consistent with the repo, preserving `message.SessionId` and `message.Timestamp`, surfacing visible tool names in the gated conversation context even before `AITool` adapter work, updating directly coupled tests away from `AssemblyMarker`, and recording the local validation blocker because `dotnet` is unavailable in this environment.

## Problem statement

`LeanKernel.Agents` is still a scaffold project with placeholder files only. The rearchitecture requires a concrete agent package that can build a LiteLLM-backed chat client, execute the canonical turn pipeline, and expose a stable `IAgentRuntime` entry point without pushing orchestration logic into other projects.

## Scope

This task will:

1. Implement `AgentFactory` in `src/LeanKernel.Agents` to create an `IChatClient` backed by LiteLLM configuration from `LeanKernelConfig`.
2. Add the `Strategies` folder with `IAgentStrategy`, `AgentStrategyContext`, and `StaticAgentStrategy` for the default single-model invocation flow.
3. Implement `TurnPipeline` as the canonical runtime pipeline for session persistence, context gating, prompt assembly, visible tool discovery, strategy invocation, optional response enhancement, assistant-turn persistence, and post-turn event publishing.
4. Implement `AgentRuntime` as the public `IAgentRuntime` wrapper over `ITurnPipeline`.
5. Implement `AgentsServiceCollectionExtensions.AddLeanKernelAgents(IServiceCollection)` using the existing registration pattern.
6. Add any directly required package references to `LeanKernel.Agents.csproj`, including `Microsoft.Extensions.DependencyInjection.Abstractions`.
7. Remove placeholder scaffold files `AssemblyMarker.cs` and `GlobalUsings.cs` from `src/LeanKernel.Agents`.
8. Update directly coupled smoke tests and add focused unit tests for the new agents behavior where low-cost and directly relevant.
9. Attempt restore, build, test, coverage, and Sonar validation, capturing the current `dotnet` availability blocker if it persists.
10. Mark SQL todo `p1-agents` done after implementation and validation attempts complete.

## Out of scope

- Building the `ToolDefinition` to `AITool` adapter layer for model-native tool calling.
- Introducing new abstractions beyond the existing runtime and pipeline interfaces in `LeanKernel.Abstractions`.
- Moving prompt assembly, persistence, or tool-governance behavior into `LeanKernel.Host`.
- Implementing advanced orchestration or multi-model routing strategies beyond the requested static strategy.

## File plan

### Files to add

- `src/LeanKernel.Agents/AgentFactory.cs`
- `src/LeanKernel.Agents/AgentRuntime.cs`
- `src/LeanKernel.Agents/TurnPipeline.cs`
- `src/LeanKernel.Agents/AgentsServiceCollectionExtensions.cs`
- `src/LeanKernel.Agents/Strategies/IAgentStrategy.cs`
- `src/LeanKernel.Agents/Strategies/AgentStrategyContext.cs`
- `src/LeanKernel.Agents/Strategies/StaticAgentStrategy.cs`
- `src/LeanKernel.Tests.Unit/Agents/StaticAgentStrategyTests.cs`
- `src/LeanKernel.Tests.Unit/Agents/TurnPipelineTests.cs`

### Files to remove

- `src/LeanKernel.Agents/AssemblyMarker.cs`
- `src/LeanKernel.Agents/GlobalUsings.cs`

### Files to update

- `src/LeanKernel.Agents/LeanKernel.Agents.csproj`
- `src/LeanKernel.Tests.Unit/ScaffoldSmokeTests.cs`
- `docs/plans/index.md`

## Design notes

### Agent factory

- Bind LiteLLM settings from `LeanKernelConfig.LiteLlm`.
- Expose the configured `IChatClient` and default model for downstream strategies.
- Keep a second constructor for test injection of a prebuilt `IChatClient`.
- Use constructor argument validation and structured initialization logging.

### Strategy surface

- `IAgentStrategy` represents one invocation style for a turn.
- `AgentStrategyContext` carries the user message, system prompt, admitted history, and optional `AITool` list.
- `StaticAgentStrategy` maps the system prompt, prior turns, and current user message into `ChatMessage` instances, attaches tools when present, invokes the chat client once, and returns `response.Text ?? string.Empty` deterministically.

### Turn pipeline

- Respect `message.SessionId` when provided; otherwise create or resolve a session id from channel and sender.
- Persist the user turn before gating so stored history includes the new input.
- Build the context budget with `ContextBudget.FromConfig(_config.LiteLlm.ContextWindowTokens, _config.Context)`.
- Call `IContextGatekeeper` for deny-by-default context admission and `PromptAssembler` for the system message.
- Query visible tools via `IToolRegistry.GetVisibleTools(new ToolVisibilityContext { UserId = message.SenderId })`.
- Surface visible tool names in the gated `ConversationContext.ActiveToolNames` even while the `AITool` adapter is deferred.
- Pass `Tools = null` into the strategy until a concrete adapter exists.
- Apply `IResponseEnhancer` and `ITurnEventSink` only when those optional collaborators are available.
- Persist the assistant turn with an explicit UTC timestamp and emit a `TurnEvent` containing the configured model name.

### Service registration

- Register `AgentFactory`, `IAgentStrategy`, `ITurnPipeline`, and `IAgentRuntime` as singletons.
- Keep registration feature-local to `LeanKernel.Agents` and avoid introducing host-specific composition logic.

### Testing

- Update the smoke test to anchor on a concrete agents type such as `AgentRuntime` rather than `AssemblyMarker`.
- Add focused unit coverage for `StaticAgentStrategy` message/response behavior and `TurnPipeline` session persistence plus optional collaborator flow using injected test doubles.

## Validation plan

1. Inspect the resulting diff for namespace, dependency, and scaffold-cleanup correctness.
2. Attempt `dotnet restore src/LeanKernel.sln`.
3. Attempt `dotnet build src/LeanKernel.sln --no-restore -v minimal`.
4. Attempt `dotnet test src/LeanKernel.sln --no-build -v minimal`.
5. Attempt `scripts/quality/test-coverage.sh`.
6. Attempt `scripts/quality/sonarqube-scan.sh`.
7. If the environment still lacks `dotnet`, record that blocker explicitly and verify source-level consistency of the edited files.

## Acceptance criteria

- `LeanKernel.Agents` contains the requested agent factory, strategy surface, turn pipeline, runtime entry point, and DI registration surface.
- The pipeline reuses existing abstractions and keeps orchestration behavior inside `LeanKernel.Agents` rather than other projects.
- Visible tool names are surfaced in the gated conversation context without prematurely implementing model-native tool calling.
- Placeholder files are removed from `LeanKernel.Agents`.
- Directly coupled tests reference a concrete agents type and cover the new behavior.
- Validation evidence is recorded, including the current `dotnet` blocker if it remains unresolved.
- SQL todo `p1-agents` is marked `done` only after implementation and validation attempts are complete.
