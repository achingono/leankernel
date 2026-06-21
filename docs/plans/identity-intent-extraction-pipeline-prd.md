# Identity Intent Extraction Pipeline PRD

## Goal

Codify user instructions about agent behavior (for example, autonomy and permission posture) into durable identity pages automatically, using the existing async post-turn pipeline.

## Scope

- Extend current identity writeback behavior to include user-intent extraction.
- Keep updates safe with allowlists, confidence thresholds, and conflict checks.
- Reuse existing LeanKernel components wherever possible.

## Existing Building Blocks to Reuse

- Async turn event pipeline:
  - `TurnPipeline` emits `TurnEvent` after each response.
  - `TurnEventQueue` and `LearningBackgroundWorker` process events asynchronously.
- Identity persistence model:
  - `IdentityConfig.AllowedIdentityFields` allowlist.
  - `IdentityPageSerializer` for structured identity page frontmatter.
  - `KnowledgePageUpdateCoordinator` for serialized page writes.
- Current identity writeback:
  - `IdentityUpdateProjector` already updates `identity-user-default`.

## Proposed Architecture

Add a new learning step: `IdentityIntentExtractionStep`.

### Pipeline placement

- Register as `ILearningStep` in `AddLeanKernelLearning`.
- Suggested order: after fact extraction, before engagement tracking.
- Runs inside existing `LearningBackgroundWorker` (already async + queued).

### Input

- Consume `TurnEvent.UserMessage`, `TurnEvent.AssistantResponse`, `TurnEvent.Context`.
- Include a short recent history excerpt for disambiguation.

### Extraction model

- Use LiteLLM with a small model (`small` tier by default).
- New config key: `Learning.IntentExtractionModel` (default `small`).
- Prompt output must be strict JSON matching schema below.

### Output schema

```json
{
  "updates": [
    {
      "field": "autonomy_level",
      "value": "proactive-by-default-unless-destructive",
      "confidence": 0.0,
      "reason": "User explicitly asked for proactive behavior with safety guardrails"
    }
  ],
  "hasBehaviorIntent": true
}
```

## Mapping and Persistence

- Only persist fields in `IdentityConfig.AllowedIdentityFields`.
- Primary target page: `identity-user-default`.
- Optional second target (phase 2): managed section in `identity-agent-main` with synthesized operating policy.
- Use `IdentityPageSerializer.ParseDocument/SerializeDocument` for stable writes.
- Use `KnowledgePageUpdateCoordinator.ExecuteAsync` per page key to prevent races.

## Safety and Conflict Policy

- Minimum confidence threshold (new config): `Learning.IntentExtractionMinConfidence` (default `0.72`).
- If existing field confidence is higher, do not overwrite; log diagnostic conflict.
- Keep source metadata as `source: user_intent_extraction`.
- Keep `last_updated` timestamp from extraction event time.
- Add hard normalization for autonomy values to a compact enum-like set:
  - `low`, `medium`, `high`, `proactive-by-default-unless-destructive`.

## Async and Reliability Guarantees

- Non-blocking for chat path: extraction only runs in background worker.
- Queue backpressure already handled via bounded channel.
- Step failures are recoverable and logged; do not fail user turn.
- Idempotency: if normalized value unchanged, skip write.

## Diagnostics and Observability

- Add metrics:
  - `identity_intent_events_total`
  - `identity_intent_updates_applied_total`
  - `identity_intent_conflicts_total`
  - `identity_intent_low_confidence_dropped_total`
- Add diagnostics payload for each applied/dropped update including:
  - session id, turn id, field, confidence, decision.

## Configuration Additions

Add to `LearningConfig`:

- `bool IntentExtractionEnabled = true`
- `string IntentExtractionModel = "small"`
- `double IntentExtractionTemperature = 0.0`
- `double IntentExtractionMinConfidence = 0.72`
- `int IntentExtractionMaxUpdatesPerTurn = 3`

## Rollout Plan

1. Implement step with `IntentExtractionEnabled=false` by default.
2. Ship to staging and capture diagnostics only (dry-run mode optional).
3. Enable writes for `autonomy_level` only.
4. Expand to additional behavior fields after validation.

## Test Plan

- Unit tests for:
  - JSON parsing/validation failures.
  - Allowlist enforcement.
  - Confidence threshold behavior.
  - Conflict resolution (higher-confidence existing value wins).
  - No-op when no behavior intent present.
- Integration tests:
  - End-to-end turn event causes `identity-user-default` update.
  - Updated identity is loaded into prompt context in subsequent turn.

## Non-Goals (Phase 1)

- Real-time in-request identity mutation before current response returns.
- Free-form updates outside allowlisted fields.
- Automatic mutation of arbitrary wiki pages.
