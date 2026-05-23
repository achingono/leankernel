# GBrain Garry Tan Integration PRD

- **Status:** Reviewed
- **Audience:** LeanKernel maintainers and implementers
- **Goal:** Replace the current fake/custom GBrain integration with the real [`garrytan/gbrain`](https://github.com/garrytan/gbrain) Bun/TypeScript application while preserving LeanKernel's `IKnowledgeService` and MCP JSON-RPC transport.
- **Plan review:** Reviewed by `gpt-5.4-mini`. Review outcome: proceed, but also update `/mcp` base-URL references outside the originally listed files, define the new local install script contract clearly, make validation steps concrete for the no-.NET environment, and confirm the runtime uses the wiki working directory for PGLite persistence.

## Problem statement

LeanKernel currently integrates against the wrong GBrain distribution. The Docker image downloads a binary from `laozhong86/gbrain`, the Compose service wraps it with `supergateway`, and the knowledge adapter expects payload fields like `key` and `content`. The real upstream package is `garrytan/gbrain`, which is a Bun/TypeScript MCP server installed from GitHub, started with `gbrain serve --http`, and returns page/search payloads keyed by fields such as `slug` and `compiled_truth`.

## Scope

This change will:

1. Rewrite the GBrain container image to use Bun and install `github:garrytan/gbrain`.
2. Replace the legacy binary installer script with a Bun-based local install helper.
3. Update `docker-compose.yml` so the `gbrain` service runs the real upstream server directly, uses embedded PGLite storage under the wiki working directory, no longer depends on the shared Postgres service, and exposes the MCP HTTP endpoint at the root URL.
4. Update LeanKernel GBrain configuration defaults and app settings from `http://gbrain:8789/mcp` to `http://gbrain:8789`.
5. Update `GBrainKnowledgeService` request argument names and response DTO mappings for the real upstream MCP tool contract.
6. Update directly coupled unit and integration tests that assert GBrain field names or base URLs.
7. Update environment docs and feature/infrastructure/README documentation to describe the real GBrain runtime, health check, storage, and API-key expectations.
8. Run concrete non-.NET validation commands available in this environment and record any .NET/Sonar blockers explicitly.

## Out of scope

- Changing the public `IKnowledgeService` contract.
- Replacing the existing `GBrainMcpClient` transport shape.
- Introducing external Postgres support for GBrain in this task.
- Reworking unrelated wiki tools beyond verifying that their `IKnowledgeService` usage still fits.

## File plan

### `config/gbrain/Dockerfile`
- Replace the Node-based image and downloaded binary flow with `oven/bun:1-slim`.
- Install GBrain with `bun install -g github:garrytan/gbrain`.
- Set up `/app/data/wiki` as the persistent working directory used by the container.
- Expose `8789` and default to `gbrain serve --http --port 8789`.

### `config/gbrain/install-gbrain.sh`
- Replace the checksum/download logic with a local-development helper that installs Bun and then installs `github:garrytan/gbrain` globally.
- Document via comments that the script ensures `gbrain` is available on `PATH` for local, non-Docker usage rather than copying a binary to a target path.

### `docker-compose.yml`
- Remove `supergateway` and the `database` dependency from the `gbrain` service.
- Update the service to run with `/app/data/wiki` as the working directory and persisted volume.
- Pass through embedding-provider API keys (`OPENAI_API_KEY`, `ZEROENTROPY_API_KEY`, `ANTHROPIC_API_KEY`) plus timezone.
- Update the health check to use Bun against `http://localhost:8789/health`.
- Update `LEANKERNEL__GBRAIN__BASEURL` for the `engine` service to the root endpoint.

### `src/LeanKernel.Knowledge/GBrainKnowledgeService.cs`
- Change `get_page` and `put_page` arguments from `key` to `slug`.
- Update search and page DTOs to map `slug`, `compiled_truth`, `updated_at`, `page_id`, and link objects.
- Preserve `SearchAsync`, `GetPageAsync`, and `PutPageAsync` behavior through the existing `IKnowledgeService` abstraction.
- Map linked pages from GBrain link targets rather than raw string lists.

### `src/LeanKernel.Abstractions/Configuration/GBrainConfig.cs`
- Change the default base URL to `http://gbrain:8789`.

### `src/LeanKernel.Gateway/appsettings.json`
### `src/LeanKernel.Gateway/appsettings.Development.json`
- Update GBrain base URLs to remove the `/mcp` suffix.

### `src/LeanKernel.Tests.Unit/Knowledge/GBrainKnowledgeServiceTests.cs`
- Update mocked GBrain responses and request assertions to use the real field names and root base URL.

### `src/LeanKernel.Tests.Integration/GatewayEndpointTests.cs`
- Update in-memory test configuration to the new base URL.

### `.env.example`
- Add the embedding-provider API key placeholders used by the real GBrain service.

### `README.md`
### `docs/architecture/infrastructure.md`
### `docs/features/knowledge-retrieval.md`
### `docs/configuration/phase-1-config.md`
- Update GBrain runtime, MCP endpoint, PGLite storage, health-check, and configuration details so docs match the new integration.

## Design notes

- Keep domain behavior inside `LeanKernel.Knowledge`; only composition/configuration changes belong in Gateway/Compose/docs.
- Preserve the existing JSON-RPC 2.0 `tools/call` and `tools/list` behavior in `GBrainMcpClient`.
- Treat `compiled_truth` as the page/result content surfaced through LeanKernel models.
- Continue returning `null` for not-found pages when GBrain reports a matching error.
- Assume the upstream server persists embedded PGLite state from its working directory; if the codebase reveals a required flag already documented elsewhere, align Docker/Compose with that instead of inventing a new path contract.

## Validation plan

1. Run `sh -n config/gbrain/install-gbrain.sh`.
2. Run `docker compose config`.
3. If Docker is available, attempt `docker compose build gbrain`.
4. Inspect edited source and configuration files for syntax/logical consistency.
5. Attempt repository-prescribed quality commands only if the required tooling exists:
   - `dotnet restore src/LeanKernel.sln`
   - `dotnet build src/LeanKernel.sln --no-restore -v minimal`
   - `dotnet test src/LeanKernel.sln --no-build -v minimal`
   - `scripts/quality/test-coverage.sh`
   - `scripts/quality/sonarqube-scan.sh`
6. If .NET SDK, Docker, or Sonar prerequisites are missing, record the blocker explicitly instead of claiming those checks passed.

## Acceptance criteria

- The repository no longer downloads or runs `laozhong86/gbrain` or `supergateway` for the GBrain service.
- The default GBrain base URL is the root HTTP MCP endpoint, not `/mcp`.
- `GBrainKnowledgeService` sends `slug` for page reads/writes and correctly maps upstream response payloads into `RetrievalCandidate` and `KnowledgePage`.
- Directly coupled tests and configuration references are updated for the new payload fields and endpoint.
- Documentation accurately describes the garrytan/gbrain runtime, PGLite-backed storage, health endpoint, and required API-key environment variables.
- Validation evidence is recorded, including any unavailable-tool blockers.
