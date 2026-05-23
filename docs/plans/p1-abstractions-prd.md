# Phase 1 LeanKernel.Abstractions PRD

- **Status:** Reviewed
- **Audience:** LeanKernel maintainers and implementers
- **Phase goal:** Establish the shared abstractions package for the rearchitecture so downstream projects can depend on stable contracts, configuration models, DTOs, and enums without pulling in infrastructure dependencies.
- **Plan review:** Reviewed by `claude-sonnet-4.6` after an initial `gpt-5.4` review. Review outcome: proceed with a narrowed task scope matching todo `p1-abstractions`, use `typeof(LeanKernel.Abstractions.Configuration.LeanKernelConfig)` as the smoke-test anchor after marker cleanup, and capture validation blockers if the .NET toolchain is unavailable locally.

## Problem statement

`LeanKernel.Abstractions` is currently a scaffold project with placeholder marker files. The rearchitecture needs a real shared-contracts package that defines the configuration binding surface, core orchestration interfaces, transport and retrieval DTOs, and foundational enums used across the solution.

## Scope

This task will:

1. Implement the requested configuration models under `src/LeanKernel.Abstractions/Configuration`.
2. Implement the requested interfaces under `src/LeanKernel.Abstractions/Interfaces`.
3. Implement the requested DTOs and models under `src/LeanKernel.Abstractions/Models`.
4. Implement the requested enums under `src/LeanKernel.Abstractions/Enums`.
5. Keep the package dependency-free beyond the .NET base class library.
6. Remove placeholder files from `LeanKernel.Abstractions`, including `AssemblyMarker.cs` and `GlobalUsings.cs`.
7. Update the directly coupled smoke test to reference `typeof(LeanKernel.Abstractions.Configuration.LeanKernelConfig)` instead of the removed `AssemblyMarker` type.
8. Attempt restore/build/test/quality validation and record environment blockers if local tooling is unavailable.

## Out of scope

- Adding packages or implementation logic to downstream projects.
- Introducing infrastructure concerns such as EF Core, ASP.NET Core, logging providers, or HTTP clients into `LeanKernel.Abstractions`.
- Expanding this task beyond the explicitly requested contract set.
- Implementing the broader Phase 1 supporting DTOs `InstructionSegment`, `ModelRequest`, `ModelResponse`, and `DiagnosticSnapshot`; those remain follow-up work even though the broader phase plan mentions them.

## Namespace map

- `LeanKernel.Abstractions.Configuration`
- `LeanKernel.Abstractions.Interfaces`
- `LeanKernel.Abstractions.Models`
- `LeanKernel.Abstractions.Enums`

All files will use file-scoped namespaces and BCL-only type references.

## API matrix

### Configuration

- `LeanKernelConfig`
  - section constant: `SectionName = "LeanKernel"`
  - properties: `LiteLlm`, `Context`, `Routing`, `GBrain`, `Database`, `Diagnostics`
- `LiteLlmConfig`
  - defaults: `BaseUrl = "http://litellm:4000"`, `ApiKey = string.Empty`, `DefaultModel = "gpt-4o-mini"`, `ContextWindowTokens = 128_000`
- `ContextConfig`
  - defaults for budget ratios, turn limits, and entity expansion settings exactly as requested
- `RoutingConfig`
  - defaults for enablement, quality thresholds, escalation count, and shadow routing flag
- `GBrainConfig`
  - defaults: `BaseUrl = "http://gbrain:8789/mcp"`, `AuthToken = string.Empty`, `TimeoutSeconds = 30`
- `DatabaseConfig`
  - default Postgres connection string for the compose environment
- `DiagnosticsConfig`
  - defaults: diagnostics enabled, persist to database enabled, `ServiceName = "leankernel"`

### Interfaces

- `IAgentRuntime`
  - `Task<string> RunTurnAsync(LeanKernelMessage message, CancellationToken ct = default);`
- `ITurnPipeline`
  - `Task<string> ProcessAsync(LeanKernelMessage message, CancellationToken ct = default);`
- `IContextGatekeeper`
  - `Task<ConversationContext> GateContextAsync(LeanKernelMessage message, ContextBudget budget, string sessionId, CancellationToken ct = default);`
- `IKnowledgeService`
  - search APIs over `RetrievalCandidate`, page retrieval via `KnowledgePage`, and page persistence via `key` plus `content`
- `ISessionStore`
  - session creation, turn append, and history retrieval APIs over `ConversationTurn`
- `IToolRegistry`
  - visible-tool projection plus direct lookup via `ToolDefinition`
- `IToolExecutor`
  - `Task<ToolResult> ExecuteAsync(string toolName, IDictionary<string, object?> arguments, CancellationToken ct = default);`
- `IResponseEnhancer`
  - `Task<string> EnhanceAsync(string response, ConversationContext context, CancellationToken ct = default);`
- `IDiagnosticsSink`
  - write/read APIs over `DiagnosticEntry`
- `ITurnEventSink`
  - `Task PublishAsync(TurnEvent turnEvent, CancellationToken ct = default);`
- `ITokenEstimator`
  - `int EstimateTokens(string text);`

### Models

- `LeanKernelMessage` and companion `Attachment`
- `ConversationContext`
- `ConversationTurn`
- `ContextBudget`
  - exact factory signature: `public static ContextBudget FromConfig(int contextWindowTokens, ContextConfig config)`
  - calculates usable tokens as `contextWindowTokens * (1.0 - config.ResponseHeadroomRatio)` and allocates budget buckets from that total
- `ContextBudgetUsage`
- `RetrievalCandidate`
- `KnowledgePage`
- `ToolDefinition` and `ToolParameter`
- `ToolVisibilityContext`
- `ToolResult`
- `TurnEvent`
- `DiagnosticEntry`
- `ContextAdmissionRecord`

### Enums

- `ContextAdmissionReason`
- `ContextExclusionReason`
- `ModelTier`
- `QualityOutcome`
- `DiagnosticCategory`

## Cleanup plan

- Delete `src/LeanKernel.Abstractions/AssemblyMarker.cs`.
- Delete `src/LeanKernel.Abstractions/GlobalUsings.cs` because the project keeps SDK implicit usings enabled.
- Avoid adding replacement marker files inside `LeanKernel.Abstractions`.
- Update `src/LeanKernel.Tests.Unit/ScaffoldSmokeTests.cs` so the Abstractions assertion anchors on `typeof(LeanKernel.Abstractions.Configuration.LeanKernelConfig).Assembly.GetName().Name`.

## Validation plan

1. Inspect the resulting diff for folder and namespace correctness.
2. Attempt `dotnet restore src/LeanKernel.sln`.
3. Attempt `dotnet build src/LeanKernel.Abstractions/LeanKernel.Abstractions.csproj --no-restore` or the nearest workable equivalent.
4. Attempt `dotnet build src/LeanKernel.sln --no-restore` with the understanding that downstream failures may remain outside this task.
5. Attempt `dotnet test src/LeanKernel.sln --no-build -v minimal`.
6. Attempt `scripts/quality/test-coverage.sh`.
7. Attempt `scripts/quality/sonarqube-scan.sh`.
8. If the environment lacks `dotnet`, Docker, or other required tooling, capture that blocker explicitly and still verify the file layout and source-level consistency.

## Acceptance criteria

- `LeanKernel.Abstractions` contains the requested configuration models, interfaces, models, and enums under the documented namespaces.
- The project remains free of external package dependencies.
- Placeholder files are removed from `LeanKernel.Abstractions`.
- The directly coupled smoke test no longer references `LeanKernel.Abstractions.AssemblyMarker`.
- Validation evidence is recorded, including toolchain blockers if local execution is not possible.
- SQL todo `p1-abstractions` is marked `done` after implementation and validation attempts.