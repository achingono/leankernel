# Phase 1 LeanKernel.Tools PRD

- **Status:** Reviewed
- **Audience:** LeanKernel maintainers and implementers
- **Phase goal:** Implement the LeanKernel.Tools rearchitecture slice so the solution has a concrete tool registry, execution surface, governance policy, and GBrain-backed built-in wiki tools.
- **Plan review:** Reviewed by `claude-sonnet-4.6`. Review outcome: proceed after avoiding a captive transient `IKnowledgeService` inside singleton tool definitions, aligning policy documentation with the requested open-default visibility behavior, adding directly coupled unit coverage for registry/executor/governance behavior, and recording local toolchain blockers when validation commands cannot run.

## Problem statement

`LeanKernel.Tools` is currently a scaffold project with placeholder marker files only. The rearchitecture needs a real tool package that can register built-in tools, filter them for callers, execute handlers safely, and delegate wiki operations to the existing `IKnowledgeService` backed by GBrain.

## Scope

This task will:

1. Implement `ToolGovernancePolicy`, `ToolRegistry`, and `ToolExecutor` in `src/LeanKernel.Tools`.
2. Implement built-in wiki tools for search, read, and write under `src/LeanKernel.Tools/BuiltIn`.
3. Register the tool services via `AddLeanKernelTools`.
4. Resolve `IKnowledgeService` per tool execution using `IServiceScopeFactory` so singleton tool definitions do not capture a transient dependency.
5. Remove scaffold files `AssemblyMarker.cs` and `GlobalUsings.cs` from `LeanKernel.Tools`.
6. Update directly coupled smoke tests and add focused unit tests for governance, registry lookup, and executor behavior.
7. Add required package references to the tools project for logging and DI abstractions.
8. Attempt restore/build/test/quality validation and capture environment blockers if the .NET SDK or related tooling is unavailable.
9. Mark SQL todo `p1-tools` done after implementation and validation attempts complete.

## Out of scope

- Adding new abstractions beyond the existing tool and knowledge interfaces.
- Expanding built-in tools beyond the requested wiki search/read/write operations.
- Changing `IKnowledgeService` lifetimes in other projects unless required by a compile or runtime issue tightly coupled to this slice.
- Broad solution cleanup unrelated to `LeanKernel.Tools` and its directly coupled tests/docs.

## File plan

### `src/LeanKernel.Tools/ToolGovernancePolicy.cs`
- Implement visibility filtering with explicit tool-name allow lists taking precedence over category filters.
- Document the requested open-default behavior clearly so comments match implementation.

### `src/LeanKernel.Tools/ToolRegistry.cs`
- Store `ToolDefinition` instances in a case-insensitive dictionary.
- Filter visible tools through `ToolGovernancePolicy` and log registry initialization plus visibility counts.

### `src/LeanKernel.Tools/ToolExecutor.cs`
- Resolve tools by name, validate handlers, execute them, and convert exceptions into failed `ToolResult` instances.

### `src/LeanKernel.Tools/BuiltIn/WikiSearchTool.cs`
- Define the `wiki_search` tool metadata.
- Parse `query` and `max_results` arguments defensively, including boxed numeric values.
- Resolve `IKnowledgeService` from a fresh scope per execution and format search results for tool output.

### `src/LeanKernel.Tools/BuiltIn/WikiReadTool.cs`
- Define the `wiki_read` tool metadata.
- Resolve `IKnowledgeService` per execution and return page content or a deterministic not-found error.

### `src/LeanKernel.Tools/BuiltIn/WikiWriteTool.cs`
- Define the `wiki_write` tool metadata.
- Resolve `IKnowledgeService` per execution and persist wiki content through `PutPageAsync`.

### `src/LeanKernel.Tools/ToolsServiceCollectionExtensions.cs`
- Register `ToolGovernancePolicy`, `IToolRegistry`, and `IToolExecutor`.
- Build the built-in tool list once while capturing `IServiceScopeFactory` rather than a concrete `IKnowledgeService` instance.
- Validate required arguments with `ArgumentNullException` checks.

### `src/LeanKernel.Tools/LeanKernel.Tools.csproj`
- Add `Microsoft.Extensions.Logging.Abstractions` and `Microsoft.Extensions.DependencyInjection.Abstractions` version `10.0.7` to support logging and service registration APIs explicitly.

### `src/LeanKernel.Tests.Unit/ScaffoldSmokeTests.cs`
- Replace the `LeanKernel.Tools.AssemblyMarker` assertion with an assembly-name assertion anchored on `typeof(LeanKernel.Tools.ToolRegistry)`.

### `src/LeanKernel.Tests.Unit/Tools/*.cs`
- Add focused unit tests for governance filtering, case-insensitive registry lookup, successful execution, missing-tool/missing-handler handling, and exception translation.

### `docs/plans/index.md`
- Add this PRD to the roadmap plan index.

## Design notes

- Keep the implementation feature-local to `LeanKernel.Tools`; avoid pushing tool logic into `LeanKernel.Host`.
- Use file-scoped namespaces and SDK implicit usings.
- Prefer deterministic error strings because tool callers may surface them directly.
- Preserve `StringComparer.OrdinalIgnoreCase` for tool-name matching.
- Do not introduce options binding until a concrete tool-governance config model exists; the initial policy remains context-driven.

## Validation plan

1. Inspect the resulting diff for namespace, folder, and cleanup correctness.
2. Attempt `dotnet restore src/LeanKernel.sln`.
3. Attempt `dotnet build src/LeanKernel.sln --no-restore -v minimal`.
4. Attempt `dotnet test src/LeanKernel.sln --no-build -v minimal`.
5. Attempt `scripts/quality/test-coverage.sh`.
6. Attempt `scripts/quality/sonarqube-scan.sh`.
7. If the local environment lacks `dotnet` or other required tooling, record that blocker explicitly and still verify source-level consistency of the edited files.

## Acceptance criteria

- `LeanKernel.Tools` contains the requested registry, governance, executor, built-in tools, and DI registration surface.
- Built-in wiki tools do not capture a transient `IKnowledgeService` instance for process lifetime.
- Placeholder files are removed from `LeanKernel.Tools`.
- Directly coupled unit tests reference a concrete tools type instead of `AssemblyMarker` and cover the new behavior.
- Validation evidence is recorded, including any local toolchain blockers.
- SQL todo `p1-tools` is marked `done` only after implementation and validation attempts are complete.
