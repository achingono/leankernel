# Phase 17 Outputs

## Mandatory Outputs

| Output | Description | Format |
| --- | --- | --- |
| Turn telemetry schema | Structured model/provider/usage/cost record + schema version | C# type + JSON |
| Persistence | Dedicated `TurnTelemetryEntity` (1:1 with `TurnEntity`) + migration + indexes | C# + EF migration |
| Capture path | `DbChatHistoryProvider` reads `ChatResponse` model/usage + LiteLLM cost/provider | C# source |
| **Aggregation service** | **Per-dimension cost/token rollups: by model, provider, user, session, day, tenant** | **C# source** |
| **Cross-dimension drill-down** | **Model × day, user × model queries for investigating cost spikes** | **C# source** |
| **Summary stats** | **Total cost, total tokens, unique users, avg cost per turn for a date range** | **C# source** |
| **Model efficiency metrics** | **Cost per 1k tokens, completion ratio, avg tokens per turn — per model** | **C# source** |
| **Top-N queries** | **Highest-cost users and models within a date range** | **C# source** |
| Learning export | Deterministic, PII-aware labeled dataset for grouping/failover/cost profiles | C# source + schema |
| Grounding attribution labels | Evidence-class and groundedness telemetry on assistant turns | C# source + schema |
| Configuration + validation | Enable/disable, currency, retain-raw-metadata | C# + appsettings |
| Tests | Capture, persistence, aggregation correctness, partition isolation, resilience | xUnit projects |
| Documentation | Telemetry schema, aggregation API, cost model docs | Markdown |

## Optional Outputs
- Diagnostics/API endpoint exposing per-session cost (aligns with Phase 08).
- Real-time cost dashboard data feed (future Blazor UI consumption).

## Output Quality Checklist
- [ ] All mandatory outputs produced
- [ ] Aggregation queries return correct results verified by tests with known input data
- [ ] Cost figures validated against LiteLLM-reported cost
- [ ] All aggregation queries are partition-scoped (no cross-tenant leakage)
- [ ] Evidence log updated with output references
