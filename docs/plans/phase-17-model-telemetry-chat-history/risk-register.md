# Phase 17 Risk Register

## Risks

| ID | Risk | Impact | Mitigation | Status |
| --- | --- | --- | --- | --- |
| R1 | LiteLLM cost surface differs by version (header vs hidden params) or is absent | Inaccurate/missing cost figures | Confirm deployed surface; fall back to token-based cost estimate from model cost profile; flag estimated vs reported | Open |
| R2 | Streaming responses report usage only at completion | Under/over-counted tokens | Aggregate final usage on stream completion; test streaming path | Open |
| R3 | Proxy route-log correlation is deferred and provider/model_id can be partial on some turns | Incomplete reconciliation for provider diagnostics | Keep model/usage/cost capture independent of correlation; treat proxy correlation as follow-up work | Open |
| R4 | Telemetry written across partitions or leaks another tenant's data | Privacy/cost-attribution errors | Reuse `IPermit` scoping; isolation tests | Open |
| R5 | Raw prompt/response PII persisted in telemetry/export | Privacy breach | Telemetry stores metrics/model metadata only; export is PII-aware/opt-in | Open |
| R6 | Telemetry table growth degrades aggregation performance over time | Slow reports and higher storage costs | Add indexes for model/provider/day; add date-range filters; add summary tables if needed | Open |
| R7 | Cost aggregation double-counts retries/failovers | Wrong budget totals | Attribute cost per served attempt with attempt/correlation keys; dedupe in aggregation | Open |
| R8 | Evidence-class labeling is inconsistent across tools/providers | Noisy groundedness metrics | Define strict enum + fallback mapping and validate at capture boundary | Open |

## Open Decisions
- Currency handling and whether to store both reported and token-estimated cost.
- Whether the learning export lives in this phase or is deferred to Phase 07 consuming this data.
- Whether groundedness status should be inferred in telemetry capture path or computed in downstream analytics.
