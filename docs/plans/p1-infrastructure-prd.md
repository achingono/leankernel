# Phase 1 Infrastructure PRD

- **Status:** Reviewed
- **Audience:** LeanKernel maintainers and implementers
- **Phase goal:** Replace the local Docker Compose stack with the rearchitected four-service topology centered on LeanKernel.Gateway, PostgreSQL + pgvector, LiteLLM, and GBrain.
- **Plan review:** Reviewed by `gpt-5.4`. Review outcome: proceed after ensuring the root Dockerfile restores from `src/LeanKernel.sln` with all current project files, update directly related runtime docs, validate with Docker-aware checks when available, and record the local `dotnet`/`docker` blockers explicitly if tooling is unavailable.

## Problem statement

The repository still ships a legacy local runtime stack built around Qdrant, Unstructured, the Python indexer, and the Signal sidecar. The rearchitecture now requires a smaller Compose topology: PostgreSQL with pgvector as the system of record, LiteLLM as the model proxy, GBrain as the wiki/memory MCP server, and LeanKernel.Gateway as the .NET entry point. The root Dockerfile and local environment templates must align with the current `src/LeanKernel.sln` project layout so the stack can be built and operated consistently.

## Scope

This task will:

1. Replace the root `docker-compose.yml` with the requested four-service stack (`database`, `litellm`, `gbrain`, `engine`).
2. Replace the root `Dockerfile` so it restores from `src/LeanKernel.sln` and publishes `LeanKernel.Gateway`.
3. Create `scripts/db/init.sql` to provision required PostgreSQL extensions.
4. Ensure the gateway bootstraps the LeanKernel PostgreSQL schema on startup for fresh local environments.
5. Refresh `.env.example` and `.dockerignore` to match the new infrastructure contract.
5. Verify `docker-compose.sonar.yml` still supports standalone SonarQube scanning without relying on removed services.
6. Preserve the existing `config/litellm/config.yaml` when present, only creating a minimal config if it is missing.
7. Ensure `data/wiki/.gitkeep` exists for the GBrain bind mount.
8. Update directly related contributor-facing docs for the new Compose topology and validation flow.
9. Attempt restore, build, test, coverage, Sonar, and Docker-based validation commands; record blockers if required tooling is unavailable.
10. Mark SQL todo `p1-infrastructure` done after implementation and validation attempts.

## Out of scope

- Reworking application configuration binding beyond the environment variables requested for Compose.
- Changing runtime .NET code outside of what is necessary for the container build and documentation accuracy.
- Replacing the existing LiteLLM source spec when `config/litellm/config.yaml` already exists.
- Rewriting unrelated architecture documents that describe broader roadmap direction instead of the local runtime contract.

## Implementation plan

### Files to add

- `docs/plans/p1-infrastructure-prd.md`
- `config/gbrain/Dockerfile`
- `config/gbrain/install-gbrain.sh`
- `scripts/db/init.sql`
- `data/wiki/.gitkeep`

### Files to update

- `docker-compose.yml`
- `Dockerfile`
- `.env.example`
- `.dockerignore`
- `docs/plans/index.md`
- `README.md`
- `docs/development/index.md`
- `docs/development/quality.md`

### Files to review only

- `docker-compose.sonar.yml`
- `config/litellm/config.yaml`

## Design notes

### Compose topology

- `database` uses `pgvector/pgvector:pg16`, exposes the configured Postgres port, and runs `scripts/db/init.sql` on first boot.
- `litellm` uses the upstream `ghcr.io/berriai/litellm:main-latest` image, points its backing database at the shared Postgres instance, mounts `config/litellm` read-only, renders the runtime config on startup, and uses LiteLLM's liveliness probe for health gating.
- `gbrain` builds from `config/gbrain/Dockerfile`, installs a checksum-verified pinned GBrain release binary plus a pinned `supergateway` version, persists wiki data from `./data/wiki`, exposes the stdio MCP server over HTTP `/mcp`, and uses a `tools/list` probe for health checks.
- `engine` builds from the root Dockerfile, listens on port `5080`, receives `LEANKERNEL__*` environment variables for LiteLLM, GBrain, database, diagnostics, and gateway settings, and ensures the LeanKernel database schema exists during startup.
- Implementation note: the current upstream GBrain package is a stdio MCP server, so the Compose service uses `supergateway` to preserve LeanKernel's existing HTTP `/mcp` client contract.

### Dockerfile

- Keep the restore layer cache-friendly by copying `src/LeanKernel.sln`, `src/Directory.Build.props`, and every solution `.csproj` before `dotnet restore`.
- Publish `LeanKernel.Gateway` from the build stage and run it from the ASP.NET runtime image with `curl` installed for the container health check.
- Restore a dedicated non-root runtime user in the final engine image after publishing.

### Documentation updates

- Update the README quick-start, stack summary, environment example, and repository structure so contributors see the current four-service runtime instead of the removed sidecars.
- Update development docs so Docker validation instructions reference the new compose flow and standalone SonarQube compose file.

## Validation plan

1. Attempt `dotnet restore src/LeanKernel.sln`.
2. Attempt `dotnet build src/LeanKernel.sln --no-restore -v minimal`.
3. Attempt `dotnet test src/LeanKernel.sln --no-build -v minimal`.
4. Attempt `scripts/quality/test-coverage.sh`.
5. Attempt `scripts/quality/sonarqube-scan.sh`.
6. If Docker is available, run `docker compose config` and `docker build -t leankernel-engine:local .`.
7. If `dotnet` or Docker tooling is unavailable, record the blocker explicitly and still verify source-level consistency through diff inspection.

## Acceptance criteria

- The root Compose file defines only the rearchitected `database`, `litellm`, `gbrain`, and `engine` services plus the requested volumes.
- The root Dockerfile restores and publishes the current Gateway-based .NET solution layout.
- Postgres initialization, environment template, ignore rules, and wiki bootstrap files are present and match the requested infrastructure contract.
- `docker-compose.sonar.yml` remains valid for standalone SonarQube scans.
- Contributor docs accurately describe the current local runtime stack and validation flow.
- Validation evidence is recorded, including any unavailable-tooling blockers.
- SQL todo `p1-infrastructure` is marked `done` after implementation and validation attempts.
