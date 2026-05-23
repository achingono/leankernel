# Phase 1 — Core Runtime PRD

- **Status:** Draft
- **Audience:** LeanKernel maintainers, implementers, and reviewers
- **Scope owner:** LeanKernel rearchitecture program
- **Phase goal:** Ship a working MAF-native personal agent runtime on Microsoft Agent Framework v1.6.1

## Executive summary

Phase 1 delivers the smallest complete version of the rebuilt LeanKernel platform: one MAF-native agent that accepts a message through an HTTP API, deterministically decides what context is allowed into the prompt, invokes a model through LiteLLM using an OpenAI-compatible surface, returns a response, and persists runtime state in Postgres. GBrain is the retrieval system for wiki and knowledge lookups. The outcome is not feature parity with the current system; it is a clean, verifiable core runtime that proves the new architecture, the deny-by-default context model, and the persistence and diagnostics spine needed for later phases.

This PRD is execution-oriented. It defines the Phase 1 solution boundary, the eleven functional requirements, acceptance criteria, non-functional expectations, assumptions, and measurable success criteria needed to implement the first shippable slice of the new architecture.

## Problem statement

LeanKernel is being rearchitected from scratch around native Microsoft Agent Framework concepts. The legacy system already proved the value of explicit context control, durable runtime state, and observability, but its architecture is broader than what is needed to validate the new foundation. Phase 1 must reduce scope to the essentials while preserving the product's defining behavior:

- MAF-native agent execution rather than framework-adjacent orchestration.
- Deterministic, deny-by-default context assembly rather than full-history prompting.
- Durable Postgres-backed session, turn, and diagnostic state.
- GBrain-backed retrieval rather than ad hoc local knowledge access.
- A minimal but real API and infrastructure footprint that can be tested end to end.

## Goals

1. Prove LeanKernel can run as a MAF-native personal AI agent on .NET 10 and MAF v1.6.1.
2. Preserve the product differentiator of deterministic context gating from day one.
3. Establish shared contracts, persistence, diagnostics, and deployment primitives that later phases can extend without rework.
4. Ensure the Phase 1 stack is fully testable in local development and CI with containerized dependencies.

## Non-goals

Phase 1 does **not** include:

- multi-agent orchestration beyond a single static strategy
- advanced model routing or automatic escalation beyond a static model target
- dynamic skills marketplace or runtime skill discovery
- UI or operator console work
- advanced autonomy policies or approval workflows
- production-grade scaling, sharding, or high-availability deployment
- user profile/onboarding systems beyond what is required to carry a conversation session

## Phase boundary

The phase is complete when the following user journey works end to end:

1. A caller sends `POST /api/chat` with an API key and a message.
2. LeanKernel creates or loads a session from Postgres.
3. The turn pipeline gathers candidate context, applies deterministic budgets, and assembles the final instruction manifest.
4. The runtime invokes a MAF-native agent backed by LiteLLM.
5. The assistant response is returned to the caller.
6. Session state, turn state, diagnostics, and capability-gap records are durable in Postgres.
7. GBrain retrieval and wiki tool stubs are callable by the runtime.

## High-level architecture

```text
Client
  -> LeanKernel.Gateway (Minimal API + API key auth)
  -> LeanKernel.Agents.AgentRuntime
  -> LeanKernel.Agents.TurnPipeline
     -> LeanKernel.Persistence (sessions, turns, diagnostics, capability gaps)
     -> LeanKernel.Context (candidate retrieval, budgeting, gating, prompt assembly)
     -> LeanKernel.Knowledge (GBrain MCP client + knowledge service)
     -> LeanKernel.Tools (registry, governance, MAF adapters, wiki stubs)
     -> LiteLLM / OpenAI-compatible model endpoint
  -> Response
```

## Functional requirements

### FR-1 — Solution Scaffold

Phase 1 must create a new .NET 10 solution that cleanly separates contracts, runtime logic, infrastructure, and tests.

#### Required deliverables

- `src/LeanKernel.sln` targeting .NET 10.
- The following projects:
  - `LeanKernel.Abstractions`
  - `LeanKernel.Agents`
  - `LeanKernel.Context`
  - `LeanKernel.Knowledge`
  - `LeanKernel.Gateway`
  - `LeanKernel.Tools`
  - `LeanKernel.Persistence`
  - `LeanKernel.Diagnostics`
  - `LeanKernel.Tests.Unit`
  - `LeanKernel.Tests.Integration`
- A reference graph that keeps `LeanKernel.Abstractions` dependency-light and reusable.
- Centralized package/version management where practical so MAF, EF Core, Serilog, OpenTelemetry, and test packages remain aligned.

#### Requirement details

- Public contracts used across project boundaries must live in `LeanKernel.Abstractions`.
- `LeanKernel.Gateway` is the composition root for API hosting and dependency injection.
- Test projects must reference implementation projects without introducing circular references.
- The solution must be scaffolded for incremental Phase 2 and Phase 3 growth without renaming or collapsing core project boundaries.

#### Acceptance criteria

- All projects restore and build successfully with `.NET 10` using `dotnet build src/LeanKernel.sln --no-restore -v minimal`.
- The solution has no circular project references.
- Shared contracts are consumed from `LeanKernel.Abstractions`, not duplicated in implementation projects.
- The solution structure clearly supports unit and integration test separation.

### FR-2 — LeanKernel.Abstractions

Phase 1 must define the shared contracts, configuration models, and DTOs that stabilize the runtime surface.

#### Required deliverables

- Configuration models:
  - `LeanKernelConfig`
  - `ContextConfig`
  - `RoutingConfig`
  - `GBrainConfig`
- Core interfaces:
  - `IAgentRuntime`
  - `ITurnPipeline`
  - `IContextGatekeeper`
  - `IKnowledgeService`
  - `ISessionStore`
  - `IToolRegistry`
- Core DTOs:
  - `LeanKernelMessage`
  - `ConversationContext`
  - `ContextBudget`
  - `TurnEvent`
  - `ToolResult`
- Supporting DTOs needed to make the phase implementable, including:
  - `RetrievalCandidate`
  - `InstructionSegment`
  - `ModelRequest`
  - `ModelResponse`
  - `DiagnosticSnapshot`
- Core enums for message roles, context source types, turn status, diagnostic phases, and tool policy outcomes.

#### Requirement details

- Contracts must be implementation-agnostic and serializable across API, persistence, and test boundaries.
- Configuration models must support standard .NET options binding using the `LeanKernel:*` namespace pattern.
- `ContextBudget` must support percentage-based and absolute token allocations so diagnostics can report both.
- `ModelRequest` and `ModelResponse` must be capable of carrying MAF-relevant data such as tool definitions, model metadata, usage counts, and finish reasons.
- Interfaces must support deterministic testing; no implementation-specific types may leak into public contracts.

#### Acceptance criteria

- All DTOs serialize and deserialize with `System.Text.Json` without custom converters beyond what is explicitly documented.
- The configuration models bind successfully from `appsettings.json`, environment variables, and integration-test overrides.
- Contract tests verify required null guards, immutable/required fields where used, and JSON compatibility.
- The abstractions package can be referenced by every other Phase 1 project without pulling in ASP.NET Core or EF Core.

### FR-3 — LeanKernel.Persistence

Phase 1 must provide durable Postgres-backed runtime state using EF Core, Npgsql, and pgvector.

#### Required deliverables

- `LeanKernelDbContext` configured for Postgres 16.
- Npgsql provider configuration with pgvector enabled.
- Entities:
  - `Session`
  - `Turn`
  - `CapabilityGap`
  - `DiagnosticEntry`
- Initial migrations committed to the repository.
- `PostgresSessionStore` implementing `ISessionStore`.

#### Entity expectations

| Entity | Minimum responsibility |
| --- | --- |
| `Session` | Owns session identity, timestamps, agent/runtime metadata, and session-level state. |
| `Turn` | Persists incoming and outgoing turn content, turn status, token usage, model metadata, and correlation to a session. |
| `CapabilityGap` | Records runtime misses, blocked tool attempts, retrieval misses, or unsupported requests for later analysis. |
| `DiagnosticEntry` | Stores machine-readable diagnostics for context gating, prompt assembly, tool visibility, model invocation, and failures. |

#### Requirement details

- Runtime state must be durable even if the process exits after a response is returned.
- The system must persist the incoming user turn before model invocation begins.
- If model invocation or enhancement fails, the failure must still be durable as a failed turn state and a diagnostic record.
- `Session` and `Turn` writes must support optimistic concurrency so concurrent requests against the same session do not silently overwrite each other.
- pgvector must be enabled in the schema even though GBrain is the primary retrieval engine in Phase 1; this preserves compatibility for future persisted embeddings, ranking features, or retrieval traces.

#### Acceptance criteria

- Initial EF migrations apply cleanly against a fresh Postgres 16 + pgvector instance.
- Integration tests verify create/read/update flows for sessions and turns.
- Integration tests verify failed turns and diagnostic records persist when model calls fail.
- Optimistic concurrency conflicts are surfaced deterministically and covered by tests.
- Schema artifacts are committed and reproducible in CI.

### FR-4 — LeanKernel.Knowledge

Phase 1 must integrate with GBrain as the knowledge and wiki retrieval backend through an MCP-style HTTP JSON-RPC client.

#### Required deliverables

- `GBrainClient` over `HttpClient` using JSON-RPC request/response envelopes.
- Operations:
  - `SearchAsync`
  - `GetPageAsync`
  - `PutPageAsync`
  - `TraverseGraphAsync`
- `GBrainKnowledgeService` implementing `IKnowledgeService`.
- Mapping from GBrain results into `RetrievalCandidate` DTOs.

#### Requirement details

- `SearchAsync` must support deterministic ranking output as supplied by GBrain, plus any metadata needed for downstream gating.
- `GetPageAsync` must fetch a single canonical wiki/knowledge page by identifier.
- `PutPageAsync` must support wiki-write tool stubs in a controlled Phase 1 form.
- `TraverseGraphAsync` must support graph-based neighborhood expansion for future retrieval policies even if Phase 1 uses it minimally.
- HTTP timeouts, retries, and error translation must be explicit and configurable in `GBrainConfig`.
- Knowledge-service methods must return enough metadata for diagnostics, including source ids, scores, snippets, and provenance.

#### Acceptance criteria

- Integration tests validate successful search, page read, page write, and graph traversal against a containerized or mocked GBrain endpoint.
- Retries and timeouts are configurable and covered by tests.
- GBrain protocol errors are translated into typed runtime failures rather than opaque transport exceptions.
- `RetrievalCandidate` objects produced by the knowledge service are consumable by `LeanKernel.Context` without additional mapping.

### FR-5 — LeanKernel.Context

Phase 1 must implement the core product differentiator: deny-by-default, deterministic context selection and prompt assembly.

#### Required deliverables

- `ContextGatekeeper`
- `ContextBudget`
- `ContextCandidateRetriever`
- `ConversationHistoryAssembler`
- `PromptAssembler`
- `TokenEstimator`
- `ContextDiagnostics`

#### Deterministic budget policy

The default token budget slices for the input window are:

- **15%** system instructions
- **20%** wiki context
- **40%** conversation history
- **20%** retrieval candidates
- **5%** tool metadata

These percentages must be configurable, but the defaults above are the required Phase 1 baseline.

#### Requirement details

- The gatekeeper starts from an empty context window; nothing is admitted implicitly.
- `ContextCandidateRetriever` gathers possible context from fixed source categories: system, wiki, history, retrieval, and tools.
- `TokenEstimator` computes deterministic token estimates used before model invocation.
- `ConversationHistoryAssembler` must preserve message order while selecting history deterministically within the history slice.
- `PromptAssembler` must emit an instruction manifest that explains which segments were assembled and from which sources.
- The admission/exclusion algorithm must use stable ordering so the same inputs and configuration always yield the same included context.
- Every excluded candidate must have an explicit reason such as `over_budget`, `lower_ranked`, `policy_denied`, or `duplicate`.
- Context diagnostics must be persisted and queryable per turn.

#### Acceptance criteria

- Unit tests verify deterministic budget calculations for fixed token windows.
- Unit tests verify deterministic gatekeeping outcomes for identical candidate sets and configuration.
- Unit tests verify `PromptAssembler` emits a stable instruction manifest with segment provenance.
- Integration tests verify a known session and retrieval set produce the expected slice usage and selected candidates.
- Diagnostics for included and excluded context are persisted as `DiagnosticEntry` rows and exposed through the diagnostics API.

### FR-6 — LeanKernel.Agents

Phase 1 must provide the runtime pipeline that turns a chat request into a persisted, observable model invocation.

#### Required deliverables

- `AgentFactory` that creates a MAF-native `AIAgent` using LiteLLM through an OpenAI-compatible surface.
- `TurnPipeline` implementing the ordered flow:
  1. persist incoming turn
  2. gate context
  3. assemble prompt
  4. invoke model
  5. enhance response
  6. persist assistant turn and emit `TurnEvent`
- `AgentRuntime` as the primary facade behind the API.
- `StaticAgentStrategy` for the Phase 1 single-agent, single-model path.
- MAF middleware registration for pre/post execution hooks.

#### Requirement details

- `AgentFactory` must isolate MAF-specific object creation from pipeline orchestration.
- `TurnPipeline` must persist a durable failure state if any step after initial persistence throws.
- The `enhance` stage may be minimal in Phase 1, but it must exist as a first-class step for tool-result normalization and future response policies.
- `StaticAgentStrategy` must target a configured model alias and avoid dynamic routing logic in Phase 1.
- Middleware must emit diagnostics for entry, exit, duration, and failures.

#### Acceptance criteria

- An end-to-end integration test proves `POST /api/chat` reaches `AgentRuntime`, invokes the model client, and returns a response payload.
- Unit tests cover pipeline behavior for success, model failure, and persistence failure scenarios.
- The runtime emits a durable `TurnEvent` after a successful assistant turn is persisted.
- MAF middleware is active and observable through logs or diagnostics during integration tests.

### FR-7 — LeanKernel.Tools

Phase 1 must provide a minimal but real tool layer that can be governed and exposed through MAF-native tool primitives.

#### Required deliverables

- `IToolDefinition`
- `ToolRegistry`
- `ToolGovernancePolicy`
- `ToolFunctionAdapter` wrapping tools as MAF `AITool`/`AIFunction`
- Stub tools:
  - `wiki_search`
  - `wiki_read`
  - `wiki_write`

#### Requirement details

- `ToolRegistry` must support deterministic registration and lookup by name.
- `ToolGovernancePolicy` must default to deny-by-default and expose allow/deny decisions in diagnostics.
- `wiki_search` delegates to `GBrainClient.SearchAsync` through the knowledge service.
- `wiki_read` delegates to `GBrainClient.GetPageAsync`.
- `wiki_write` delegates to `GBrainClient.PutPageAsync` and must be explicitly governable even if disabled by default.
- `ToolFunctionAdapter` must translate internal tool definitions into MAF tool surfaces without leaking tool implementation details into the model request builder.

#### Acceptance criteria

- Unit tests verify registry registration, lookup, duplicate handling, and deterministic ordering.
- Unit tests verify governance decisions for allow, deny, and missing-policy cases.
- Integration or pipeline tests verify the three wiki stub tools delegate to GBrain and return `ToolResult` payloads.
- Tool visibility and policy outcomes are captured in diagnostics for each turn.

### FR-8 — LeanKernel.Gateway

Phase 1 must expose a minimal API surface for chat, health, and diagnostics.

#### Required deliverables

- ASP.NET Core Minimal API host.
- Endpoints:
  - `POST /api/chat`
  - `GET /api/health`
  - `GET /api/diagnostics/{sessionId}`
- API key authentication.
- Configuration binding for gateway, runtime, persistence, LiteLLM, GBrain, and diagnostics settings.

#### Endpoint expectations

| Endpoint | Responsibility |
| --- | --- |
| `POST /api/chat` | Accept a `LeanKernelMessage`, execute a turn, and return a structured response with session and turn identifiers. |
| `GET /api/health` | Report process readiness and dependency summary suitable for container health checks. |
| `GET /api/diagnostics/{sessionId}` | Return persisted diagnostic entries for a session in a machine-readable format. |

#### Requirement details

- API key auth must be enforced for the chat and diagnostics endpoints.
- Health may expose anonymous access if configured for container orchestration.
- Request/response contracts must remain aligned with DTOs from `LeanKernel.Abstractions`.
- The gateway must be thin; orchestration logic belongs in `AgentRuntime` and the turn pipeline.
- OpenAPI generation is desirable for developer usability if it does not distort the minimal API surface.

#### Acceptance criteria

- Integration tests verify authorized and unauthorized access behavior.
- Integration tests verify `POST /api/chat` returns a successful response with persisted session and turn ids.
- Integration tests verify diagnostics can be retrieved for a known session.
- Health checks report success when required dependencies are reachable or when degraded mode is explicitly configured.

### FR-9 — LeanKernel.Diagnostics

Phase 1 must provide first-class observability across the turn lifecycle.

#### Required deliverables

- `DiagnosticsCollector`
- OpenTelemetry traces and metrics wiring
- `IDiagnosticsSink`
- Postgres-backed diagnostics sink implementation
- Serilog structured logging

#### Requirement details

- Diagnostics must cover: request receipt, session load/create, context budgeting, candidate inclusion/exclusion, prompt assembly, tool visibility, model invocation, persistence, and failures.
- The runtime must emit both machine-readable persisted diagnostics and human-usable structured logs.
- OpenTelemetry traces must correlate gateway, pipeline, GBrain calls, Postgres persistence, and model invocation work.
- Diagnostic retention policy must be explicit so Phase 1 does not silently accumulate unbounded data.

#### Acceptance criteria

- Integration tests verify diagnostic entries are stored for successful and failed turns.
- Traces and metrics are emitted when OpenTelemetry is enabled.
- Log output contains correlation identifiers for session id and turn id.
- A deterministic diagnostic payload exists for each completed turn and can be retrieved by session id.

### FR-10 — Infrastructure

Phase 1 must define the local and CI runtime environment needed to run the stack repeatably.

#### Required deliverables

- `docker-compose` configuration with services:
  - `engine` (.NET 10 API runtime)
  - `database` (Postgres 16 + pgvector)
  - `litellm` (model proxy)
  - `gbrain` (MCP-compatible knowledge service)
- A new multi-stage Dockerfile for the engine.
- Environment variable schema documentation.

#### Required environment variables

| Variable | Purpose |
| --- | --- |
| `LEANKERNEL__Persistence__ConnectionString` | Postgres connection string for runtime state. |
| `LEANKERNEL__Routing__BaseUrl` | LiteLLM/OpenAI-compatible base URL. |
| `LEANKERNEL__Routing__ApiKey` | API key for LiteLLM if required. |
| `LEANKERNEL__Routing__ModelAlias` | Static model alias used by `StaticAgentStrategy`. |
| `LEANKERNEL__GBrain__BaseUrl` | GBrain JSON-RPC endpoint. |
| `LEANKERNEL__GBrain__ApiKey` | Optional GBrain auth token. |
| `LEANKERNEL__Context__MaxInputTokens` | Max prompt-side token budget. |
| `LEANKERNEL__Context__MaxOutputTokens` | Reserved response budget. |
| `LEANKERNEL__Gateway__ApiKeys__0` | First accepted gateway API key. |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Optional OpenTelemetry collector endpoint. |

#### Requirement details

- `docker-compose up` must be sufficient for local smoke testing.
- The database container must enable pgvector on first startup.
- The engine container must wait for required dependencies or fail loudly with actionable logs.
- CI must be able to reuse the same compose topology or an equivalent service matrix for integration tests.

#### Acceptance criteria

- A local compose run starts the required services and `GET /api/health` returns success.
- Migration execution is documented and works against the compose database.
- The engine image builds successfully from the repository Dockerfile.
- Integration tests can run against the compose-defined dependencies or a documented CI equivalent.

### FR-11 — Tests

Phase 1 must ship with enough automated coverage to keep the new architecture safe to evolve.

#### Required deliverables

- Unit tests for:
  - `ContextBudget`
  - `ContextGatekeeper`
  - `PromptAssembler`
  - `TokenEstimator`
  - `ToolRegistry`
- Integration tests for:
  - `GBrainClient`
  - `PostgresSessionStore`
  - full turn pipeline from gateway to persistence
- Automated coverage reporting with a minimum target of **80%** for the in-scope implementation projects.

#### Requirement details

- Tests must favor deterministic fixtures and mocked model responses where live model variability would make CI flaky.
- Integration tests must prove persistence of both success and failure paths.
- Coverage must emphasize logic-heavy projects, especially `LeanKernel.Context`, `LeanKernel.Agents`, `LeanKernel.Knowledge`, and `LeanKernel.Persistence`.
- The test suite must be runnable by contributors without hidden infrastructure beyond the documented container stack.

#### Acceptance criteria

- `dotnet test src/LeanKernel.sln --no-build -v minimal` passes in local and CI environments.
- Coverage reports are generated automatically by the repository quality workflow.
- Combined coverage for the in-scope runtime projects is at least 80%.
- The full turn-pipeline integration test verifies request handling, context gating, model invocation, persistence, and diagnostics in one execution path.

## Non-functional requirements

### Performance

- The gateway and runtime overhead, excluding model latency, should add no more than **500 ms p95** for the reference Phase 1 workload.
- Context retrieval, token estimation, gating, and prompt assembly should complete within **250 ms p95** for a representative session workload.
- Diagnostic persistence must not materially block the request path; when synchronous persistence is required, it must remain observable and bounded.

### Testability

- Every external dependency must be abstracted behind an interface or adapter.
- Deterministic integration tests must be possible with containerized or mocked services.
- Runtime behavior must be inspectable without attaching a debugger.

### Extensibility

- New context sources, tools, agent strategies, and diagnostics sinks must be addable without breaking Phase 1 public contracts.
- The project layout must support future routing, multi-agent workflows, and richer knowledge policies without collapsing module boundaries.

### Determinism and observability

- Identical inputs and configuration must yield identical context-selection results.
- Every turn must have enough diagnostic data to explain why a prompt looked the way it did.
- Failure states must be durable, queryable, and correlated to the session and turn.

## Dependencies and assumptions

- Microsoft Agent Framework v1.6.1 APIs required for the Phase 1 agent path are available and stable enough for implementation.
- LiteLLM exposes an OpenAI-compatible endpoint suitable for the chosen MAF agent adapter.
- GBrain provides an MCP-style HTTP JSON-RPC interface with the required methods.
- Postgres 16 with pgvector is available in local development and CI.
- The repository can adopt or vendor a deterministic tokenizer implementation appropriate for the chosen model family.
- Docker Compose is available for local smoke tests and integration testing.
- Runtime state of record for sessions, turns, gaps, and diagnostics lives in Postgres; GBrain remains the retrieval system of record for wiki/knowledge content.

## Success metrics

| Metric | Target |
| --- | --- |
| End-to-end chat success rate in Phase 1 smoke suite | >= 99% |
| Deterministic context selection for repeated identical test cases | 100% identical outputs |
| Runtime state persistence for successful turns | 100% of successful turns persisted |
| Runtime state persistence for failed turns | 100% of failed turns persisted with diagnostics |
| Gateway + runtime overhead excluding model latency | <= 500 ms p95 |
| In-scope automated test coverage | >= 80% |
| Local stack startup success via Compose | reproducible on clean environment |

## Delivery summary

Phase 1 is successful when LeanKernel has a narrow but complete runtime spine: contracts, persistence, knowledge access, deterministic context gating, MAF-native agent execution, governed tools, a thin API gateway, structured diagnostics, containerized infrastructure, and automated tests. Later phases may add sophistication, but they must build on this foundation rather than replace it.
