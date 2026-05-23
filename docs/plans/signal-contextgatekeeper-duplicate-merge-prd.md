# Signal ContextGatekeeper Duplicate-Merge Crash PRD

## Overview and problem statement

Signal user did not receive replies to the last two messages. Runtime logs show `ContextGatekeeper.MergeCandidates` throwing `ArgumentException` ("An item with the same key has already been added") during fallback recall, which aborts turn processing before a response is sent.

## Goals

- Eliminate duplicate-key crashes in context candidate merging.
- Ensure Signal (and all channels) always return a fallback response when context assembly fails unexpectedly.
- Preserve current ranking behavior while deduplicating duplicate retrieval candidates safely.

## Non-goals

- No broad ranking strategy redesign.
- No changes to channel transport behavior.

## Functional requirements

1. `ContextGatekeeper.MergeCandidates` must tolerate duplicates in both primary and fallback lists.
2. Duplicate handling must keep the strongest candidate using existing score/similarity precedence.
3. `ThinkerService.ProcessAsync` must handle gatekeeper failures with the standard fallback response path (except cancellation).
4. Context assembly failures must not result in silent dropped user messages.

## Architecture and code touchpoints

- `src/LeanKernel.Archivist/ContextGatekeeper.cs`
  - Replace `primary.ToDictionary(...)` with collision-safe iterative merge logic.
- `src/LeanKernel.Thinker/ThinkerService.cs`
  - Extend error handling to include `GateContextAsync` failures and return fallback response.
  - Preserve `OperationCanceledException` propagation semantics.

## Test requirements

- Add unit coverage for duplicate merge scenarios in gatekeeper tests:
  - duplicate keys within primary list
  - duplicate keys within fallback list
  - case-insensitive key collisions
  - same `EntryId` across different `SourceType` retained independently
- Add ThinkerService resilience test:
  - gatekeeper exception returns fallback response (instead of dropped turn).

## Rollout and acceptance criteria

1. Repro path from logs no longer throws in `MergeCandidates`.
2. A failing gatekeeper path still produces a user-visible fallback response.
3. `dotnet restore src/LeanKernel.sln`
4. `dotnet build src/LeanKernel.sln --no-restore -v minimal`
5. `dotnet test src/LeanKernel.sln --no-build -v minimal`
6. `scripts/quality/test-coverage.sh`
7. `scripts/quality/sonarqube-scan.sh`
8. `docker compose build`

## Risks and mitigations

- **Risk:** Deduping may alter candidate ordering.
  - **Mitigation:** Preserve existing "higher semantic similarity / score wins" rule.
- **Risk:** Expanded try/catch in Thinker could accidentally swallow cancellation.
  - **Mitigation:** Keep explicit cancellation propagation behavior and add test coverage.

## Implementation checklist

- [ ] Implement collision-safe merge in `ContextGatekeeper.MergeCandidates`.
- [ ] Handle `GateContextAsync` failures in `ThinkerService.ProcessAsync` with fallback response behavior.
- [ ] Add/extend unit tests for duplicate merge and gatekeeper failure resilience.
- [ ] Run full validation and quality gates listed above.
