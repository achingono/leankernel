# LeanKernel

LeanKernel is the current `.NET 10` LeanKernel rebuild: a gateway-centered microservice stack built around Microsoft Agent Framework (MAF), an ASP.NET gateway, EF-backed persistence, and companion services for LiteLLM, GBrain, and Webwright MCP tooling.

This repository currently implements the rebuild that lives in this worktree. Older LeanKernel layouts and module maps should be treated as reference material, not as the current workspace structure.

## Why Consider LeanKernel

LeanKernel is aimed at teams that want an agent runtime they can actually inspect, test, and evolve, instead of a loose collection of demos and glue code.

### What It Gives You

- A MAF-based runtime with a real ASP.NET gateway instead of an isolated prototype loop.
- Durable transcript and agent-state persistence backed by EF Core.
- A memory pipeline that stays provider-agnostic in logic while integrating with GBrain at the gateway boundary.
- A local stack that can be run either directly from the gateway project or through Docker Compose with PostgreSQL, LiteLLM, and GBrain.

### Why It May Be A Good Fit

- You want a practical starting point for agent-backed product work on .NET.
- You care about identity partitioning, persistence boundaries, and testable runtime behavior.
- You want one workspace that covers API hosting, runtime composition, persistence, and memory plumbing while still running as a service-oriented stack.
- You prefer a rebuild that is explicit about what exists today instead of promising an unimplemented platform surface.

## Current Workspace

- Full contributor-facing solution: [`LeanKernel.sln`](LeanKernel.sln)
- App-only solution: [`src/LeanKernel.sln`](src/LeanKernel.sln)
- Documentation home: [`docs/index.md`](docs/index.md)

### Implemented Projects

| Project | Purpose |
| --- | --- |
| `src/Common/LeanKernel.Core` | Shared interfaces, entities, and low-level contracts |
| `src/Common/LeanKernel.Data` | EF Core context, migrations, interceptors, and design-time data access support |
| `src/Common/LeanKernel.Logic` | Chat history, memory pipeline, identity resolution, and MAF-facing logic services |
| `src/Services/LeanKernel.Gateway` | ASP.NET host, endpoint mapping, auth/session middleware, GBrain integration, and agent state wiring |
| `test/LeanKernel.Tests.Unit` | Unit coverage for core, data, logic, and gateway components |
| `test/LeanKernel.Tests.Integration` | ASP.NET integration tests against the gateway |
| `test/LeanKernel.Tests.Playwright` | Playwright-based API endpoint checks for a running server |

Current architecture details: [`docs/architecture/solution-structure.md`](docs/architecture/solution-structure.md)

## Documentation Map

- Getting started: [`docs/getting-started/index.md`](docs/getting-started/index.md)
- Architecture: [`docs/architecture/index.md`](docs/architecture/index.md)
- Features: [`docs/features/index.md`](docs/features/index.md)
- API surface: [`docs/api/index.md`](docs/api/index.md)
- Configuration: [`docs/configuration/index.md`](docs/configuration/index.md)
- Development workflows: [`docs/development/index.md`](docs/development/index.md)
- Operations: [`docs/operations/index.md`](docs/operations/index.md)
- Decisions: [`docs/decisions/index.md`](docs/decisions/index.md)
- Plans: [`docs/plans/`](docs/plans/)

## Running Locally

### Full Docker Compose Stack

```bash
docker compose up -d --build
```

This starts PostgreSQL with `pgvector`, LiteLLM, GBrain, and the LeanKernel gateway.

Reference: [`docs/getting-started/quick-start.md`](docs/getting-started/quick-start.md)

### Run The Gateway Directly

```bash
dotnet run --project src/Services/LeanKernel.Gateway/LeanKernel.Gateway.csproj --urls "http://127.0.0.1:5080"
```

Direct local runs use the gateway appsettings defaults, which point at SQLite unless you override connection strings.

Reference: [`docs/getting-started/local-development.md`](docs/getting-started/local-development.md)

### Useful Local URLs

- Direct gateway run: `http://127.0.0.1:5080`
- Compose gateway: `http://127.0.0.1:8080`
- Gateway health: `http://127.0.0.1:8080/health`
- LiteLLM: `http://127.0.0.1:4000`
- GBrain: `http://127.0.0.1:8789`

## Build, Test, And Quality Checks

### Build

```bash
dotnet build LeanKernel.sln
```

### Focused App Build

```bash
dotnet build src/LeanKernel.sln
```

### Unit Tests

```bash
dotnet test test/LeanKernel.Tests.Unit/LeanKernel.Tests.Unit.csproj
```

### Integration Tests

```bash
dotnet test test/LeanKernel.Tests.Integration/LeanKernel.Tests.Integration.csproj
```

### Coverage Gate

```bash
scripts/quality/test-coverage.sh
```

### Documentation Link Check

```bash
python3 scripts/quality/check-doc-links.py
```

### Playwright Checks

The Playwright project targets a running server and uses `LEANKERNEL_BASE_URL` when set.

```bash
dotnet test test/LeanKernel.Tests.Playwright/LeanKernel.Tests.Playwright.csproj
```

Current build/test guidance: [`docs/development/build-and-test.md`](docs/development/build-and-test.md)

## Contributing

Use the current worktree as the source of truth. Before making non-trivial changes:

1. Copy the relevant blank templates from [`docs/templates/`](docs/templates/) into a new folder under [`docs/plans/`](docs/plans/).
2. Draft the implementation plan in that folder.
3. Have the plan reviewed before implementation.
4. Make the change.
5. Run verification appropriate to the scope.

Contributor and coding-agent guidance lives in [`AGENTS.md`](AGENTS.md).

## Current Scope Notes

- The implemented runtime is a gateway-centric service stack. The .NET projects in this worktree cover the gateway plus the shared libraries described above.
- `docker-compose.yml` supplies the companion runtime services used by the gateway, including PostgreSQL, LiteLLM, GBrain, Webwright, and Playwright.
- `src/Services` currently contains only `LeanKernel.Gateway`.
- `src/Terminals` currently exists as a placeholder directory and does not contain active projects.
- If older docs or logs mention a much larger monolith-style module map, treat that as historical or aspirational unless the matching project or container exists in this worktree.
