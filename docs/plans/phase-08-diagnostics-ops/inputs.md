# Phase 08 Inputs

## Required Inputs

| Input | Source | Owner |
|---|---|---|
| EF persistence context | `src/Common/LeanKernel.Data/EntityContext.cs` | Rebuild maintainer |
| Existing health checks | `src/Services/LeanKernel.Gateway/HealthChecks/*` | Rebuild maintainer |
| Runtime emit points | Phase 03/04/05/07 signals | Rebuild maintainer |
| Source diagnostics | `~/source/repos/leankernel/src/LeanKernel.Diagnostics/*` | Reviewer |
| Source diagnostics persistence | `~/source/repos/leankernel/src/LeanKernel.Persistence/{PostgresDiagnosticsSink,Entities/DiagnosticEntryEntity}.cs`, `Tracing/DbCommandActivityInterceptor.cs` | Reviewer |
| Source gateway middleware | `~/source/repos/leankernel/src/LeanKernel.Gateway/Middleware/*`, `Auth/ForwardedAuthHandler.cs` | Reviewer |
| Existing auth wiring | `src/Services/LeanKernel.Gateway/Programs.cs` (JWT/forwarded headers/CORS) | Rebuild maintainer |

## Optional Inputs
- Source PRDs: `phase-2-context-diagnostics-api-prd.md`, `phase-3-production-hardening-prd.md`, `budget-guardrails-fallback-prd.md`, `engine-health-recovery-prd.md`, `run-replay-provenance-prd.md`.

## Input Validation Checklist
- [ ] All required inputs are current (not from a superseded version)
- [ ] No required input is missing or in draft state
- [ ] Emit points from prior phases identified for instrumentation
