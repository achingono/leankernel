# Phase 17 Model Telemetry — Implementation Plan

## Objective

Persist structured model/provider/token-usage/cost telemetry on every assistant turn so the
system can compute accurate per-user/per-tenant/per-session budget figures and produce a
labeled dataset for model grouping, failover order, and cost-profile tuning.

## Current State

- `TurnEntity` stores role/content/author/timestamp; model, usage, and cost are discarded.
- `DbChatHistoryProvider.StoreChatHistoryAsync` persists `InvokedContext.ResponseMessages`
  but does not read `ChatResponse.ModelId` or `ChatResponse.Usage`.
- MAF's `ChatClientAgent` handles `ChatResponse` internally; the .NET code never sees it
  on the main invocation path.
- LiteLLM proxy callback logs route events (provider, model_id, api_base, response_model)
  to a JSONL file but does not send this data back to the .NET application.
- No LLM usage/cost/token tracking exists anywhere in the C# codebase.

## Design Decisions

### Persistence: dedicated `TurnTelemetryEntity` table (chosen)

`TurnEntity.Metadata` is currently used for idempotency-key deduplication in
`DbChatHistoryProvider`. Reusing/replacing that field for telemetry risks breaking retry
dedupe behavior and introducing duplicate persisted turns.

Use a dedicated one-to-one telemetry table keyed by `TurnId`:

- `TurnTelemetryEntity` (`TurnId`, `RequestedModel`, `ServedModel`, `Provider`, `ModelId`,
  `ApiBase`, `PromptTokens`, `CompletionTokens`, `TotalTokens`, `ResponseCost`, `Currency`,
  `CostIsEstimated`, `LatencyMs`, `CapturedAt`, `SchemaVersion`)
- Keep `TurnEntity.Metadata` unchanged for existing idempotency behavior.
- Add indexes for common report dimensions (`TenantId` via join, model, provider, captured day).

### Capture: `TelemetryCapturingChatClient` decorator (chosen)

Since MAF's `ChatClientAgent` handles `ChatResponse` internally, we intercept at the
`IChatClient` level by wrapping the real client in a decorator that captures
`ChatResponse.ModelId` and `ChatResponse.Usage` after each call. The captured telemetry is
stored in a scoped `ITurnTelemetryCollector` that `DbChatHistoryProvider` reads when
persisting assistant turns.

### Cost source: LiteLLM response header (primary), token-based estimate (fallback)

Read `x-litellm-response-cost` from the HTTP response headers. If absent, compute an
estimate from token counts and a configurable per-model cost table. Flag estimated vs
reported cost.

### Correlation: request-level correlation id (deferred)

Full proxy-route-log correlation requires changes to the LiteLLM callback to emit a
correlation id that the .NET side can read. This is deferred to a follow-up; the initial
implementation captures model/provider/usage/cost from the HTTP response without proxy
reconciliation.

---

## Implementation Steps

### Step 1: Define Telemetry Schema

**Create** `src/Common/LeanKernel.Logic/Telemetry/TurnTelemetry.cs`:

```csharp
public sealed class TurnTelemetry
{
    public string SchemaVersion { get; init; } = "1.0";
    public string? RequestedModel { get; init; }
    public string? ServedModel { get; init; }
    public string? Provider { get; init; }
    public string? ModelId { get; init; }
    public string? ApiBase { get; init; }
    public int? PromptTokens { get; init; }
    public int? CompletionTokens { get; init; }
    public int? TotalTokens { get; init; }
    public decimal? ResponseCost { get; init; }
    public string? Currency { get; init; }
    public bool CostIsEstimated { get; init; }
    public TimeSpan? Latency { get; init; }
    public DateTimeOffset CapturedAt { get; init; }
}
```

**Create** `src/Common/LeanKernel.Logic/Telemetry/CostEstimateTable.cs`:

```csharp
public sealed class CostEstimateTable
{
    public Dictionary<string,decimal> CostPer1kInputTokens { get; init; } = new();
    public Dictionary<string,decimal> CostPer1kOutputTokens { get; init; } = new();
}
```

### Step 2: Create Telemetry Collector Interface

**Create** `src/Common/LeanKernel.Logic/Telemetry/ITurnTelemetryCollector.cs`:

```csharp
public interface ITurnTelemetryCollector
{
    void Capture(TurnTelemetry telemetry);
    TurnTelemetry? Consume();
}
```

**Create** `src/Common/LeanKernel.Logic/Telemetry/TurnTelemetryCollector.cs`:

Scoped service that stores one `TurnTelemetry` per request. `Capture()` is called by the
chat client decorator; `Consume()` is called by `DbChatHistoryProvider` when persisting the
assistant turn. `Consume()` returns the telemetry and resets the slot.

### Step 3: Create Telemetry-Capturing Chat Client Decorator

**Create** `src/Common/LeanKernel.Logic/Telemetry/TelemetryCapturingChatClient.cs`:

Wraps `IChatClient`. After each `GetResponseAsync` / `GetStreamingResponseAsync` call,
reads `ChatResponse.ModelId` and `ChatResponse.Usage` (if available) and calls
`ITurnTelemetryCollector.Capture(...)`.

For streaming responses, aggregates usage from the final `ChatResponseUpdate` or the
assembled `ChatResponse`.

### Step 4: Wire Telemetry into the DI Pipeline

**Modify** `src/Common/LeanKernel.Logic/Extensions/IServiceCollectionExtensions.cs`:

- Register `ITurnTelemetryCollector` as scoped.
- Register `CostEstimateTable` from configuration.
- Wrap the primary `IChatClient` instances (default + tool model) with
  `TelemetryCapturingChatClient` when telemetry is enabled.

### Step 5: Persist Telemetry on Assistant Turns

**Modify** `src/Common/LeanKernel.Logic/Providers/DbChatHistoryProvider.cs`:

In `StoreChatHistoryAsync`, when persisting an `Assistant` role turn:
1. Keep current idempotency-key metadata behavior unchanged on `TurnEntity.Metadata`.
2. Call `_telemetryCollector.Consume()` to get captured telemetry.
3. If telemetry exists, persist one `TurnTelemetryEntity` linked by `TurnId`.
4. If no telemetry was captured (degraded path), persist the turn without telemetry — never
   drop the turn.
5. Add/verify a uniqueness constraint on `TurnTelemetryEntity.TurnId` to prevent duplicate
   telemetry rows.

### Step 6: Add Configuration

**Create** `src/Common/LeanKernel.Logic/Configuration/TelemetrySettings.cs`:

```csharp
public sealed class TelemetrySettings
{
    public bool Enabled { get; init; } = true;
    public string Currency { get; init; } = "USD";
    public bool RetainRawMetadata { get; init; } = true;
    public bool UseCostEstimate { get; init; } = true;
}
```

**Modify** `appsettings.json` and `appsettings.Development.json` to add
`Agents:Telemetry` section.

**Modify** `src/Common/LeanKernel.Logic/Configuration/AgentSettings.cs` (or equivalent)
to include `TelemetrySettings`.

### Step 7: Build Aggregation and Reporting Surface

This is the primary value of Phase 17. Every persisted telemetry record feeds a queryable
reporting layer that answers cost-accountability and model-selection questions.

**Create** `src/Common/LeanKernel.Logic/Telemetry/ITelemetryAggregationService.cs`:

```csharp
public interface ITelemetryAggregationService
{
    // Per-dimension cost rollups
    Task<IReadOnlyList<CostBreakdown>> GetCostByModelAsync(IPermit permit, DateRange range);
    Task<IReadOnlyList<CostBreakdown>> GetCostByProviderAsync(IPermit permit, DateRange range);
    Task<IReadOnlyList<CostBreakdown>> GetCostByUserAsync(IPermit permit, DateRange range);
    Task<IReadOnlyList<CostBreakdown>> GetCostBySessionAsync(IPermit permit, DateRange range);
    Task<IReadOnlyList<CostBreakdown>> GetCostByDayAsync(IPermit permit, DateRange range);
    Task<IReadOnlyList<CostBreakdown>> GetCostByTenantAsync(IPermit permit, DateRange range); // admin only

    // Cross-dimension drill-down
    Task<IReadOnlyList<CostBreakdown>> GetCostByModelAndDayAsync(IPermit permit, DateRange range);
    Task<IReadOnlyList<CostBreakdown>> GetCostByProviderAndDayAsync(IPermit permit, DateRange range);
    Task<IReadOnlyList<CostBreakdown>> GetCostByUserAndModelAsync(IPermit permit, DateRange range);

    // Summary stats
    Task<CostSummary> GetSummaryAsync(IPermit permit, DateRange range);
    Task<IReadOnlyList<ModelEfficiency>> GetModelEfficiencyAsync(IPermit permit, DateRange range);

    // Top-N queries
    Task<IReadOnlyList<CostBreakdown>> GetTopUsersByCostAsync(IPermit permit, DateRange range, int top = 10);
    Task<IReadOnlyList<CostBreakdown>> GetTopModelsByCostAsync(IPermit permit, DateRange range, int top = 10);
}
```

**Create** `src/Common/LeanKernel.Logic/Telemetry/Models/CostBreakdown.cs`:

```csharp
public record CostBreakdown(
    string Dimension,       // "model", "provider", "user", "session", "day", "tenant"
    string Key,             // e.g. "gpt-4o", "azure", "user-abc", "2026-07-16"
    decimal TotalCost,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    int TurnCount,
    decimal AvgCostPerTurn,
    decimal AvgTokensPerTurn,
    int EstimatedTurnCount,  // turns where cost was estimated vs reported
    int ReportedTurnCount);
```

**Create** `src/Common/LeanKernel.Logic/Telemetry/Models/CostSummary.cs`:

```csharp
public record CostSummary(
    DateRange Range,
    decimal TotalCost,
    int TotalPromptTokens,
    int TotalCompletionTokens,
    int TotalTurns,
    int UniqueUsers,
    int UniqueSessions,
    int UniqueModels,
    decimal AvgCostPerTurn,
    decimal AvgTokensPerTurn,
    string Currency);
```

**Create** `src/Common/LeanKernel.Logic/Telemetry/Models/ModelEfficiency.cs`:

```csharp
public record ModelEfficiency(
    string Model,
    string Provider,
    int TotalTurns,
    int TotalTokens,
    decimal TotalCost,
    decimal CostPer1kTokens,
    decimal AvgPromptTokensPerTurn,
    decimal AvgCompletionTokensPerTurn,
    decimal CompletionRatio);  // completion / total — higher = more generative work
```

**Create** `src/Common/LeanKernel.Logic/Telemetry/Models/DateRange.cs`:

```csharp
public record DateRange(DateTimeOffset From, DateTimeOffset To)
{
    public static DateRange Last7Days() => new(DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow);
    public static DateRange Last30Days() => new(DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow);
    public static DateRange CurrentMonth() => new(
        new DateTimeOffset(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero),
        DateTimeOffset.UtcNow);
}
```

#### Aggregation Strategy

Telemetry is stored in `TurnTelemetryEntity` typed columns (one row per assistant turn).
Aggregation queries run against typed numeric fields and indexed dimensions.

**Primary path:** single-query EF Core `GroupBy` over `TurnTelemetryEntity` joined to
`TurnEntity`/`SessionEntity` for partition scope.

**Scaling path:** if volume requires it, add day-level materialized summaries for
`model`, `provider`, and `tenant` rollups. Keep service API unchanged.

#### Report Scenarios

| Report | Dimension | Question Answered |
|---|---|---|
| **Model cost breakdown** | Model | "How much did we spend on GPT-4o vs GPT-4o-mini this month?" |
| **Provider cost breakdown** | Provider | "Which provider (Azure, Groq, Gemini) is cheapest per token?" |
| **User cost leaderboard** | User | "Which users consume the most budget?" |
| **Daily spend trend** | Day | "Is our daily cost trending up or down?" |
| **Tenant cost allocation** | Tenant | "How should we allocate costs across tenants?" (admin) |
| **Model efficiency** | Model + tokens | "Which model gives the best completion ratio for the cost?" |
| **Session cost distribution** | Session | "What's the typical cost per conversation?" |
| **Cross-dimension drill-down** | Model × Day | "Which model drove the cost spike on July 10?" |
| **User × Model** | User × Model | "Which models does the power-user team prefer?" |
| **Top-N** | Various | "Who are the top 10 spenders?" / "Which models cost the most?" |

**Implementation** uses EF Core `GroupBy` + `Sum`/`Count`/`Average` projections.
All queries are scoped by `IPermit` (TenantId/UserId/ChannelId) to prevent
cross-partition leakage. `GetCostByTenantAsync` requires an admin permit and enforces
that check inside the aggregation service before query execution.

### Step 8: Add Learning Export Shape

**Create** `src/Common/LeanKernel.Logic/Telemetry/TelemetryExportService.cs`:

Produces a deterministic, PII-free labeled dataset:

```csharp
public record TelemetryExportRecord(
    DateTimeOffset Timestamp,
    string RequestedModel,
    string ServedModel,
    string Provider,
    int PromptTokens,
    int CompletionTokens,
    decimal? ResponseCost,
    bool CostIsEstimated);
```

Ordered by timestamp, filtered by tenant/date range. No content, author, or user identity
fields included.

### Step 9: Add Tests

**Create** `test/LeanKernel.Tests.Unit/Telemetry/`:

| Test File | Coverage |
|---|---|
| `TurnTelemetryTests.cs` | Schema serialization round-trip, defaults, null handling |
| `TurnTelemetryCollectorTests.cs` | Capture/consume semantics, reset, thread safety |
| `TelemetryCapturingChatClientTests.cs` | Decorator captures usage from mock ChatClient, streaming aggregation |
| `CostEstimateTableTests.cs` | Token-based cost calculation, missing model graceful fallback |
| `TelemetryAggregationTests.cs` | Aggregation math correctness, partition scoping |
| `TelemetryExportTests.cs` | Export is PII-free, deterministic ordering |

**Modify** `test/LeanKernel.Tests.Unit/Providers/DbChatHistoryProviderTests.cs` (create if
needed) to verify that assistant turns carry telemetry metadata when telemetry is enabled
and gracefully omit it when disabled.

### Step 10: Update Documentation

- **Create** `docs/features/model-telemetry.md` — telemetry schema, capture path,
  aggregation, export, configuration reference.
- **Update** `docs/features/index.md` — add link to `model-telemetry.md`.
- **Update** `docs/configuration/appsettings-reference.md` — add `Agents:Telemetry`
  section.
- **Update** `docs/plans/phase-17-model-telemetry-chat-history/evidence.md` — add
  output references.
- **Update** `docs/plans/phase-17-model-telemetry-chat-history/exit-criteria.md` — check
  gates as they are completed.

---

## Execution Order

1. Schema + collector interface (Steps 1-2) — foundation
2. Chat client decorator (Step 3) — capture path
3. DI wiring (Step 4) — composition root
4. Persistence in DbChatHistoryProvider (Step 5) — storage
5. Configuration (Step 6) — enable/disable
6. Aggregation queries (Step 7) — reporting surface
7. Learning export (Step 8) — labeled dataset
8. Tests (Step 9) — verification
9. Documentation (Step 10) — finalize

Each step should be committed separately for clean history.

---

## Risk Mitigations

| Risk | Mitigation |
|---|---|
| LiteLLM cost header absent | Fallback to token-based estimate from `CostEstimateTable`; flag `CostIsEstimated=true` |
| Streaming usage under-counted | Aggregate final usage from assembled `ChatResponse`; test streaming path |
| MAF `ChatResponse` doesn't expose Usage | Check `ChatResponse` at implementation time; if not available, capture from `ChatMessage.Metadata` or skip gracefully |
| Metadata JSON bloat | Keep `TurnTelemetry` compact (< 500 bytes); monitor storage growth |
| Cross-partition telemetry leakage | Reuse `IPermit` scoping in aggregation queries; isolation tests |
