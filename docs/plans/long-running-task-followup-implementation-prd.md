# PRD: Long-Running Task Follow-up Implementation Plan

## Context
This plan implements `docs/plans/long-running-task-followup-review-prd.md` after an independent review pass.

## Scope
- Eliminate cross-turn progress leakage and final-send ordering races.
- Keep synthetic continuation prompts out of user-role persistence and chat transcripts.
- Remove timing-based concurrency assertions in progress broker tests.

## Reviewed Design Decisions

### 1. Channel router turn scoping and ordered final send
- File: `src/LeanKernel.Channels/ChannelRouter.cs`.
- Resolve/create one inbound turn id at routing start and set both `turn_id` and `turnId` on the runtime message metadata.
- Track active turn id in progress state and ignore broker updates whose `TurnId` does not match.
- Add a finalization guard in progress state so late callbacks cannot emit progress after shutdown starts.
- Stop heartbeat/progress subscription before final response send.
- Use a single send gate for both progress and final sends to preserve ordering.

### 2. Internal continuation prompt handling and metadata contract
- Files: `src/LeanKernel.Agents/ContinuationTurnPipeline.cs`, `src/LeanKernel.Agents/TurnPipeline.cs`.
- Mark synthetic continuation prompts with explicit internal metadata:
  - `internal_turn=true`
  - `internal_reason=auto_continuation_prompt`
  - `root_turn_id` / `rootTurnId`
- Continuation progress events use root turn id for router correlation.
- Turn pipeline skips persisting internal continuation prompts as user turns when marker and reason are present.
- Keep assistant turn persistence and continuation execution behavior unchanged.

### 3. Chat history/session filtering for legacy synthetic prompts
- File: `src/LeanKernel.Gateway/Services/ChatService.cs`.
- Filter internal continuation prompts from loaded chat history so they are not rendered in UI transcript.
- Exclude internal synthetic turns when deriving session title/preview so existing persisted data does not pollute summaries.

### 4. Deterministic progress broker concurrency test
- File: `test/LeanKernel.Tests.Unit/Agents/TurnProgressBrokerTests.cs`.
- Replace `Task.Delay(...)` concurrency inference with deterministic synchronization using `TaskCompletionSource` barriers.
- Add bounded timeout waits and explicit failure diagnostics.

## Acceptance Criteria
- Channel progress updates are scoped to the active inbound turn id and do not leak across turns.
- Final channel response does not race with stale progress sends.
- Synthetic continuation prompts are not persisted as user turns and are not rendered in chat history/session summaries.
- Progress broker concurrency test has no timing-based `Task.Delay(...)` assertion.
- Targeted unit tests pass for ChannelRouter, continuation/turn pipeline paths, chat service filtering, and progress broker concurrency.

## Validation Plan
Run targeted tests:
- `test/LeanKernel.Tests.Unit/Channels/ChannelRouterTests.cs`
- `test/LeanKernel.Tests.Unit/Agents/ContinuationTurnPipelineTests.cs`
- `test/LeanKernel.Tests.Unit/Agents/TurnPipelineTests.cs`
- `test/LeanKernel.Tests.Unit/Gateway/ChatServiceTests.cs` (new/updated)
- `test/LeanKernel.Tests.Unit/Agents/TurnProgressBrokerTests.cs`

Then run full unit and quality gates per repository workflow.
