# Phase 17 Model Telemetry In Chat History

## Companion Documents
- [Inputs](inputs.md)
- [Activities](activities.md)
- [Outputs](outputs.md)
- [Exit Criteria](exit-criteria.md)
- [Risk Register](risk-register.md)
- [Evidence](evidence.md)

## Objective
Capture the LiteLLM model-routing telemetry for every assistant turn — selected/served model name, provider, model_id/api_base, token usage, and response cost — and persist it alongside the chat history. Today `DbChatHistoryProvider` stores only role/content/timestamp and discards the model and usage information available on the MAF `ChatResponse` and in LiteLLM's response metadata. This phase makes each persisted assistant turn carry accurate, structured model/cost telemetry so the system can (1) compute accurate per-user/per-tenant/per-session budget and cost figures, and (2) build a labeled dataset for tuning model grouping, failover order, and cost profiles as part of supervised learning / self-improvement.

## Scope
This phase adds structured model-and-cost telemetry to persisted assistant turns and a queryable aggregation path for cost reporting. It captures telemetry from the LiteLLM/OpenAI-compatible response (model, provider, usage, cost) and correlates the proxy-side route log with the chat session. It does not implement routing decisions, spend enforcement, or the learning training loop themselves — it produces the telemetry those consumers depend on (Phase 04 model intelligence, Phase 07 learning, Phase 08 spend guard).

## In Scope
- A structured turn-telemetry representation persisted with each assistant turn: requested model, served/response model, provider (normalized), model_id, api_base, prompt/completion/total tokens, response cost, currency, latency, and a correlation id.
- Capture path in `DbChatHistoryProvider.StoreChatHistoryAsync`/`ToTurnEntity` (and the agent invocation) to read `ChatResponse.ModelId`, `ChatResponse.Usage` (`UsageDetails`), and LiteLLM cost/provider metadata (`x-litellm-response-cost` header, `_hidden_params`, `custom_llm_provider`).
- Correlation between the gateway turn and the proxy-side route event already emitted by `leankernel_litellm_callbacks.py` (e.g., propagate/read a request/correlation id) so provider/model_id/api_base can be reconciled.
- Persistence design: reuse `TurnEntity.Metadata` (JSON) for the per-turn telemetry and/or add typed columns / a dedicated `TurnUsageEntity` for indexed cost aggregation; EF migration.
- A cost/usage aggregation query surface (per session/user/tenant/model/provider/day) for accurate budget figures, feeding Phase 08 spend guard and reporting.
- An export/label shape suitable for Phase 07 learning (grouping, failover, cost-profile tuning) — deterministic, PII-aware.
- Configuration for enable/disable, cost currency, and whether raw metadata is retained; startup validation.
- Tests covering capture, persistence, aggregation correctness, correlation, missing-metadata resilience, and partitioning safety.

## Out of Scope
- Model routing/escalation and failover decision logic (Phase 04) — this phase only records what was chosen and what it cost.
- Spend enforcement/guardrail actions (Phase 08) — this phase feeds it.
- Training/fine-tuning execution (Phase 07) — this phase produces the labeled dataset.
- Streaming-partial cost attribution beyond the final aggregated usage on stream completion.

## Entry Criteria
- Chat history persistence exists (`DbChatHistoryProvider`) with a JSON `Metadata` field on `TurnEntity`.
- LiteLLM proxy is the model gateway and already emits route events (`config/litellm/leankernel_litellm_callbacks.py`).
- The OpenAI-compatible client returns model/usage; LiteLLM surfaces cost via header/hidden params.

## Exit Criteria
Every persisted assistant turn carries structured, accurate model/provider/usage/cost telemetry; cost can be aggregated per session/user/tenant/model/provider; and a labeled export exists for model-grouping/failover/cost-profile tuning. See `exit-criteria.md`.

## Roles
- Owner: Rebuild maintainer
- Reviewer: Separate agent session / model review
- Approver: Repository owner
