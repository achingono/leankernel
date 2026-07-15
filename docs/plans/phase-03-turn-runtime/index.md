# Phase 03 Turn Runtime And Context Gating

## Companion Documents
- [Inputs](inputs.md)
- [Activities](activities.md)
- [Outputs](outputs.md)
- [Exit Criteria](exit-criteria.md)
- [Risk Register](risk-register.md)
- [Evidence](evidence.md)

## Objective
Introduce an explicit, testable turn-execution pipeline in the rebuild so that each request flows through deterministic, observable stages instead of relying on implicit MAF middleware ordering. This phase ports the source repo's gated turn runtime — deny-by-default context assembly with budget enforcement, deterministic history shaping/compaction, scoped knowledge retrieval into the prompt, and long-running task progress/continuation — into the rebuild's `LeanKernel.Logic` and `LeanKernel.Gateway` boundaries while preserving the current identity partitioning and memory-pipeline behavior.

## Scope
This phase covers the runtime plumbing that sits between an inbound `/v1/responses` turn and the model invocation: turn lifecycle, context admission, prompt assembly, history windowing, retrieval scoping, and multi-step/long-running turn progress. It does not cover model selection strategy, response quality post-processing, or UI (those are later phases).

## In Scope
- A `TurnPipeline` orchestrator in `LeanKernel.Logic` that sequences: persist inbound turn, assemble gated context, shape history, invoke the agent, persist assistant turn, emit turn events.
- A context gatekeeper implementing deny-by-default admission with an explicit token/character budget, mirroring source `ContextGatekeeper` + `PromptAssembler`.
- Deterministic history shaping and compaction (`HistoryShaper`, `HistoryCompactionStrategy`, `ConversationCompactor` equivalents) integrated with the existing `DbChatHistoryProvider`.
- Scoped knowledge/memory retrieval merged into context via a retrieval scope policy that respects tenant/user/channel partitioning.
- Long-running task support: a turn progress broker for incremental status directives plus a continuation path for turns that exceed a single model call.
- Configuration for budgets, history window, compaction thresholds, and retrieval limits under existing config sections (`Agents`, `OpenAI`).
- Unit and integration coverage for admission, budget enforcement, compaction determinism, and continuation.

## Out of Scope
- Model routing/escalation, shadow routing, quality gates, response enhancement (Phase 04).
- New tools or tool categories (Phase 05).
- Channels, learning, scheduler, diagnostics UI, or Blazor UI.
- Redesign of the 5W1H memory pipeline internals.

## Entry Criteria
- Rebuild builds on the current MAF/OpenAI package set with the `leankernel` named agent operational.
- `DbChatHistoryProvider`, `MemoryProvider`, and identity partitioning are the integration points and are stable.
- Source references captured as behavioral targets, not code-copy: `src/LeanKernel.Agents/TurnPipeline.cs`, `AgentRuntime.cs`, `ContinuationTurnPipeline.cs`, `TurnProgressBroker.cs`; `src/LeanKernel.Context/ContextGatekeeper.cs`, `PromptAssembler.cs`, `ConversationHistoryAssembler.cs`, `History/HistoryShaper.cs`, `History/HistoryCompactionStrategy.cs`, `History/ConversationCompactor.cs`, `Retrieval/ScopedKnowledgeService.cs`, `Retrieval/RetrievalScopePolicy.cs`.

## Exit Criteria
Turns execute through the explicit pipeline with deny-by-default context admission and enforced budgets, history is deterministically shaped/compacted, scoped retrieval is merged into the prompt, and long-running turns can emit progress and continue. See `exit-criteria.md`.

## Roles
- Owner: Rebuild maintainer
- Reviewer: Separate agent session / model review
- Approver: Repository owner
