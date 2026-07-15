# Phase 08 Evidence

## Evidence Log

| Item | Reference | Notes |
| --- | --- | --- |
| Source diagnostics | `~/source/repos/leankernel/src/LeanKernel.Diagnostics/{DiagnosticsCollector,ContextDiagnosticsService,LeanKernelMetrics,LeanKernelLogEnricher}.cs` | Behavioral reference |
| Source health tracking | `~/source/repos/leankernel/src/LeanKernel.Diagnostics/Health/{ProviderHealthTracker,ProviderHealthCheck}.cs` | Behavioral reference |
| Source spend guard | `~/source/repos/leankernel/src/LeanKernel.Diagnostics/SpendGuard/{SpendTracker,SpendGuardService}.cs` | Behavioral reference |
| Source diagnostics persistence | `~/source/repos/leankernel/src/LeanKernel.Persistence/{PostgresDiagnosticsSink,Entities/DiagnosticEntryEntity}.cs`, `Tracing/DbCommandActivityInterceptor.cs` | Behavioral reference |
| Source middleware | `~/source/repos/leankernel/src/LeanKernel.Gateway/Middleware/{CorrelationIdMiddleware,RateLimitingMiddleware}.cs` | Behavioral reference |
| Rebuild health/auth | `src/Services/LeanKernel.Gateway/HealthChecks/*`, `Programs.cs` | Integration point |
