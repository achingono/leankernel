# Phase 1 LeanKernel.Knowledge PRD

- **Status:** Reviewed
- **Audience:** LeanKernel maintainers and implementers
- **Phase goal:** Implement the LeanKernel.Knowledge infrastructure package for the rearchitecture so the solution has a concrete GBrain-backed `IKnowledgeService` for wiki and knowledge retrieval operations.
- **Plan review:** Reviewed by `claude-sonnet-4.6`. Review outcome: proceed with the requested GBrain MCP client and knowledge service, update the scaffold smoke test to stop depending on `AssemblyMarker`, prefer a transient knowledge service registration to avoid capturing the typed HTTP client for process lifetime, and record the local validation blocker if the .NET SDK is unavailable.

## Problem statement

`LeanKernel.Knowledge` is currently a scaffold project with placeholder files only. The rearchitecture needs a concrete implementation that talks to the GBrain MCP HTTP JSON-RPC endpoint, translates MCP payloads into `LeanKernel.Abstractions` retrieval and page models, and exposes a DI registration surface for downstream composition.

## Scope

This task will:

1. Implement a low-level `GBrainMcpClient` for MCP `tools/call` and `tools/list` operations over `HttpClient`.
2. Implement `GBrainException` for surfaced MCP errors.
3. Implement `GBrainKnowledgeService` as the `IKnowledgeService` adapter over GBrain search, page read, and page write tools.
4. Implement `KnowledgeServiceCollectionExtensions.AddLeanKernelKnowledge` to configure the HTTP client from `GBrainConfig` and register the knowledge service.
5. Remove scaffold marker files from `src/LeanKernel.Knowledge` and update directly coupled tests.
6. Verify source layout and attempt restore/build/test/quality commands, recording environment blockers if local tooling is unavailable.
7. Mark SQL todo `p1-knowledge` done after implementation and validation attempts.

## Out of scope

- Adding GBrain-specific abstractions to `LeanKernel.Abstractions`.
- Introducing additional downstream consumers in other projects.
- Adding a new test suite beyond directly coupled updates required by scaffold cleanup.
- Extending MCP support beyond the requested `tools/call` and `tools/list` workflow.

## File plan

### `src/LeanKernel.Knowledge/GBrainMcpClient.cs`
- Define JSON-RPC request and response DTOs for MCP.
- Implement `CallToolAsync` with incrementing request IDs, `tools/call`, error translation, and JSON payload parsing.
- Implement `ListToolsAsync` with `tools/list` response parsing into `McpToolInfo`.

### `src/LeanKernel.Knowledge/GBrainException.cs`
- Add a small exception type carrying the MCP error code.

### `src/LeanKernel.Knowledge/GBrainKnowledgeService.cs`
- Map `search`, `get_page`, and `put_page` tool calls onto `IKnowledgeService`.
- Convert GBrain search results into `RetrievalCandidate` models.
- Convert page payloads into `KnowledgePage` and treat not-found MCP errors as null page results.
- Use a simple token estimator aligned with the requested rough approximation.

### `src/LeanKernel.Knowledge/KnowledgeServiceCollectionExtensions.cs`
- Add the `AddLeanKernelKnowledge(IServiceCollection, GBrainConfig)` registration API.
- Configure `HttpClient` base address, timeout, and optional bearer token.
- Register `IKnowledgeService` with a transient lifetime so the typed `GBrainMcpClient` is not captured for the full process lifetime.

### `src/LeanKernel.Tests.Unit/ScaffoldSmokeTests.cs`
- Replace the `LeanKernel.Knowledge.AssemblyMarker` assertion with an assembly-name assertion anchored on a concrete knowledge type so the marker cleanup does not break the scaffold smoke test.

## Design notes

- Keep all implementation files under the `LeanKernel.Knowledge` namespace with file-scoped declarations.
- Reuse `LeanKernel.Abstractions.Configuration.GBrainConfig`, `LeanKernel.Abstractions.Interfaces.IKnowledgeService`, and the existing knowledge models directly.
- Preserve the requested MCP JSON-RPC shape: `jsonrpc`, `id`, `method`, `params`, `name`, and `arguments`.
- Let `HttpClient` transport exceptions propagate naturally while converting MCP protocol errors into `GBrainException`.

## Validation plan

1. Inspect the resulting file structure for namespace and cleanup correctness.
2. Attempt `dotnet restore src/LeanKernel.sln`.
3. Attempt `dotnet build src/LeanKernel.sln --no-restore -v minimal`.
4. Attempt `dotnet test src/LeanKernel.sln --no-build -v minimal`.
5. Attempt `scripts/quality/test-coverage.sh`.
6. Attempt `scripts/quality/sonarqube-scan.sh`.
7. If the local environment lacks the .NET SDK or other required tooling, capture that blocker explicitly and still verify the source-level consistency of the changes.

## Acceptance criteria

- `LeanKernel.Knowledge` contains the requested GBrain MCP client, exception, knowledge service, and DI extension.
- The project no longer contains scaffold marker files.
- `IKnowledgeService` resolves through `AddLeanKernelKnowledge` using `GBrainConfig` settings for base URL, timeout, and optional bearer token.
- The unit smoke test no longer depends on `LeanKernel.Knowledge.AssemblyMarker`.
- Validation evidence is recorded, including any local toolchain blockers.
- SQL todo `p1-knowledge` is marked `done` only after implementation and validation attempts are complete.
