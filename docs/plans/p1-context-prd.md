# Phase 1 LeanKernel.Context PRD

- **Status:** Reviewed
- **Audience:** LeanKernel maintainers and implementers
- **Phase goal:** Implement the LeanKernel.Context package so the Phase 1 runtime has deterministic token estimation, deny-by-default context gating, budget-aware history shaping, and inspectable prompt assembly.
- **Plan review:** Reviewed by `claude-haiku-4.5`. Review outcome: proceed with the requested slice, document that Phase 1 pools wiki and retrieval admission against a shared knowledge budget while still reporting usage by source, keep prompt provenance explicit in stable sectioned bullet lists, retain the requested admission reason strings, and record the local validation blocker because `dotnet` is unavailable in this environment.

## Problem statement

`LeanKernel.Context` is still a scaffold project with placeholder files only. The rearchitecture requires a real context package that starts from an empty context window, admits only budget-earning context, and emits an inspectable prompt manifest for downstream agent execution.

## Scope

This task will:

1. Implement `SimpleTokenEstimator` as the deterministic `ITokenEstimator` adapter.
2. Implement `ContextCandidateRetriever` to fetch raw knowledge candidates and recent session history without making admission decisions.
3. Implement `ConversationHistoryAssembler` to select the newest conversation turns that fit the configured history slice while preserving chronological order.
4. Implement `PromptAssembler` to emit a stable instruction manifest plus a debug-oriented full prompt view with explicit source provenance.
5. Implement `ContextGatekeeper` as the deny-by-default `IContextGatekeeper` that retrieves candidates, assembles history, admits knowledge by score and budget, and records admission decisions.
6. Implement `ContextServiceCollectionExtensions.AddLeanKernelContext(IServiceCollection, ContextConfig)` using the existing registration pattern.
7. Remove `AssemblyMarker.cs` and `GlobalUsings.cs` from `src/LeanKernel.Context`.
8. Update the directly coupled unit smoke test to stop referencing the removed `LeanKernel.Context.AssemblyMarker` type.
9. Attempt build, test, coverage, and Sonar validation, recording blockers if the local toolchain remains unavailable.
10. Mark SQL todo `p1-context` done after implementation and validation attempts.

## Out of scope

- Changing `LeanKernel.Abstractions` contracts or `ContextConfig` defaults.
- Adding new diagnostics or persistence contracts beyond the requested context package surface.
- Implementing Phase 2 summarization/compaction logic for older conversation turns.
- Introducing new tests beyond directly coupled smoke-test updates required by scaffold cleanup.

## Implementation plan

### Files to add

- `src/LeanKernel.Context/SimpleTokenEstimator.cs`
- `src/LeanKernel.Context/ContextCandidateRetriever.cs`
- `src/LeanKernel.Context/ConversationHistoryAssembler.cs`
- `src/LeanKernel.Context/PromptAssembler.cs`
- `src/LeanKernel.Context/ContextGatekeeper.cs`
- `src/LeanKernel.Context/ContextServiceCollectionExtensions.cs`

### Files to remove

- `src/LeanKernel.Context/AssemblyMarker.cs`
- `src/LeanKernel.Context/GlobalUsings.cs`

### Files to update

- `src/LeanKernel.Tests.Unit/ScaffoldSmokeTests.cs`
- `docs/plans/index.md`

## Design notes

### Token estimation

- Use a fixed approximation of four characters per token.
- Treat null or empty input as zero tokens.
- Use `Math.Ceiling` so non-empty content always rounds up deterministically.

### Candidate retrieval

- Query `IKnowledgeService.SearchAsync(message.Content, maxResults: 20, ct)` for knowledge candidates.
- Query `ISessionStore.GetHistoryAsync(sessionId, maxTurns: 50, ct)` for recent turns.
- Return a raw `ContextCandidates` DTO without ranking or filtering side effects.

### History shaping

- Walk backward from the newest turn, estimating token cost from `ConversationTurn.Content`.
- Stop when the next turn would exceed the supplied history budget.
- Return turns in chronological order.
- If no turns fit the budget, return an empty list rather than throwing.

### Prompt assembly

- Build the system instruction manifest in stable section order: system prompt, wiki facts, retrieved context, then available tools.
- Emit wiki facts as `- [source] content` and retrieved items as `- [source:key] content` so provenance is inspectable.
- Provide `AssembleFullPrompt` for debug output by appending ordered conversation turns with compacted markers.

### Context gatekeeping

- Start from an empty admitted set every turn.
- Retrieve candidates first, then assemble history against `budget.ConversationBudget`.
- Pool `budget.WikiFactsBudget + budget.RetrievalBudget` into a shared Phase 1 knowledge admission budget, while categorizing admitted items into wiki vs retrieved collections for usage reporting.
- Sort candidates by descending relevance score before evaluation so decisions are deterministic for a fixed candidate set.
- Reject a candidate when it would exceed the pooled knowledge budget or when its score is below `0.1`.
- Record each decision in `ContextAdmissionRecord` using the requested reason strings: `BudgetExhausted`, `LowRelevanceScore`, and `HighRelevanceScore`.
- Compute `ContextBudgetUsage` from the default system prompt, admitted wiki items, admitted retrieved items, and included history turns.
- Leave tool-budget consumption empty in this package because the agents layer owns tool admission.

### Service registration

- Follow existing package conventions: accept a concrete `ContextConfig` instance, register `Options.Create(config)`, and register the context collaborators plus `IContextGatekeeper` as singletons.

## Validation plan

1. Inspect the resulting file tree and diff for namespace, dependency, and scaffold-cleanup correctness.
2. Attempt `dotnet restore src/LeanKernel.sln`.
3. Attempt `dotnet build src/LeanKernel.sln --no-restore -v minimal`.
4. Attempt `dotnet test src/LeanKernel.sln --no-build -v minimal`.
5. Attempt `scripts/quality/test-coverage.sh`.
6. Attempt `scripts/quality/sonarqube-scan.sh`.
7. If `dotnet` or any other required tooling is unavailable, record that blocker explicitly and still verify source-level consistency.

## Acceptance criteria

- `LeanKernel.Context` contains the requested token estimator, candidate retriever, history assembler, prompt assembler, gatekeeper, and DI extension.
- The package keeps context-admission decisions inside `LeanKernel.Context` and does not move knowledge retrieval or persistence behavior into other projects.
- Placeholder files are removed from `LeanKernel.Context`.
- The smoke test no longer references `LeanKernel.Context.AssemblyMarker`.
- Validation evidence is recorded, including the local `dotnet` blocker if it persists.
- SQL todo `p1-context` is marked `done` after implementation and validation attempts.
