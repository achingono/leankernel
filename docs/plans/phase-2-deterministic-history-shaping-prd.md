# PRD: Phase 2 Deterministic History Shaping

- **Status:** Reviewed
- **Audience:** LeanKernel maintainers and implementers
- **Phase goal:** Implement deterministic history shaping with configurable tiering, LiteLLM-backed compaction, strict budget enforcement, and traceable compaction markers.
- **Plan review:** Reviewed by `gpt-4.1`. Review outcome: proceed with the requested slice, add `TurnId` carefully because `ConversationTurn` currently lacks it, keep diagnostics propagation non-breaking for current consumers, bind `HistoryConfig` in the Gateway composition root, persist compaction markers through the existing EF persistence registration, and record the local validation blocker because `dotnet` is unavailable in this environment.

## Problem statement

Phase 1 only keeps the newest conversation turns verbatim until the history budget is exhausted. Phase 2 requires deterministic tier assignment so recent turns stay verbatim, older turns can be compacted or summarized, and operators can audit what was transformed or dropped.

## Scope

This task will:

1. Add `HistoryConfig` to `LeanKernel.Abstractions.Configuration` and expose it from `LeanKernelConfig`.
2. Add `HistoryTier`, `ShapedHistoryEntry`, `CompactionMarker`, and `HistoryShapingDiagnostics` to `LeanKernel.Abstractions.Models`.
3. Add `IConversationCompactor` and the deterministic history shaping collaborators required by the context pipeline.
4. Add deterministic history shaping components under `src/LeanKernel.Context/History/`.
5. Update `ConversationHistoryAssembler` to use `HistoryShaper` when enabled and preserve legacy truncation when disabled.
6. Update `ConversationContext` and `ContextGatekeeper` only as needed to surface `HistoryShapingDiagnostics` without breaking existing behavior.
7. Add `CompactionMarkerEntity`, `DbSet`, EF model configuration, and `PostgresSessionStore` persistence for compaction markers.
8. Add unit tests for strategy, compactor, shaper, persistence, and assembler integration/fallback behavior.
9. Update `src/LeanKernel.Gateway/appsettings.json` and plan index documentation for the new history configuration.
10. Attempt validation, recording that `dotnet` is unavailable if the blocker persists.

## Out of scope

- Diagnostics HTTP endpoints for `/history` and broader Phase 2 diagnostics APIs.
- Background or asynchronous compaction workflows beyond inline shaping for current context assembly.
- Automatic deduplication/versioning of already-compacted ranges beyond deterministic marker persistence for this slice.
- UI or operator-console surfaces for browsing compaction markers.

## Functional requirements

### FR-1 Deterministic tier assignment

- Assign history tiers by recency using stable rules for a fixed input sequence:
  - newest `RecentTurnsVerbatim` turns -> `Verbatim`
  - next `CompactedTurnsMax` turns -> `Compacted`
  - next `SummarizedTurnsMax` turns -> `Summarized`
  - remaining turns -> `Dropped`
- Tier assignment must not depend on model output.

### FR-2 Strict budget enforcement

- Respect the conversation budget slice passed into history assembly.
- If shaped content still exceeds budget after compaction/summarization, drop the oldest non-verbatim shaped segments first until the budget fits.
- Never exceed the supplied history budget.

### FR-3 Deterministic compaction execution

- Use fixed prompts and low temperature for LiteLLM requests.
- Use the configured compaction model, temperature, and max summary token cap.
- Keep prompt instructions concise and stable so repeated runs are consistent.

### FR-4 Traceability

- Every compacted or summarized artifact must be associated with a `CompactionMarker` containing timestamps, source counts, token counts, and model identity.
- Marker persistence must be configurable through `HistoryConfig.PersistCompactionMarkers`.

### FR-5 Backward-compatible assembly

- When history shaping is disabled, preserve current newest-turn truncation behavior.
- Existing `ConversationContext.History` must remain consumable as `IReadOnlyList<ConversationTurn>`.
- Diagnostics should be additive and optional for current consumers.

## Design notes

### Abstractions

- Add `HistoryConfig` under `LeanKernel.Abstractions.Configuration` and a `History` property on `LeanKernelConfig`.
- Add `TurnId` to `ConversationTurn` while preserving existing `IsCompacted` and `CompactionSourceId` fields so persistence and prompt rendering stay compatible.
- Persist compaction markers through the existing EF-backed persistence registration so `HistoryShaper` can store trace records without changing the session-store contract for this slice.

### Context pipeline

- `HistoryCompactionStrategy` performs deterministic tier selection and token accounting.
- `ConversationCompactor` handles LiteLLM HTTP calls only; it does not choose tiers.
- `HistoryShaper` orchestrates strategy, compactor calls, strict budget trimming, marker persistence, and diagnostic emission.
- `ConversationHistoryAssembler` remains the entry point used by `ContextGatekeeper` and exposes the most recent shaping diagnostics.

### Persistence

- Add `CompactionMarkerEntity` under `LeanKernel.Persistence.Entities` and a `CompactionMarkers` set on `LeanKernelDbContext`.
- Store session-scoped compaction marker rows with indexes on `SessionId` and `CompactedAt`.
- Persist the generated compacted or summarized text in `CompactedContent` for operator traceability.

### Configuration and DI

- Bind the new `History` section from `LeanKernel` in `src/LeanKernel.Gateway/Program.cs` through the existing `LeanKernelConfig` binding.
- Update `AddLeanKernelContext` to register the new collaborators and provide `IOptions<HistoryConfig>` and LiteLLM settings without moving history logic into the Gateway project.

## Validation plan

1. Review the resulting diff for contract, namespace, and DI consistency.
2. Attempt `dotnet restore src/LeanKernel.sln`.
3. Attempt `dotnet build src/LeanKernel.sln --no-restore -v minimal`.
4. Attempt `dotnet test src/LeanKernel.sln --no-build -v minimal`.
5. Attempt `scripts/quality/test-coverage.sh`.
6. Attempt `scripts/quality/sonarqube-scan.sh`.
7. If `dotnet` remains unavailable, record that blocker explicitly and still verify source-level consistency.

## Acceptance criteria

- History shaping contracts, configuration, and service registrations are added in the correct projects.
- `ConversationHistoryAssembler` supports both deterministic shaping and legacy truncation fallback.
- Persistence supports saving compaction markers for a session when enabled.
- New unit tests cover deterministic tiering, LiteLLM request/response handling, shaper orchestration, marker persistence, and assembler fallback behavior.
- Gateway configuration exposes the `History` section with the requested defaults.
- Validation evidence is recorded, including the local `dotnet` blocker if it persists.
