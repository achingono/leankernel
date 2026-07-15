# Phase 17 Exit Criteria

## Gate Checklist
- [ ] Every persisted assistant turn carries structured telemetry: served model, provider, model_id/api_base, prompt/completion/total tokens, response cost + currency.
- [ ] Telemetry is captured from the MAF `ChatResponse` (model/usage) and LiteLLM cost/provider metadata, and reconciles with the proxy route log via a correlation id.
- [ ] Cost and token usage can be aggregated accurately per session, user, tenant, model, provider, and day.
- [ ] A deterministic, PII-aware labeled export exists for model-grouping / failover / cost-profile tuning (Phase 07 consumable).
- [ ] Missing or partial metadata degrades gracefully without dropping the turn.
- [ ] Telemetry is correctly partition-scoped (no cross-tenant/user/channel leakage).
- [ ] Configuration (enable/disable, currency, retention) validated at startup.
- [ ] Unit + integration tests cover capture, persistence, aggregation, correlation, resilience, and isolation.

## Approval Table

| Role | Name | Status | Notes |
| --- | --- | --- | --- |
| Owner | | Pending | |
| Reviewer | | Pending | |
| Approver | | Pending | |
