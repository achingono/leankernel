# Phase 17 Outputs

## Mandatory Outputs

| Output | Description | Format |
| --- | --- | --- |
| Turn telemetry schema | Structured model/provider/usage/cost record + schema version | C# type + JSON |
| Persistence | `TurnEntity.Metadata` JSON and/or typed columns / `TurnUsageEntity` + migration | C# + EF migration |
| Capture path | `DbChatHistoryProvider` reads `ChatResponse` model/usage + LiteLLM cost/provider | C# source |
| Proxy correlation | Correlation id propagated gateway → LiteLLM route log; documented join key | C# + Python config |
| Cost aggregation surface | Queries per session/user/tenant/model/provider/day | C# source |
| Learning export | Deterministic, PII-aware labeled dataset for grouping/failover/cost profiles | C# source + schema |
| Configuration + validation | Enable/disable, currency, retain-raw-metadata | C# + appsettings |
| Tests | Capture, persistence, aggregation, correlation, resilience, isolation | xUnit projects |
| Documentation | Telemetry schema + cost model docs | Markdown |

## Optional Outputs
- Diagnostics/API endpoint exposing per-session cost (aligns with Phase 08).

## Output Quality Checklist
- [ ] All mandatory outputs produced
- [ ] Cost figures validated against LiteLLM-reported cost
- [ ] Evidence log updated with output references
