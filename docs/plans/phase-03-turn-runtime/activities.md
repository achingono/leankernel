# Phase 03 Activities

## Step-By-Step Activities
1. Confirm the current implicit turn flow: trace how `/v1/responses` invokes the `leankernel` agent, where `MemoryProvider` injects context, and where `DbChatHistoryProvider` loads/saves turns.
2. Define pipeline contracts in `LeanKernel.Logic`: a `TurnContext` model (permit, inbound message, budgets) and an ordered `ITurnStage` sequence, plus a `TurnPipeline` orchestrator.
3. Implement the context gatekeeper: deny-by-default admission of candidate context items (identity, memory, retrieval, history) with an explicit budget accounting stage; emit an admission record for later diagnostics.
4. Implement deterministic history shaping: a `HistoryShaper` that selects the eligible window from `DbChatHistoryProvider`, and a compaction strategy that summarizes overflow deterministically (stable ordering, no time-dependent nondeterminism).
5. Implement scoped retrieval: a retrieval scope policy that filters knowledge/memory candidates by tenant/user/channel, merged through the gatekeeper budget rather than appended blindly.
6. Implement prompt assembly that renders admitted context into the agent invocation in a stable, testable order.
7. Add long-running task support: a turn progress broker that emits incremental status directives, and a continuation pipeline path for turns needing more than one model call.
8. Wire the pipeline into the gateway turn path behind configuration, keeping `app.MapOpenAIResponses()` on its current no-argument path.
9. Add configuration keys (budgets, history window size, compaction threshold, retrieval top-k) under existing `Agents`/`OpenAI` sections; add startup validation.
10. Add unit tests (admission, budget overflow, compaction determinism, scope filtering, continuation) and an integration test asserting end-to-end turn behavior with persistence.
11. Update `docs/features/` and `docs/architecture/runtime-flows.md` to document the pipeline.

## Review Focus
- Deny-by-default admission is truly default-deny, not default-allow with filters.
- Budget accounting is enforced before model invocation and is deterministic.
- History compaction is deterministic and does not leak cross-partition turns.
- Retrieval scoping preserves tenant/user/channel isolation.
- Continuation does not double-persist turns or corrupt agent state.
- No change to `MapOpenAIResponses()` semantics or memory-pipeline scope keys.
