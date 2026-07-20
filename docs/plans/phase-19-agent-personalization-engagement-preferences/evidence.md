# Phase 19 Agent Personalization and Engagement Preferences — Evidence

## Evidence Log

| Item | Reference | Notes |
| --- | --- | --- |
| Current flat instruction injection | `src/Common/LeanKernel.Logic/TurnRuntime/PromptAssembler.cs:26-29` | System message uses `AgentSettings.DefaultInstructions` verbatim — the composition point to replace |
| Base instructions config | `src/Common/LeanKernel.Logic/Configuration/AgentSettings.cs:21` | `DefaultInstructions` default string; new `Personalization` options nest under `Agents` |
| Admission ordering (system/identity first) | `src/Common/LeanKernel.Logic/TurnRuntime/ContextGatekeeper.cs:29-72` | Preference block must fit the system budget; safety segment must stay admitted |
| Context item shape + sources | `src/Common/LeanKernel.Logic/TurnRuntime/TurnContext.cs:84-110` | Sources: system/identity/memory/retrieval; renderer emits a system/preferences item |
| Identity partition keys | `src/Common/LeanKernel.Core/Interfaces/IPermit.cs:8-53` | `TenantId`, `PersonId`, `UserId`, `ChannelId` for scoping and isolation |
| EF context + DbSets | `src/Common/LeanKernel.Data/EntityContext.cs:9-62` | Register new preference `DbSet`s and migration here |
| Policy entity precedent | `src/Common/LeanKernel.Core/Entities/ChannelMemoryPolicyEntity.cs`, `UserEntity.cs` | Auditable/recyclable conventions for the new entities |
| Deterministic-render reference | `docs/plans/phase-16-identity-claims-context/` | Allowlisted identity-context rendering pattern to mirror |
| Pipeline DI + stage order | `src/Common/LeanKernel.Logic/TurnRuntime/TurnPipelineServiceExtensions.cs:39-52` | Current order ScopedRetrieval → ContextGatekeeper → HistoryShaper → PromptAssembler; new `PreferenceCompositionStage` slots after HistoryShaper, before PromptAssembler |
| Budget settings (system/retrieval tiers) | `src/Common/LeanKernel.Logic/Configuration/TurnPipelineSettings.cs:71-80` | `SystemContextTokenBudget`/`RetrievalTokenBudget`; preferences deliberately avoid these by riding in `ComposedInstructions` |
| Pipeline invoked only in tests today | no `new TurnContext` in `src/` (only under `test/LeanKernel.Tests.Unit/TurnRuntime/`) | Confirms the per-turn override transport is defined at the future production invocation site |
| | | _Add build/test/scan/deep-review results here as the phase executes._ |
