# Phase 1 LeanKernel.Gateway PRD

- **Status:** Reviewed
- **Audience:** LeanKernel maintainers and implementers
- **Phase goal:** Implement the LeanKernel.Gateway composition root as a thin ASP.NET Core Minimal API host that wires the Phase 1 runtime and exposes the required chat, health, diagnostics, and OpenAPI surfaces.
- **Plan review:** Reviewed by `gpt-5.4-mini`. Review outcome: proceed with the requested slice, keep Gateway thin, add OpenAPI generation for the Minimal API surface, add integration coverage for authorized and unauthorized flows, document the new API/configuration surface, and record the local validation blocker because `dotnet` is unavailable in this environment.

## Problem statement

`LeanKernel.Gateway` is still a near-template ASP.NET Core project with only a trivial health endpoint. The rearchitecture requires Gateway to become the runtime composition root that binds configuration, wires the Phase 1 subsystems, and exposes the minimal public API surface expected by the rest of the platform.

## Scope

This task will:

1. Add direct project references from `LeanKernel.Gateway` to `LeanKernel.Knowledge`, `LeanKernel.Context`, and `LeanKernel.Tools` so the composition root can wire every runtime subsystem explicitly.
2. Replace the template `Program.cs` with a Serilog-enabled Minimal API host that binds `LeanKernelConfig`, registers the requested subsystems, and maps endpoint extensions.
3. Add `Endpoints.cs` with `POST /api/chat`, `GET /api/health`, and `GET /api/diagnostics/{sessionId}`.
4. Add `Models/ChatRequest.cs` with request and response DTOs for the chat endpoint.
5. Replace Gateway appsettings files with the requested runtime configuration payloads.
6. Add OpenAPI generation for the Minimal API surface so the HTTP contract is derived from implementation.
7. Add integration tests covering health, chat, diagnostics, and API-key authorization behavior using `WebApplicationFactory` with stubbed services.
8. Remove leftover template-only files from `LeanKernel.Gateway`.
9. Update the directly related contributor-facing documentation for the new Gateway API and configuration surface.
10. Attempt restore, build, test, coverage, and Sonar validation; record blockers if the local toolchain remains unavailable.
11. Mark SQL todo `p1-gateway` done after implementation and validation attempts.

## Out of scope

- Adding the planned Blazor UI, auth system, onboarding flows, or OpenAI-compatible compatibility endpoints.
- Changing `IAgentRuntime`, persistence contracts, or diagnostics contracts beyond what Gateway needs to compose them.
- Implementing deep dependency readiness checks beyond lightweight service-resolution health reporting.
- Introducing new transport protocols or channel adapters.

## Implementation plan

### Files to add

- `src/LeanKernel.Gateway/Endpoints.cs`
- `src/LeanKernel.Gateway/Models/ChatRequest.cs`
- `src/LeanKernel.Tests.Integration/GatewayEndpointsTests.cs`
- `docs/features/gateway-api.md`
- `docs/configuration/phase-1-config.md`

### Files to remove

- `src/LeanKernel.Gateway/Properties/launchSettings.json`

### Files to update

- `src/LeanKernel.Gateway/LeanKernel.Gateway.csproj`
- `src/LeanKernel.Gateway/Program.cs`
- `src/LeanKernel.Gateway/appsettings.json`
- `src/LeanKernel.Gateway/appsettings.Development.json`
- `docs/plans/index.md`
- `docs/features/index.md`
- `docs/configuration/index.md`
- `docs/architecture/solution-structure.md`
- `README.md`

## Design notes

### Composition root

- Bind `LeanKernelConfig` from the `LeanKernel` section.
- Register `Configure<LeanKernelConfig>` so downstream services can consume typed options.
- Compose persistence, knowledge, context, tools, agents, and diagnostics from the Gateway host only.

### HTTP surface

- `POST /api/chat` accepts the lightweight request DTO, validates input, ensures a session id exists, calls `IAgentRuntime.RunTurnAsync`, and returns a structured response.
- `GET /api/health` reports process liveness plus whether the core runtime and knowledge services resolve from the container.
- `GET /api/diagnostics/{sessionId}` returns persisted diagnostic entries when a diagnostics sink is available.

### API-key validation

- Keep anonymous development mode when no gateway key configuration is present.
- When one or more configured keys are present, require `X-Api-Key` on chat and diagnostics routes.
- Accept the requested single-key `LeanKernel:Gateway:ApiKey` setting and also tolerate an `ApiKeys` array for environment-variable-friendly expansion without changing the requested appsettings payload.

### OpenAPI

- Register OpenAPI generation from the Minimal API implementation and expose the document in development.
- Attach endpoint metadata so the generated contract is usable without a manually maintained endpoint list.

### Test strategy

- Use `WebApplicationFactory<Program>` and `ConfigureTestServices`/configuration overrides.
- Replace `IAgentRuntime`, `IKnowledgeService`, and `IDiagnosticsSink` with deterministic fakes.
- Verify success and unauthorized flows without requiring live Postgres, LiteLLM, or GBrain services.

## Validation plan

1. Inspect the resulting diff and file tree for composition-root, namespace, and scaffold-cleanup correctness.
2. Attempt `dotnet restore src/LeanKernel.sln`.
3. Attempt `dotnet build src/LeanKernel.sln --no-restore -v minimal`.
4. Attempt `dotnet test src/LeanKernel.sln --no-build -v minimal`.
5. Attempt `scripts/quality/test-coverage.sh`.
6. Attempt `scripts/quality/sonarqube-scan.sh`.
7. If `dotnet` or any other required tooling is unavailable, record that blocker explicitly and still verify source-level consistency.

## Acceptance criteria

- `LeanKernel.Gateway` becomes the Minimal API composition root for the Phase 1 runtime.
- The project references and registers persistence, knowledge, context, tools, agents, and diagnostics as required.
- The required chat, health, and diagnostics endpoints exist and are documented through generated OpenAPI metadata.
- Template-only files are removed from `LeanKernel.Gateway`.
- Integration tests cover the endpoint contract and API-key behavior with stubbed services.
- Documentation reflects the current Gateway API/configuration surface.
- Validation evidence is recorded, including the local `dotnet` blocker if it persists.
- SQL todo `p1-gateway` is marked `done` after implementation and validation attempts.
