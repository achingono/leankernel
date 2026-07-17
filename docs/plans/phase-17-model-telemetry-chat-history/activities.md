# Phase 17 Activities

## Step-By-Step Activities

### Capture Path (Steps 1-5)

1. Confirm current behavior: `ToTurnEntity` (`DbChatHistoryProvider.cs:215-232`) persists only role/content/author/timestamp; model, provider, usage, and cost from the `ChatResponse`/LiteLLM metadata are discarded.
2. Define the turn-telemetry schema: requested model, served/response model, normalized provider, model_id, api_base, prompt/completion/total tokens, response cost + currency, latency, and a schema version.
3. Implement persistence as a dedicated `TurnTelemetryEntity` (one-to-one with `TurnEntity`) so telemetry does not collide with idempotency metadata; add the EF migration and indexes.
4. Capture usage/model on the invocation path: read `ChatResponse.ModelId` and `ChatResponse.Usage` (`UsageDetails.InputTokenCount`/`OutputTokenCount`/`TotalTokenCount`); handle streaming by aggregating final usage on completion.
5. Capture cost/provider from LiteLLM: read the response cost header (`x-litellm-response-cost`) and/or `_hidden_params` (`response_cost`, `custom_llm_provider`, `model_id`, `api_base`); normalize provider consistently with the proxy callback (`normalize_provider`).
6. Persist telemetry when storing assistant turns in `StoreChatHistoryAsync`; ensure it is written only for assistant/response turns and is partition-scoped.

### Aggregation and Reporting (Steps 7-9) — Primary Value

7. Build the per-dimension aggregation surface: cost/token rollups by model, provider, user, session, day, and tenant. Each query groups by the dimension, sums cost and tokens, counts turns, and computes avg cost per turn and avg tokens per turn.
8. Build cross-dimension drill-down queries: model × day (investigate cost spikes), user × model (power-user analysis). These combine two GROUP BY dimensions.
9. Build summary stats: total cost, total tokens, unique users, unique sessions, avg cost per turn, avg tokens per turn for a date range. This is the "dashboard headline" query.
10. Build model efficiency metrics: cost per 1k tokens, completion ratio (completion / total tokens), avg prompt tokens per turn, avg completion tokens per turn — per model. This drives model selection decisions.
11. Build top-N queries: highest-cost users and models within a date range, for quick triage.
12. Produce a labeled export shape for Phase 07 (model grouping, failover order, cost profiles) — deterministic ordering, PII-free field selection.

### Configuration and Quality (Steps 6, 10-12)

13. Add configuration (enable/disable, currency, retain-raw-metadata) and startup validation.
14. Add tests: capture correctness, persistence round-trip, aggregation math correctness, partition isolation, missing/partial metadata resilience, export PII-freedom.
15. Document the telemetry schema, aggregation API, and cost model in `docs/features/` and `docs/operations/`.

## Review Focus
- Cost and token figures are accurate and reconcile with LiteLLM's own reported cost.
- Aggregation queries return correct results across all dimensions (model, provider, user, session, day, tenant).
- Cross-dimension drill-downs correctly combine GROUP BY dimensions.
- All aggregation queries are partition-scoped — no cross-tenant/user/channel data leakage.
- Missing/partial metadata (no cost header, provider unknown) degrades gracefully without dropping the turn.
- Export is deterministic and free of raw prompt PII unless explicitly configured.
