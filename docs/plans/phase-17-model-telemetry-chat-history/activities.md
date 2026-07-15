# Phase 17 Activities

## Step-By-Step Activities
1. Confirm current behavior: `ToTurnEntity` (`DbChatHistoryProvider.cs:215-232`) persists only role/content/author/timestamp; model, provider, usage, and cost from the `ChatResponse`/LiteLLM metadata are discarded.
2. Define the turn-telemetry schema: requested model, served/response model, normalized provider, model_id, api_base, prompt/completion/total tokens, response cost + currency, latency, correlation id, and a schema version.
3. Decide persistence shape: store the telemetry JSON in `TurnEntity.Metadata` and/or add typed columns or a dedicated `TurnUsageEntity` for indexed cost aggregation; add the EF migration.
4. Capture usage/model on the invocation path: read `ChatResponse.ModelId` and `ChatResponse.Usage` (`UsageDetails.InputTokenCount`/`OutputTokenCount`/`TotalTokenCount`); handle streaming by aggregating final usage on completion.
5. Capture cost/provider from LiteLLM: read the response cost header (`x-litellm-response-cost`) and/or `_hidden_params` (`response_cost`, `custom_llm_provider`, `model_id`, `api_base`); normalize provider consistently with the proxy callback (`normalize_provider`).
6. Correlate with the proxy route log: propagate a correlation/request id from the gateway so the gateway turn and the proxy-side `record_route` event reconcile; document the join key.
7. Persist telemetry when storing assistant turns in `StoreChatHistoryAsync`; ensure it is written only for assistant/response turns and is partition-scoped.
8. Build the aggregation surface: queries for cost/tokens per session, user, tenant, model, provider, and day for accurate budget figures.
9. Produce a labeled export shape for Phase 07 (model grouping, failover order, cost profiles) — deterministic ordering, PII-aware field selection.
10. Add configuration (enable/disable, currency, retain-raw-metadata) and startup validation.
11. Add tests: capture correctness, persistence round-trip, aggregation math, correlation, missing/partial metadata resilience, and tenant/user/channel isolation.
12. Document the telemetry schema and cost model in `docs/features/` and `docs/operations/`.

## Review Focus
- Cost and token figures are accurate and reconcile with LiteLLM's own reported cost.
- Telemetry is written only on assistant turns and correctly partition-scoped (no cross-tenant leakage).
- Missing/partial metadata (no cost header, provider unknown) degrades gracefully without dropping the turn.
- Aggregation queries are correct and indexed for reporting/enforcement use.
- Export is deterministic and free of raw prompt PII unless explicitly configured.
