# Phase 2 Identity and Onboarding PRD

- **Status:** Reviewed
- **Audience:** LeanKernel maintainers and implementers
- **Phase goal:** Implement durable GBrain-backed identity grounding, additive onboarding guidance, and best-effort identity writeback for the Phase 1 runtime.
- **Plan review:** Reviewed by `gpt-5.4-mini` and `gpt-5.4`. Review outcome: proceed after explicitly including `LeanKernel.Agents` in scope, preserving new identity/onboarding context through `TurnPipeline`, carrying session correlation for projector diagnostics, treating malformed identity pages as non-fatal, and keeping writeback idempotent, additive, and non-blocking.

## Problem statement

Phase 1 provides context gating, prompt assembly, persistence, and turn orchestration, but every turn still lacks durable identity grounding and a guided way to fill missing personalization data. LeanKernel needs to load agent and user identity from GBrain before retrieval, surface weak or missing fields as additive onboarding guidance, and persist approved identity updates without introducing a new storage path outside the existing `IKnowledgeService` contract.

## Scope

This task will:

1. Add the requested identity configuration, interfaces, and models in `LeanKernel.Abstractions`.
2. Extend `ConversationContext` with identity and onboarding data and add session correlation needed for post-turn diagnostics.
3. Implement `IdentityProvider`, `OnboardingGapDetector`, `OnboardingDirectiveBuilder`, and `IdentityUpdateProjector` in `LeanKernel.Context`.
4. Parse and serialize identity pages as YAML-frontmatter-backed GBrain pages without modifying `LeanKernel.Knowledge`.
5. Load identity before retrieval in `ContextGatekeeper`, attach onboarding guidance to prompt assembly, and account for identity/onboarding tokens in system-prompt usage.
6. Preserve identity/onboarding/session data through `TurnPipeline` in `LeanKernel.Agents` so post-processing and emitted events see the same context.
7. Register the new identity services in DI and enable them from `LeanKernel.Gateway`.
8. Add targeted unit tests for identity loading, gap detection, directive generation, projector writeback/conflicts, and the related prompt/pipeline integration points.
9. Update runtime configuration and directly related documentation.
10. Record the local validation limitation that `dotnet` is unavailable in this environment.

## Out of scope

- Modifying `LeanKernel.Knowledge` internals or the `IKnowledgeService` contract.
- Changing retrieval scope policy or semantic-search behavior beyond loading identity before retrieval.
- Adding UI onboarding screens or new API endpoints.
- Introducing non-deterministic extraction or arbitrary identity-page mutation.

## Implementation plan

### Files to add

- `src/LeanKernel.Abstractions/Configuration/IdentityConfig.cs`
- `src/LeanKernel.Abstractions/Interfaces/IIdentityProvider.cs`
- `src/LeanKernel.Abstractions/Interfaces/IOnboardingDetector.cs`
- `src/LeanKernel.Abstractions/Models/IdentityContext.cs`
- `src/LeanKernel.Abstractions/Models/OnboardingResult.cs`
- `src/LeanKernel.Context/Identity/IdentityProvider.cs`
- `src/LeanKernel.Context/Identity/OnboardingGapDetector.cs`
- `src/LeanKernel.Context/Identity/OnboardingDirectiveBuilder.cs`
- `src/LeanKernel.Context/Identity/IdentityUpdateProjector.cs`
- `src/LeanKernel.Tests.Unit/Context/Identity/IdentityProviderTests.cs`
- `src/LeanKernel.Tests.Unit/Context/Identity/OnboardingGapDetectorTests.cs`
- `src/LeanKernel.Tests.Unit/Context/Identity/OnboardingDirectiveBuilderTests.cs`
- `src/LeanKernel.Tests.Unit/Context/Identity/IdentityUpdateProjectorTests.cs`

### Files to update

- `src/LeanKernel.Abstractions/Configuration/LeanKernelConfig.cs`
- `src/LeanKernel.Abstractions/Models/ConversationContext.cs`
- `src/LeanKernel.Context/ContextGatekeeper.cs`
- `src/LeanKernel.Context/PromptAssembler.cs`
- `src/LeanKernel.Context/ContextServiceCollectionExtensions.cs`
- `src/LeanKernel.Agents/TurnPipeline.cs`
- `src/LeanKernel.Gateway/Program.cs`
- `src/LeanKernel.Gateway/appsettings.json`
- `src/LeanKernel.Tests.Unit/Context/ContextGatekeeperTests.cs`
- `src/LeanKernel.Tests.Unit/Context/PromptAssemblerTests.cs`
- `src/LeanKernel.Tests.Unit/Agents/TurnPipelineTests.cs`
- `docs/plans/index.md`
- `README.md`
- `docs/features/context-gating.md`
- `docs/features/turn-pipeline.md`

## Design notes

### Identity loading

- `IdentityProvider.LoadIdentityAsync(userId)` will read the configured agent profile page and user preference page concurrently through `IKnowledgeService.GetPageAsync`.
- Page parsing will treat YAML frontmatter as optional. If frontmatter is missing or malformed, the provider will log the condition, keep the raw body content, and still emit prompt-safe segments rather than failing the turn.
- Parsed identity fields will support scalar entries and simple nested field maps containing `value`, `confidence`, `last_updated`, and `source`.
- `IdentityContext.OverallConfidence` will be derived from available parsed field confidences, falling back to `0.0` when identity is absent and `1.0` when content exists but no explicit confidence values are present.

### Onboarding detection

- `OnboardingGapDetector` will inspect configured allowlisted user-preference fields.
- It will emit gaps for missing values, placeholders such as `todo`, `unknown`, `n/a`, low-confidence values below `OnboardingConfidenceThreshold`, and stale values for time-sensitive fields such as recurring goals.
- Gap codes will be deterministic (for example `missing_preferred_name`, `weak_timezone`, `stale_recurring_goals`).

### Onboarding directive generation

- `OnboardingDirectiveBuilder` will convert the highest-value gaps into a short instruction block.
- The directive will ask at most `MaxOnboardingQuestionsPerTurn` focused questions and will always state that the assistant should still answer the user’s current request.
- If there are no gaps, or no questions are selected, the builder will return `null`.

### Identity writeback

- `IdentityUpdateProjector` will implement `IResponseEnhancer` so it can observe the final assistant response together with the resolved `ConversationContext`.
- The projector will use deterministic acknowledgement patterns to infer updates for allowlisted fields only.
- Existing higher-confidence conflicting values will be preserved. Conflicts will record a best-effort `DiagnosticEntry` using `ResponseEnhancement` as the category when `SessionId` is available in `ConversationContext`.
- Writeback failures will be logged and suppressed so user-facing responses remain unchanged.
- Reprocessing the same response should converge on the same stored page content, keeping the enhancer idempotent.

### Prompt assembly and context flow

- `ContextGatekeeper` will load identity before knowledge retrieval and history shaping.
- The returned `ConversationContext` will carry `SessionId`, `Identity`, and `Onboarding` alongside the existing prompt and admitted context data.
- `PromptAssembler` will render sections in this stable order: base system prompt, identity context, onboarding guidance, relevant knowledge, retrieved context, available tools.
- `ContextBudgetUsage.SystemPromptUsed` will include the base system prompt plus identity and onboarding tokens because those sections occupy the system-message budget slice.

### Dependency injection

- `AddLeanKernelIdentity(IServiceCollection, IdentityConfig)` will register:
  - `IIdentityProvider` → `IdentityProvider`
  - `IOnboardingDetector` → `OnboardingGapDetector`
  - `OnboardingDirectiveBuilder`
  - `IdentityUpdateProjector`
  - `IResponseEnhancer` → existing `IdentityUpdateProjector` singleton
- `LeanKernel.Gateway` will call `AddLeanKernelIdentity(leanKernelConfig.Identity)` after `AddLeanKernelContext`.

## Validation plan

1. Inspect the resulting diff and file tree for correct namespaces, DI wiring, and prompt/pipeline data flow.
2. Review unit tests for coverage of missing-page, malformed-frontmatter, low-confidence, stale-value, allowlist, and conflict scenarios.
3. Attempt repository validation commands only if the required tooling is available.
4. Record that build/test/Sonar execution is skipped locally because the user stated `dotnet` is unavailable in this environment.

## Acceptance criteria

- `LeanKernel.Abstractions` exposes the requested identity configuration, interfaces, and models.
- `LeanKernel.Context` loads identity from GBrain before retrieval, detects onboarding gaps, and surfaces identity/onboarding sections in prompt assembly.
- `ConversationContext` preserves identity/onboarding/session data through `TurnPipeline` and into optional response enhancement/event publishing.
- Identity updates are allowlisted, best-effort, non-blocking, and preserve higher-confidence existing values while recording conflicts when possible.
- Gateway configuration includes the new `LeanKernel:Identity` section.
- Targeted unit tests cover the requested identity behaviors.
- Documentation and plan index reflect the new Phase 2 identity/onboarding slice.
