# Phase 08 Inputs

## Required Inputs

| Input | Source | Owner |
|---|---|---|
| LiteLLM proxy configuration | `proxy_config.yaml` (deploy-time) | Operations |
| LiteLLM proxy admin API | `/health/services`, `/spend`, `/metrics` endpoints | Operations |
| EF persistence context | `src/Common/LeanKernel.Data/EntityContext.cs` | Rebuild maintainer |
| Existing health checks | `src/Services/LeanKernel.Gateway/HealthChecks/*` | Rebuild maintainer |
| Runtime emit points | Phase 03/04/05/07 signals | Rebuild maintainer |
| Existing auth wiring | `src/Services/LeanKernel.Gateway/Programs.cs` (JWT/forwarded headers/CORS) | Rebuild maintainer |

## Optional Inputs
- Dream run telemetry/report outputs from Phase 07 scheduler integration.
- Truth lifecycle conflict/canonicalization signals from Phase 22.

## Input Validation Checklist
- [ ] LiteLLM proxy address is reachable and `/health/services` responds
- [ ] LiteLLM `/metrics` endpoint is exposed and Prometheus-compatible
- [ ] LiteLLM spend/budget configuration is loaded (or n/a for deploy)
- [ ] All required inputs are current (not from a superseded version)
- [ ] No required input is missing or in draft state
- [ ] Emit points from prior phases identified for instrumentation
- [ ] Ingest-to-enrich-to-Dream lifecycle correlation strategy defined
