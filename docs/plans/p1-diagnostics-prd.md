# Phase 1 LeanKernel.Diagnostics PRD

- **Status:** Reviewed
- **Audience:** LeanKernel maintainers and implementers
- **Phase goal:** Implement the diagnostics package for the rearchitecture so the solution has a concrete observability surface for structured diagnostic collection, metrics, log enrichment, and dependency-injection registration.
- **Plan review:** Reviewed by `claude-sonnet-4.6`. Review outcome: proceed with the requested task scope, keep the user-specified `AddLeanKernelDiagnostics(IServiceCollection, DiagnosticsConfig)` API for this slice, remove placeholder files from `LeanKernel.Diagnostics`, update the smoke test away from `AssemblyMarker`, and record local validation blockers because `dotnet` is unavailable in this environment.

## Problem statement

`LeanKernel.Diagnostics` is currently a scaffold project containing placeholder files only. The rearchitecture needs a real diagnostics package that can emit structured diagnostic entries, OpenTelemetry traces and metrics, and Serilog-enriched logs while staying aligned with the contracts already defined in `LeanKernel.Abstractions`.

## Scope

This task will:

1. Implement `DiagnosticsCollector` under `src/LeanKernel.Diagnostics` using `ILogger<DiagnosticsCollector>`, `IOptions<DiagnosticsConfig>`, and an optional `IDiagnosticsSink`.
2. Emit OpenTelemetry `Activity` traces for context admission, budget usage, tool visibility, model routing, quality gates, and turn lifecycles.
3. Persist structured `DiagnosticEntry` instances through `IDiagnosticsSink` when `DiagnosticsConfig.PersistToDatabase` is enabled.
4. Implement `LeanKernelMetrics` with the requested counters and histograms.
5. Implement `LeanKernelLogEnricher` for Serilog `ServiceName` enrichment.
6. Implement `DiagnosticsServiceCollectionExtensions` with the requested registration surface.
7. Remove placeholder files from `LeanKernel.Diagnostics`, including `AssemblyMarker.cs` and `GlobalUsings.cs`.
8. Update the directly coupled smoke test to anchor on `typeof(LeanKernel.Diagnostics.DiagnosticsCollector)` instead of the removed marker type.
9. Attempt restore/build/test and quality-script validation, recording the local toolchain blocker if execution is not possible.

## Out of scope

- Implementing a concrete database-backed `IDiagnosticsSink`.
- Expanding diagnostics wiring into other projects beyond the package surface requested here.
- Changing configuration contracts already defined in `LeanKernel.Abstractions`.

## Implementation plan

### Files to add

- `src/LeanKernel.Diagnostics/DiagnosticsCollector.cs`
- `src/LeanKernel.Diagnostics/LeanKernelMetrics.cs`
- `src/LeanKernel.Diagnostics/LeanKernelLogEnricher.cs`
- `src/LeanKernel.Diagnostics/DiagnosticsServiceCollectionExtensions.cs`

### Files to remove

- `src/LeanKernel.Diagnostics/AssemblyMarker.cs`
- `src/LeanKernel.Diagnostics/GlobalUsings.cs`

### Files to update

- `src/LeanKernel.Tests.Unit/ScaffoldSmokeTests.cs`
- `docs/plans/index.md`

## API details

### `DiagnosticsCollector`

- Uses a static `ActivitySource` named `LeanKernel.Diagnostics`.
- Records diagnostic data via the existing `IDiagnosticsSink` and `DiagnosticEntry` contracts.
- Uses existing abstractions models `ContextAdmissionRecord` and `ContextBudgetUsage`.
- Uses existing `DiagnosticCategory` enum values converted to strings for persisted entry categories.
- Logs structured summaries through `ILogger<DiagnosticsCollector>`.

### `LeanKernelMetrics`

Creates a `Meter` named `LeanKernel` version `1.0.0` with:

- `leankernel.turns.processed`
- `leankernel.tokens.used`
- `leankernel.turn.latency`
- `leankernel.quality_gate.failures`
- `leankernel.escalations`
- `leankernel.budget.utilization`

### `LeanKernelLogEnricher`

- Implements `Serilog.Core.ILogEventEnricher`.
- Adds a `ServiceName` property with a default of `leankernel`.

### `DiagnosticsServiceCollectionExtensions`

- Registers `Options.Create(config)`.
- Registers `DiagnosticsCollector` and `LeanKernelMetrics` as singletons.

## Validation plan

1. Inspect the resulting diff for namespace and dependency correctness.
2. Attempt `dotnet restore src/LeanKernel.sln`.
3. Attempt `dotnet build src/LeanKernel.sln --no-restore -v minimal`.
4. Attempt `dotnet test src/LeanKernel.sln --no-build -v minimal`.
5. Attempt `scripts/quality/test-coverage.sh`.
6. Attempt `scripts/quality/sonarqube-scan.sh`.
7. If the environment lacks `dotnet` or other required tooling, record that blocker explicitly and still verify the source-level changes.

## Acceptance criteria

- `LeanKernel.Diagnostics` contains the requested collector, metrics, log enricher, and service-registration types.
- Existing `LeanKernel.Abstractions` diagnostics contracts are reused without introducing new abstractions.
- Placeholder files are removed from `LeanKernel.Diagnostics`.
- The smoke test no longer references `LeanKernel.Diagnostics.AssemblyMarker`.
- Validation evidence is recorded, including the local `dotnet` blocker if it persists.
- SQL todo `p1-diagnostics` is marked `done` after implementation and validation attempts.
