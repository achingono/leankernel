# ADR 0003: Separate transcript sessions from durable agent runtime state

- Status: Accepted
- Date: 2026-07-13

## Context

During implementation, the repository ended up with two concepts that both looked like "sessions": the EF-backed transcript session used for chat history and the durable MAF checkpoint blob used to resume runtime state. This caused confusion in planning and code review.

The logs show explicit discussion about whether these should be the same entity, whether they should be directly related, and whether the naming was misleading.

## Decision

LeanKernel will keep transcript history and agent runtime checkpointing as separate persistence concepts.

- `SessionEntity` represents the durable transcript session for chat history.
- `TurnEntity` belongs to `SessionEntity`.
- `AgentStateEntity` represents serialized MAF runtime state for conversation resumption.

Related identifier domains remain explicit and distinct:

- `conversationId`: externally visible OpenAI conversation id
- `scopedConversationId`: internal isolation-scoped conversation key used by the MAF session store
- `chatSessionId`: persisted `SessionEntity.Id` used for transcript history

The former `AgentSessionEntity` name is replaced by `AgentStateEntity` to make the distinction explicit.

## Consequences

Positive:

- Transcript persistence and runtime checkpointing can evolve independently.
- Naming better matches semantics and avoids confusion with `SessionEntity`.
- MAF resume state does not need to inherit transcript lifecycle assumptions.

Tradeoffs:

- The system must explicitly map between conversation ids and transcript session ids.
- There is no single "session" table that explains every runtime concept.
- Developers need to understand two persistence layers when debugging conversation behavior.

## Evidence From Session Logs

- OpenCode session `ses_0abffac42ffeqKxiSWd1Vg90bD`, `2026-07-12`, "PRD implementation and architecture gap review"
  - Identified conflation between external `conversationId`, scoped internal ids, and persisted chat-session ids as a design defect that needed explicit separation.
- OpenCode session `ses_0a7203802ffeCCK07rFnwepiBK`, `2026-07-13`, "Configure gateway service health in docker-compose.yml"
  - Explained why `SessionEntity` and `AgentSessionEntity` had different lifecycles and identity shapes.
  - Recommended renaming `AgentSessionEntity` to `AgentStateEntity` because it stores a durable state checkpoint, not another transcript session.
  - Followed through with the rename and compose verification.
