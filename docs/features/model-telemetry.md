# Model Telemetry

LeanKernel persists structured telemetry for assistant turns to support cost reporting and
model-performance analysis.

## Capture Path

- `TelemetryCapturingChatClient` wraps the primary `IChatClient` and captures:
  - requested model (`ChatOptions.ModelId`)
  - served model (`ChatResponse.ModelId`)
  - token usage (`ChatResponse.Usage`)
  - response cost from `x-litellm-response-cost` (when surfaced)
  - estimated cost from configured per-model token rates when direct cost is missing
- Captured data is stored in request scope by `ITurnTelemetryCollector`.
- `DbChatHistoryProvider` consumes that telemetry while persisting assistant turns.

## Persistence

- Telemetry is stored in `TurnTelemetryEntity` (`TurnTelemetry` table), one-to-one with
  `TurnEntity` by `TurnId`.
- `TurnEntity.Metadata` remains reserved for idempotency behavior and is not reused.
- Key indexes:
  - `TurnId` (unique)
  - `CapturedAt`
  - `Provider`
  - `ServedModel`

## Aggregation and Export

- `ITelemetryAggregationService` exposes permit-scoped rollups by model, provider, user,
  session, day, tenant, plus summary, efficiency, and top-N queries.
- `ITelemetryExportService` exports a deterministic, PII-free record shape:
  timestamp, requested/served model, provider, prompt/completion tokens, response cost,
  estimated-cost flag.

## Configuration

`Agents:Telemetry` controls telemetry behavior:

- `Enabled`
- `Currency`
- `RetainRawMetadata`
- `UseCostEstimate`
- `CostEstimate:CostPer1kInputTokens`
- `CostEstimate:CostPer1kOutputTokens`

Code anchors:

- [`../../src/Common/LeanKernel.Logic/Telemetry/TelemetryCapturingChatClient.cs`](../../src/Common/LeanKernel.Logic/Telemetry/TelemetryCapturingChatClient.cs)
- [`../../src/Common/LeanKernel.Logic/Providers/DbChatHistoryProvider.cs`](../../src/Common/LeanKernel.Logic/Providers/DbChatHistoryProvider.cs)
- [`../../src/Common/LeanKernel.Core/Entities/TurnTelemetryEntity.cs`](../../src/Common/LeanKernel.Core/Entities/TurnTelemetryEntity.cs)
- [`../../src/Common/LeanKernel.Logic/Telemetry/TelemetryAggregationService.cs`](../../src/Common/LeanKernel.Logic/Telemetry/TelemetryAggregationService.cs)
- [`../../src/Common/LeanKernel.Logic/Telemetry/TelemetryExportService.cs`](../../src/Common/LeanKernel.Logic/Telemetry/TelemetryExportService.cs)
