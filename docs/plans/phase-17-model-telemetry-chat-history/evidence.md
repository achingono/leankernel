# Phase 17 Evidence

## Evidence Log

| Item | Reference | Notes |
| --- | --- | --- |
| Telemetry schema and capture contract | `src/Common/LeanKernel.Logic/Telemetry/TurnTelemetry.cs`, `src/Common/LeanKernel.Logic/Telemetry/ITurnTelemetryCollector.cs` | Structured requested/served model, provider, tokens, cost, latency fields |
| Chat client capture path | `src/Common/LeanKernel.Logic/Telemetry/TelemetryCapturingChatClient.cs` | Captures model/usage/cost and writes into scoped collector |
| Persistence path | `src/Common/LeanKernel.Logic/Providers/DbChatHistoryProvider.cs` | Consumes collector and persists `TurnTelemetryEntity` for assistant turns |
| Telemetry storage entity | `src/Common/LeanKernel.Core/Entities/TurnTelemetryEntity.cs` | One-to-one with `TurnEntity`, unique `TurnId` |
| Schema migration | `src/Common/LeanKernel.Data/Migrations/20260716223823_AddTurnTelemetry.cs` | Adds telemetry table and indexes |
| Query/rollup surface | `src/Common/LeanKernel.Logic/Telemetry/TelemetryAggregationService.cs` | Cost and token rollups by model/provider/user/session/day/tenant |
| Labeled export | `src/Common/LeanKernel.Logic/Telemetry/TelemetryExportService.cs` | Deterministic PII-aware export for Phase 07 consumers |
| DI wiring | `src/Common/LeanKernel.Logic/Extensions/IServiceCollectionExtensions.cs` | Registers telemetry services and wraps `IChatClient` when enabled |
| Configuration shape | `src/Common/LeanKernel.Logic/Configuration/TelemetrySettings.cs`, `docs/configuration/appsettings-reference.md` | `Agents:Telemetry` settings documented and bound |
| Remaining gap | `src/Common/LeanKernel.Logic/Extensions/IServiceCollectionExtensions.cs` | `TelemetrySettings` currently bound without `ValidateOnStart` gate |

## Implemented Output Targets

- `src/Common/LeanKernel.Logic/Telemetry/TurnTelemetry.cs`
- `src/Common/LeanKernel.Core/Entities/TurnTelemetryEntity.cs`
- `src/Common/LeanKernel.Logic/Telemetry/ITurnTelemetryCollector.cs`
- `src/Common/LeanKernel.Logic/Telemetry/TelemetryCapturingChatClient.cs`
- `src/Common/LeanKernel.Logic/Telemetry/TelemetryAggregationService.cs`
- `src/Common/LeanKernel.Logic/Telemetry/TelemetryExportService.cs`
- `src/Common/LeanKernel.Logic/Configuration/TelemetrySettings.cs`
- `src/Common/LeanKernel.Data/Migrations/20260716223823_AddTurnTelemetry.cs`
- `test/LeanKernel.Tests.Unit/Telemetry/`

## Verification

- `dotnet test test/LeanKernel.Tests.Unit/LeanKernel.Tests.Unit.csproj`
