# LeanKernel

LeanKernel is a `.NET 10` modular-monolith agent platform with a Blazor + API gateway, durable persistence, and configurable model/runtime orchestration.

## Why Try LeanKernel

LeanKernel is built for teams that want to ship practical AI agents without stitching together fragile demos. It gives you one opinionated runtime for chat turns, context assembly, tool execution, diagnostics, and UI/API delivery.

## Pain Points It Solves

- Agent prototypes that work in notebooks but break in production.
- Opaque prompt/context behavior that is hard to debug.
- Tool-heavy agent flows with weak governance and poor observability.
- Scattered architecture across many services before the product earns that complexity.

## Value Proposition

- Deterministic, inspectable turn pipeline with persisted diagnostics.
- Built-in tooling, retrieval, scheduling, and hardening controls.
- Dual surface out of the box: API endpoints plus a Blazor operator UI.
- Modular project boundaries without forcing microservice overhead.

## Who It Is For

- Product engineers building agent-backed features.
- Platform teams standardizing runtime behavior and safety controls.
- Applied AI teams that need reproducible local-to-CI workflows.
- Developers who want to move fast but keep architecture maintainable.

## Start Here

- Documentation home: [`docs/index.md`](docs/index.md)
- Getting started: [`docs/getting-started/index.md`](docs/getting-started/index.md)
- Architecture: [`docs/architecture/index.md`](docs/architecture/index.md)
- Feature docs: [`docs/features/index.md`](docs/features/index.md)
- API docs: [`docs/api/index.md`](docs/api/index.md)
- Configuration reference: [`docs/configuration/index.md`](docs/configuration/index.md)
- Development and quality: [`docs/development/index.md`](docs/development/index.md)

## Repository Structure

Primary solution: `src/LeanKernel.sln`

- `src/LeanKernel.Abstractions`: contracts and shared models/config
- `src/LeanKernel.Core`: core primitives
- `src/LeanKernel.Agents`: runtime orchestration and strategy execution
- `src/LeanKernel.Context`: prompt/context assembly and gating
- `src/LeanKernel.Knowledge`: knowledge and retrieval integrations
- `src/LeanKernel.Persistence`: persistence and session storage
- `src/LeanKernel.Tools`: built-in tool surface
- `src/LeanKernel.Channels`: channel routing and adapters
- `src/LeanKernel.Diagnostics`: telemetry and diagnostics primitives
- `src/LeanKernel.Scheduler`: scheduled/background jobs
- `src/LeanKernel.Gateway`: ASP.NET Core host + Blazor UI + API endpoints

## CI-Aligned Local Commands

The CI workflow is in [`.github/workflows/build-and-test.yml`](.github/workflows/build-and-test.yml).

### Build and non-Playwright tests

```bash
dotnet restore src/LeanKernel.sln
dotnet build src/LeanKernel.sln --no-restore -v minimal
dotnet test src/LeanKernel.sln --no-build -v minimal --filter 'FullyQualifiedName!~Playwright' --logger "trx;LogFileName=results.trx" --collect:"XPlat Code Coverage"
```

### Coverage gate

```bash
scripts/quality/test-coverage.sh
```

### Playwright tests

Install Playwright browser dependencies once:

```bash
npx playwright install --with-deps chromium
```

Start the app:

```bash
dotnet run --project src/LeanKernel.Gateway/LeanKernel.Gateway.csproj --urls "http://127.0.0.1:5080"
```

In another shell, run UI tests:

```bash
LEANKERNEL_BASE_URL="http://127.0.0.1:5080" dotnet test test/LeanKernel.Tests.Playwright/LeanKernel.Tests.Playwright.csproj
```

## Local Runtime Notes

- Gateway default URL in local docs/tests: `http://127.0.0.1:5080`
- Running outside Docker is supported for UI/test flows, but service probes to external dependencies may log warnings if backing services are unavailable.
- For roadmap and implementation planning artifacts, see [`docs/plans/index.md`](docs/plans/index.md).
