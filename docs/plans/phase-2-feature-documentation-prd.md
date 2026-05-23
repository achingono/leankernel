# Phase 2 Feature Documentation PRD

- **Status:** Reviewed
- **Audience:** LeanKernel maintainers, contributors, and operators who need implementation-accurate Phase 2 runtime explanations.
- **Document type:** Explanation (Diátaxis)
- **Phase goal:** Publish the Phase 2 feature and configuration documentation for context, personalization, history shaping, and channel routing features already implemented in the rearchitecture tree.
- **Plan review:** Reviewed by `gpt-5-mini`. Review outcome: proceed, document implemented behavior only, verify the dedicated context diagnostics API against source before writing, and update navigation links from `channel-routing.md` to `channels.md`.

## Problem statement

Phase 2 implementation work added identity grounding, additive onboarding, scoped retrieval, deterministic history shaping, and channel routing, but the explanation-oriented documentation set is incomplete and the Phase 2 configuration reference is partial. Contributors can inspect the code, but they do not yet have a concise feature-level explanation of how these slices fit together, how they are configured, or where the current implementation still differs from the broader Phase 2 roadmap.

## Scope

This task will:

1. Create `docs/features/identity-onboarding.md`.
2. Create `docs/features/scoped-retrieval.md`.
3. Create `docs/features/history-shaping.md`.
4. Create `docs/features/channels.md`.
5. Create `docs/features/context-diagnostics-api.md`.
6. Expand `docs/configuration/phase-2-config.md` to cover all implemented Phase 2 settings.
7. Update `docs/features/index.md` with the complete Phase 2 feature list.
8. Update directly related navigation links that still point to `channel-routing.md` so the new feature doc is discoverable.
9. Record the local validation limitation that `dotnet` is unavailable and therefore build/test/Sonar steps are skipped for this documentation task.

## Out of scope

- Changing runtime behavior, API contracts, or configuration models.
- Changing the existing context diagnostics API, endpoint contracts, or configuration defaults.
- Rewriting unrelated documentation outside the directly affected feature/configuration/navigation files.
- Claiming planned functionality is already shipped.

## Source files

The new and updated docs must stay aligned to these implementation sources:

- `src/LeanKernel.Abstractions/Configuration/IdentityConfig.cs`
- `src/LeanKernel.Abstractions/Configuration/RetrievalConfig.cs`
- `src/LeanKernel.Abstractions/Configuration/HistoryConfig.cs`
- `src/LeanKernel.Abstractions/Configuration/ChannelsConfig.cs`
- `src/LeanKernel.Abstractions/Configuration/DiagnosticsConfig.cs`
- `src/LeanKernel.Abstractions/Configuration/LeanKernelConfig.cs`
- `src/LeanKernel.Abstractions/Interfaces/IIdentityProvider.cs`
- `src/LeanKernel.Abstractions/Interfaces/IOnboardingDetector.cs`
- `src/LeanKernel.Abstractions/Interfaces/IScopedKnowledgeService.cs`
- `src/LeanKernel.Abstractions/Interfaces/IChannel.cs`
- `src/LeanKernel.Abstractions/Interfaces/IAgentRuntime.cs`
- `src/LeanKernel.Abstractions/Interfaces/IContextDiagnosticsService.cs`
- `src/LeanKernel.Abstractions/Models/IdentityContext.cs`
- `src/LeanKernel.Abstractions/Models/OnboardingResult.cs`
- `src/LeanKernel.Abstractions/Models/RetrievalDiagnostics.cs`
- `src/LeanKernel.Abstractions/Models/HistoryShapingDiagnostics.cs`
- `src/LeanKernel.Abstractions/Models/ContextDiagnosticsResponse.cs`
- `src/LeanKernel.Abstractions/Models/ConversationContext.cs`
- `src/LeanKernel.Abstractions/Models/HistoryTier.cs`
- `src/LeanKernel.Context/ContextGatekeeper.cs`
- `src/LeanKernel.Context/ContextCandidateRetriever.cs`
- `src/LeanKernel.Context/ConversationHistoryAssembler.cs`
- `src/LeanKernel.Context/PromptAssembler.cs`
- `src/LeanKernel.Context/ContextServiceCollectionExtensions.cs`
- `src/LeanKernel.Context/Identity/IdentityProvider.cs`
- `src/LeanKernel.Context/Identity/OnboardingGapDetector.cs`
- `src/LeanKernel.Context/Identity/OnboardingDirectiveBuilder.cs`
- `src/LeanKernel.Context/Identity/IdentityUpdateProjector.cs`
- `src/LeanKernel.Context/Retrieval/RetrievalScopePolicy.cs`
- `src/LeanKernel.Context/Retrieval/ScopedKnowledgeService.cs`
- `src/LeanKernel.Context/Retrieval/EntityExpander.cs`
- `src/LeanKernel.Context/History/HistoryCompactionStrategy.cs`
- `src/LeanKernel.Context/History/ConversationCompactor.cs`
- `src/LeanKernel.Context/History/HistoryShaper.cs`
- `src/LeanKernel.Channels/ServiceCollectionExtensions.cs`
- `src/LeanKernel.Channels/ChannelAuthenticator.cs`
- `src/LeanKernel.Channels/ChannelRouter.cs`
- `src/LeanKernel.Channels/SignalChannel.cs`
- `src/LeanKernel.Channels/ChannelHostedService.cs`
- `src/LeanKernel.Gateway/Endpoints.cs`
- `src/LeanKernel.Gateway/Program.cs`
- `src/LeanKernel.Gateway/appsettings.json`
- `src/LeanKernel.Diagnostics/ContextDiagnosticsService.cs`
- `src/LeanKernel.Diagnostics/DiagnosticsServiceCollectionExtensions.cs`
- `src/LeanKernel.Persistence/Entities/CompactionMarkerEntity.cs`
- `docs/CONTRIBUTING-DOCS.md`
- `docs/features/context-gating.md`
- `docs/features/knowledge-retrieval.md`
- `docs/features/turn-pipeline.md`
- `docs/features/diagnostics.md`
- `docs/features/gateway-api.md`
- `docs/configuration/phase-1-config.md`

## Documentation approach

### Diátaxis quadrant

Each Phase 2 feature file will be an **Explanation** document. The docs will clarify why the feature exists, how the collaborators fit together, what design constraints shape the behavior, and where the current implementation stops.

### Target audience and goal

- **Audience:** maintainers, contributors, and operators already familiar with the LeanKernel codebase.
- **Goal:** understand the implementation and its trade-offs without reading every source file first.

### Style and structure

- Use implementation-accurate names from the code.
- Use Markdown tables for components and configuration keys.
- Use Mermaid diagrams where a flow or dependency view improves clarity.
- Include short config or API examples when they help readers reason about the feature.
- Cross-link related Phase 1 and Phase 2 docs using relative paths.
- Keep planned-but-unimplemented behavior explicitly labeled as **Planned**.

## Accuracy constraints

The docs must reflect these important implementation details:

- `ContextGatekeeper` loads identity and runs onboarding detection before retrieval.
- Onboarding is additive: `OnboardingDirectiveBuilder` always instructs the assistant to continue answering the current request.
- `IdentityUpdateProjector` is best-effort, allowlist-based, and suppresses writeback failures.
- `RetrievalScopePolicy` resolves metadata in the order `retrieval_scope`, `task_scope`, then `agent_scope`, then falls back to `DefaultScope`.
- `ScopedKnowledgeService` never silently widens retrieval when a matching policy is unavailable; it returns no admitted candidates with diagnostics such as `unknown_scope`.
- `EntityExpander` uses bounded search plus linked-page traversal controlled by `MaxEntityExpansionResults` and `ContextConfig.EntityExpansionDepth`.
- `HistoryCompactionStrategy` assigns tiers deterministically by recency, but `HistoryShaper` still performs final oldest-first pruning until the history budget fits exactly.
- `ConversationCompactor` uses LiteLLM `chat/completions` with the configured compaction model, low temperature, and `MaxSummaryTokens`.
- Compaction markers are persisted through `CompactionMarkerEntity` when `PersistCompactionMarkers` is enabled and an EF `DbContextFactory` is registered.
- All channel traffic still reaches `IAgentRuntime.RunTurnAsync`; `ChannelRouter` does not implement separate reasoning logic.
- `ChannelAuthenticator` fails closed when channel auth config is missing.
- `SignalChannel` is registered only when `Channels:Signal:Enabled` is true and startup is skipped when `PhoneNumber` is empty.
- `TurnPipeline` stores a `ContextDiagnosticsSnapshot` through `IContextDiagnosticsService` after context assembly and tool visibility resolution.
- Gateway exposes `GET /api/diagnostics/{sessionId}/context`, `/budget`, and `/history`, all with optional `turnId` filtering and `404` when no matching snapshot exists.
- `DiagnosticsConfig` exposes `Enabled`, `PersistToDatabase`, `ContextDiagnosticsEnabled`, `MaxDiagnosticsPerSession`, and `ServiceName`.

## Deliverables

### `docs/features/identity-onboarding.md`

Explain identity-as-pages in GBrain, gap detection, non-blocking onboarding guidance, writeback behavior, and configuration.

### `docs/features/scoped-retrieval.md`

Explain scope policy resolution, namespace and metadata filtering, entity expansion and boosting, diagnostics, and configuration.

### `docs/features/history-shaping.md`

Explain deterministic tiering, LiteLLM compaction/summarization, final budget pruning, persisted markers, and configuration.

### `docs/features/channels.md`

Explain the channel abstraction, shared runtime path, sender authentication, Signal polling/send behavior, configuration, and extension path for new adapters.

### `docs/features/context-diagnostics-api.md`

Explain the dedicated context diagnostics API: snapshot storage, `/context`, `/budget`, and `/history` endpoints, `turnId` filtering, response models, retention behavior, and configuration.

### `docs/configuration/phase-2-config.md`

List all implemented Phase 2 keys under `LeanKernel:Identity`, `LeanKernel:Retrieval`, `LeanKernel:History`, `LeanKernel:Channels`, and `LeanKernel:Diagnostics`, including `ContextDiagnosticsEnabled` and `MaxDiagnosticsPerSession`.

## Validation plan

1. Inspect the edited docs for broken relative links and outdated file names.
2. Check Mermaid blocks, tables, and examples for Markdown correctness.
3. Verify feature docs stay aligned to current source behavior and config defaults.
4. Do not run `dotnet`-based restore/build/test/Sonar locally because the user explicitly stated `dotnet` is not available in this environment.
5. Record that code-level validation for this docs task is limited to source inspection and documentation consistency checks.

## Acceptance criteria

- The requested Phase 2 feature docs exist and follow the Explanation quadrant.
- The Phase 2 configuration reference covers the implemented Phase 2 settings comprehensively.
- `docs/features/index.md` links to the complete Phase 2 doc set.
- Navigation references use `channels.md` instead of the old `channel-routing.md` path where appropriate.
- The diagnostics documentation reflects the implemented context snapshot service, dedicated endpoints, and diagnostics configuration accurately.
- The reviewed PRD is saved under `docs/plans/` before the documentation edits.
- The final report clearly states that `dotnet`-based validation was skipped because the user requested that build steps be skipped in this environment.
