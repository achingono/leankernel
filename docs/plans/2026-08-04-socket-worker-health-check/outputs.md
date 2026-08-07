# Phase 01 Outputs

## Mandatory Outputs

| Output | Description | Format |
|---|---|---|
| `SignalSettings` extensions & validation | Worker health check threshold configuration properties with options validation (`Unhealthy > Degraded > (EffectiveReceiveDeadline + ReconnectDelay)`, `UnhealthyError > ConsecutiveError >= 1`) | C# class properties in `Settings.cs` & options validation |
| `SocketWorkerHealthState` record | Immutable per-account worker state: Account, State (enum), LastSuccessfulReceiveUtc (DateTime?), LastWorkerLoopTickUtc (DateTime?), StartedUtc (DateTime), ConsecutiveErrors, LastErrorUtc (DateTime?) | C# `record` in `HealthChecks/SocketWorkerHealthState.cs` |
| `ISocketWorkerHealthProvider` interface | Contract for exposing worker states, initial discovery flag, startup timestamp, and atomic manager state snapshot to the health check | C# interface in `HealthChecks/ISocketWorkerHealthProvider.cs` |
| `SocketTransportClient` instrumentation | Thread-safe progress tracking via private `AccountWorkerHealthTracker` class, account discovery result handling, and `ISocketWorkerHealthProvider` implementation | Modified `Clients/SocketTransportClient.cs` |
| `SocketWorkerHealthCheck` | Health check evaluating worker progress, startup timeouts, error thresholds, and manager liveness against thresholds | C# class in `HealthChecks/SocketWorkerHealthCheck.cs` |
| Health check & options registration | Added to DI, options validation, and health checks pipeline in `Program.cs` | Modified `Program.cs` |
| `Constants.Healthchecks.SocketWorker` | Constant for health check name/tag | Modified `Constants.cs` |
| Unit tests | Coverage for health check decision tree, progress tracking, manager failure detection, discovery resilience, and options validation | xUnit tests in `test/LeanKernel.Tests.Unit/Signal/SocketWorkerHealthCheckTests.cs` |
| Tool adapter coverage tests | Covers `ToolDefinitionAIToolAdapter` schema normalization, description formatting, no-handler, and failure-result branches (closes Sonar leak-period `new_coverage` gap) | xUnit tests in `test/LeanKernel.Tests.Unit/Tools/ToolDefinitionAIToolAdapterTests.cs` |
| Documentation | Architecture, features, and operations docs updated | Markdown in `docs/` |

## Optional Outputs
- Integration test verifying end-to-end health check behavior
- Example health check JSON response in documentation

## Output Quality Checklist
- [x] All mandatory outputs specified and structured
- [x] All outputs reviewed before gate
- [x] Evidence log updated with output references
- [x] Code coverage target set to ≥ 80% for new files
- [x] SonarQube scan requirement integrated (no Blocker/Critical/Major)
- [x] Deep review process specified and issues addressed
