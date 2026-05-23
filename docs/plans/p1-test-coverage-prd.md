# Phase 1 Test Coverage PRD

- **Status:** Reviewed
- **Audience:** LeanKernel maintainers and implementers
- **Phase goal:** Raise Phase 1 rearchitecture test coverage to at least 80% with focused unit and integration coverage across context gating, agent orchestration, tools, knowledge, persistence, diagnostics, and gateway endpoints.
- **Plan review:** Reviewed by `gpt-5-mini`. Review outcome: proceed after adding coverage for directly coupled helper/runtime paths that materially affect package coverage, using an EF Core in-memory test provider for persistence tests while noting that it does not fully replace PostgreSQL-specific validation, favoring the least risky existing gateway test-host pattern, and recording the current local validation blocker because `dotnet` is unavailable in this environment.

## Problem statement

Phase 1 packages now contain real runtime logic, but automated coverage is still around 51%. The current test suite leaves major behavior in `LeanKernel.Context`, `LeanKernel.Knowledge`, `LeanKernel.Persistence`, `LeanKernel.Diagnostics`, and parts of `LeanKernel.Agents` and `LeanKernel.Tools` under-tested, reducing confidence in the rearchitecture's core differentiators.

## Scope

This task will:

1. Add or expand focused unit tests in `src/LeanKernel.Tests.Unit` for the requested Phase 1 packages.
2. Reorganize broad existing tests into feature-local files matching the requested structure where practical.
3. Add persistence test infrastructure suitable for `PostgresSessionStore` and `PostgresDiagnosticsSink` using EF Core in-memory contexts.
4. Add knowledge-client tests using a fake `HttpMessageHandler` to validate JSON-RPC payloads and response mapping.
5. Expand gateway integration coverage for `/api/chat`, `/api/health`, and `/api/diagnostics/{sessionId}` using mocked runtime dependencies and the existing low-risk in-process host pattern.
6. Update test project package references only where required for the new tests to compile.
7. Attempt restore, build, test, coverage, and Sonar validation, recording blockers if the local .NET toolchain remains unavailable.
8. Mark SQL todo `p1-tests` done after implementation and validation attempts.

## Out of scope

- Changing production behavior outside fixes directly coupled to making the requested tests compile and run.
- Replacing the current persistence implementation with a different database provider.
- Introducing non-existent validation tooling or custom test frameworks.
- Expanding coverage to unrelated post-Phase-1 packages.

## File plan

### Unit tests

- `src/LeanKernel.Tests.Unit/Context/SimpleTokenEstimatorTests.cs`
- `src/LeanKernel.Tests.Unit/Context/ContextBudgetTests.cs`
- `src/LeanKernel.Tests.Unit/Context/ContextCandidateRetrieverTests.cs`
- `src/LeanKernel.Tests.Unit/Context/ConversationHistoryAssemblerTests.cs`
- `src/LeanKernel.Tests.Unit/Context/PromptAssemblerTests.cs`
- `src/LeanKernel.Tests.Unit/Context/ContextGatekeeperTests.cs`
- `src/LeanKernel.Tests.Unit/Agents/StaticAgentStrategyTests.cs`
- `src/LeanKernel.Tests.Unit/Agents/TurnPipelineTests.cs`
- `src/LeanKernel.Tests.Unit/Agents/AgentRuntimeTests.cs`
- `src/LeanKernel.Tests.Unit/Tools/ToolGovernancePolicyTests.cs`
- `src/LeanKernel.Tests.Unit/Tools/ToolRegistryTests.cs`
- `src/LeanKernel.Tests.Unit/Tools/ToolExecutorTests.cs`
- `src/LeanKernel.Tests.Unit/Tools/BuiltInToolTests.cs`
- `src/LeanKernel.Tests.Unit/Knowledge/GBrainMcpClientTests.cs`
- `src/LeanKernel.Tests.Unit/Knowledge/GBrainKnowledgeServiceTests.cs`
- `src/LeanKernel.Tests.Unit/Persistence/PostgresSessionStoreTests.cs`
- `src/LeanKernel.Tests.Unit/Persistence/PostgresDiagnosticsSinkTests.cs`
- `src/LeanKernel.Tests.Unit/Diagnostics/DiagnosticsCollectorTests.cs`
- `src/LeanKernel.Tests.Unit/Diagnostics/LeanKernelMetricsTests.cs`

### Integration tests

- `src/LeanKernel.Tests.Integration/GatewayEndpointTests.cs`
- `src/LeanKernel.Tests.Integration/HealthCheckTests.cs`

### Test project updates

- `src/LeanKernel.Tests.Unit/LeanKernel.Tests.Unit.csproj` (add EF Core in-memory provider if required)
- `docs/plans/index.md`

## Design notes

### Context coverage

- Cover token estimation edge cases, budget calculations, newest-first history shaping with chronological output, stable prompt section ordering, and deny-by-default context admission behavior.
- Retain direct coverage for `ContextCandidateRetriever` because it feeds `ContextGatekeeper` and materially affects package coverage.
- Verify `ContextGatekeeper` admission logs, pooled knowledge budget behavior, low-score exclusion, and empty-candidate outcomes.

### Agents coverage

- Verify `StaticAgentStrategy` maps system/history/user messages into `ChatMessage` instances and forwards tools via `ChatOptions` when present.
- Verify `TurnPipeline` session creation/reuse, tool-name merging, optional response enhancement, optional turn-event publishing, and full happy-path persistence behavior.
- Verify `AgentRuntime` delegates directly to `ITurnPipeline`.

### Tools coverage

- Keep governance, registry, and executor tests aligned to the requested visibility and execution rules.
- Add built-in tool coverage for argument parsing, required-argument validation, result formatting, and knowledge-service delegation because these files materially contribute to project coverage.

### Knowledge coverage

- Validate `GBrainMcpClient` JSON-RPC request formation for `tools/call` and `tools/list`, result-envelope unwrapping, string fallback parsing, null handling, and surfaced MCP errors.
- Validate `GBrainKnowledgeService` mapping for search, page retrieval, missing-page handling, and put-page delegation.

### Persistence coverage

- Use a lightweight in-memory EF Core test database and a small `IDbContextFactory<LeanKernelDbContext>` test double.
- Cover session create/reuse, turn append, chronological history ordering, zero-turn requests, missing-session append failures, diagnostic recording, and JSON payload round-tripping.
- Record that these tests verify repository logic but do not replace future PostgreSQL-specific validation.

### Diagnostics coverage

- Verify `DiagnosticsCollector` enabled/disabled behavior, sink/no-sink paths, persisted category/payload mapping, and `StartTurnActivity` behavior.
- Verify `LeanKernelMetrics` counters and histograms can be created and recorded without throwing.

### Gateway integration coverage

- Extend the existing in-process gateway test host to verify valid chat requests, missing message handling, API-key authorization behavior, healthy status payloads, diagnostics retrieval, and diagnostics-sink-absent behavior.
- Prefer the least risky existing host approach over a larger test-host refactor.

## Validation plan

1. Inspect the resulting file tree and test namespaces for requested structure and consistency.
2. Attempt `dotnet restore src/LeanKernel.sln`.
3. Attempt `dotnet build src/LeanKernel.sln --no-restore -v minimal`.
4. Attempt `dotnet test src/LeanKernel.sln --no-build -v minimal`.
5. Attempt `scripts/quality/test-coverage.sh`.
6. Attempt `scripts/quality/sonarqube-scan.sh`.
7. If `dotnet` or required quality tooling remains unavailable locally, record that blocker explicitly and fall back to source-level consistency checks only.

## Acceptance criteria

- The requested Phase 1 unit and integration test files exist under the expected test project folders.
- The new tests cover the requested context, agents, tools, knowledge, persistence, diagnostics, and gateway scenarios.
- Any required test-only package references are added surgically.
- Validation evidence is recorded, including the local `dotnet` blocker if it persists.
- SQL todo `p1-tests` is marked `done` after implementation and validation attempts.
