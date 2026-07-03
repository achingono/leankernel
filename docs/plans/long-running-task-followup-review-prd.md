# PRD: Long-Running Task Follow-up Review Findings

## Context
This PRD captures high-signal issues identified during code review of the long-running task continuation and typing/progress implementation.

## Problem
The feature is functionally complete, but three implementation risks remain:
1. session-scoped progress dispatch can leak across turns,
2. synthetic continuation prompts are persisted as user turns,
3. one concurrency test can flake under load.

## Goals
- Eliminate cross-turn progress leakage and response ordering races.
- Keep continuation internals out of user-visible history and user-role persistence.
- Remove timing-based flakiness from progress broker tests.

## Non-Goals
- Redesign of progress UX copy.
- Broader refactors outside continuation/progress/history boundaries.

## Findings (Reviewed)

### 1. Cross-turn progress leakage and race with final send (**High**)
- **Files:** `src/LeanKernel.Channels/ChannelRouter.cs` (subscription and progress routing paths).
- **Issue:** Progress subscription is keyed by `sessionId` only and does not filter by active turn id, so stale handlers can emit updates for subsequent turns. This can create duplicate/out-of-order progress messages and concurrent sends.
- **Impact:** Incorrect user-facing state, noisy progress spam, and potential send interleaving.

### 2. Synthetic continuation prompt persisted as user turn (**Medium**)
- **Files:** `src/LeanKernel.Agents/ContinuationTurnPipeline.cs`, `src/LeanKernel.Agents/TurnPipeline.cs`, `src/LeanKernel.Gateway/Services/ChatService.cs`.
- **Issue:** Auto-continuation uses a synthetic prompt (`"Continue working..."`) that currently flows through normal user-turn persistence/rendering paths.
- **Impact:** Polluted chat history, misleading transcript attribution, and avoidable context contamination.

### 3. Timing-based concurrency assertion in tests (**Low**)
- **File:** `test/LeanKernel.Tests.Unit/Agents/TurnProgressBrokerTests.cs`.
- **Issue:** Concurrency test depends on fixed `Task.Delay(...)` to infer simultaneous handler start.
- **Impact:** Intermittent CI failures under resource contention.

## Implementation Plan
1. **Turn-bound progress routing**
   - Ensure router determines/propagates a concrete turn id for each routed inbound message.
   - Filter broker updates to the active turn id before emitting progress.
   - Stop progress subscription/heartbeat before final response send to avoid stale overlap.
   - Keep a single send gate for progress/final message ordering.

2. **Internal continuation turn handling**
   - Mark synthetic continuation prompts with explicit internal metadata.
   - Exclude internal continuation prompts from user-role persistence and user-facing transcript mapping.
   - Preserve required internal context without exposing synthetic user prompts in UI history.

3. **Deterministic concurrency test signaling**
   - Replace delay-based assertions with deterministic synchronization (`TaskCompletionSource`/barrier).
   - Add bounded timeout waits and explicit failure messages.

## Acceptance Criteria
- Progress updates emitted by `ChannelRouter` are scoped to the active turn and do not leak across turns.
- Synthetic continuation prompts are not persisted/rendered as user chat turns.
- `TurnProgressBroker` concurrency test no longer depends on `Task.Delay(...)`.
- Targeted unit tests covering these paths pass consistently.

## Risks
- Over-filtering progress by turn id could suppress legitimate updates if turn metadata wiring is incomplete.
- History filtering must preserve context assembly correctness while removing synthetic user artifacts.

## Validation
- Targeted unit tests:
  - `ChannelRouterTests`
  - `ContinuationTurnPipelineTests`
  - `TurnPipelineTests`
  - `TurnProgressBrokerTests`
- Full unit test suite and quality gates after implementation.
