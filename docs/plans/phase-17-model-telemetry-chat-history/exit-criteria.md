# Phase 17 Exit Criteria

## Gate Checklist
- [x] Every persisted assistant turn carries structured telemetry: served model, provider, model_id/api_base, prompt/completion/total tokens, response cost + currency.
- [x] Telemetry is captured from the MAF `ChatResponse` (model/usage) and LiteLLM cost/provider metadata.
- [x] Cost and token usage can be aggregated accurately by model, provider, user, session, tenant, and day.
- [x] Aggregation queries return correct results: per-model cost breakdown, per-provider cost breakdown, per-user cost leaderboard, daily spend trend, tenant cost allocation, model efficiency (cost per 1k tokens, completion ratio).
- [x] Cross-dimension drill-down queries work (model × day, user × model) for investigating cost spikes.
- [x] Top-N queries identify highest-cost users and models within a date range.
- [x] All aggregation queries are partition-scoped by `IPermit` — no cross-tenant/user/channel leakage.
- [x] A deterministic, PII-aware labeled export exists for model-grouping / failover / cost-profile tuning (Phase 07 consumable).
- [x] Missing or partial metadata degrades gracefully without dropping the turn; `CostIsEstimated` flag distinguishes reported vs estimated cost.
- [ ] Configuration (enable/disable, currency, retain-raw-metadata) validated at startup.
- [x] Unit + integration tests cover capture, persistence, aggregation correctness, partition isolation, and graceful degradation.

## Approval Table

| Role | Name | Status | Notes |
| --- | --- | --- | --- |
| Owner | | Pending | |
| Reviewer | | Pending | |
| Approver | | Pending | |
