# Phase 17 Risk Register

## Risks

| ID | Risk | Impact | Mitigation | Status |
| --- | --- | --- | --- | --- |
| R1 | LiteLLM cost surface differs by version (header vs hidden params) or is absent | Inaccurate/missing cost figures | Confirm deployed surface; fall back to token-based cost estimate from model cost profile; flag estimated vs reported | Open |
| R2 | Streaming responses report usage only at completion | Under/over-counted tokens | Aggregate final usage on stream completion; test streaming path | Open |
| R3 | Gateway turn and proxy route event fail to correlate | Provider/model_id unreconcilable | Propagate a correlation id header; document join key; tolerate partial data | Open |
| R4 | Telemetry written across partitions or leaks another tenant's data | Privacy/cost-attribution errors | Reuse `IPermit` scoping; isolation tests | Open |
| R5 | Raw prompt/response PII persisted in telemetry/export | Privacy breach | Telemetry stores metrics/model metadata only; export is PII-aware/opt-in | Open |
| R6 | Metadata JSON bloat degrades history read/write | Latency/storage | Keep telemetry compact; consider typed columns / `TurnUsageEntity` for aggregation | Open |
| R7 | Cost aggregation double-counts retries/failovers | Wrong budget totals | Attribute cost per served attempt with attempt/correlation keys; dedupe in aggregation | Open |

## Open Decisions
- Persist telemetry in `TurnEntity.Metadata` JSON vs typed columns vs a dedicated `TurnUsageEntity`.
- Currency handling and whether to store both reported and token-estimated cost.
- Whether the learning export lives in this phase or is deferred to Phase 07 consuming this data.
