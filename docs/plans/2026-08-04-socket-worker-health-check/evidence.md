# Phase 01 Evidence

## Evidence Log

| Item | Reference | Notes |
|---|---|---|
| Current health check registration | `src/Terminals/LeanKernel.Channels.Signal/Program.cs` — `AddHealthChecks()` block | Shows existing database, gateway, signal-api checks |
| SignalApiHealthCheck reference | `src/Terminals/LeanKernel.Channels.Signal/HealthChecks/SignalApiHealthCheck.cs` | Pattern for new health check |
| SocketTransportClient worker loop | `src/Terminals/LeanKernel.Channels.Signal/Clients/SocketTransportClient.cs` — `RunAccountWorkerAsync` method | Where progress tracking and atomic metric updates are added |
| Manager loop and worker management | `src/Terminals/LeanKernel.Channels.Signal/Clients/SocketTransportClient.cs` — `ManageWorkersAsync` / `RefreshWorkersAsync` methods | Where manager liveness tracking (`IsManagerRunning`) and resilient account discovery handling are added |
| Deep-review remediation (C1/M1/M2/M3/S1/S2/S3/S4) | `src/Terminals/LeanKernel.Channels.Signal/Clients/SocketTransportClient.cs`, `src/Terminals/LeanKernel.Channels.Signal/HealthChecks/SocketWorkerHealthCheck.cs`, `src/Terminals/LeanKernel.Channels.Signal/Extensions/SignalServiceCollectionExtensions.cs`, `src/Terminals/LeanKernel.Channels.Signal/HealthChecks/ISocketWorkerHealthProvider.cs`, `test/LeanKernel.Tests.Unit/Signal/SocketWorkerHealthCheckTests.cs` | Added manager self-heal, widened discovery exception handling, JSON-error backoff, empty-discovery debounce, atomic manager snapshot API, discovery startup grace degradation, fault-recovery success-threshold semantics, and validation hardening with expanded tests |
| SignalSettings configuration & options validation | `src/Terminals/LeanKernel.Channels.Signal/Settings.cs` and `Program.cs` | Where threshold settings and options validation (`AddOptions<SignalSettings>().Validate(...)`) are added |
| Constants.Healthchecks | `src/Common/LeanKernel.Core/Constants.cs` — `Healthchecks` static class | Where `SocketWorker = "socket-worker"` constant is added |
| Existing unit tests | `test/LeanKernel.Tests.Unit/Signal/SignalChannelTransportTests.cs` | Pattern for new tests covering `SocketWorkerHealthCheck` and `SocketTransportClient` tracking |
| Sonar gate remediation | `test/LeanKernel.Tests.Unit/Tools/ToolDefinitionAIToolAdapterTests.cs` (+9 tests) | `new_coverage` gap (58.8% vs 80% threshold) closed by covering `ToolDefinitionAIToolAdapter.cs` (24 uncovered lines → 0; adapter now 100% line-covered) |
| SonarQube gate result | `http://localhost:9000/dashboard?id=LeanKernel` — analysis 2026-08-07 | **PASSED**: `new_coverage` 97.1% (threshold 80%), `new_lines_to_cover` 62, `new_uncovered_lines` 0, ratings all 1.0, `new_duplicated_lines_density` 0.0; leak period `PREVIOUS_VERSION` since 2026-08-03T19:07:00+0000 |
| Full test suite | `scripts/quality/test-coverage.sh` | 1046/1046 unit tests pass; repo line coverage 81.12% (threshold 80%); coverage gate passed |
