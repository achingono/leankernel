# LeanKernel

LeanKernel is a `.NET 10` modular-monolith agent platform built on [Microsoft Agent Framework](https://github.com/microsoft/agent-framework) with a Blazor + API gateway, durable persistence, and configurable model/runtime orchestration.

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


## Repository Structure

Primary solution: `src/LeanKernel.sln`

Current solution projects and responsibilities:

- `src/Common/LeanKernel.Core`: shared code contracts, interfaces, and cross-project models
- `src/Common/LeanKernel.Data`: EF Core/Postgres persistence, session store, diagnostics/doc-ingestion repositories. Shared data contracts and persistence.
- `src/Common/LeanKernel.Logic`: turn pipeline, runtime execution, routing/orchestration strategy, response quality/enhancement
- `src/Common/LeanKernel.Logic`: context gating, retrieval scoping, history shaping, and identity grounding helpers
- `src/Services/LeanKernel.Knowledge`: GBrain-backed knowledge client/service integration
- `src/Services/LeanKernel.Tools`: built-in tool registry/execution (file, web, browser, wiki, data) and document ingestion plumbing
- `src/LeanKernel.Plugins`: dynamic runtime skills loading (`SKILL.md` parsing/registration)
- `src/Services/LeanKernel.Diagnostics`: diagnostics services and runtime metrics primitives
- `src/Services/LeanKernel.Channels`: channel router/auth and  Signal channel host integration
- `src/Services/LeanKernel.Learning`: background learning pipeline (fact/intent extraction, capability and engagement signals)
- `src/Services/LeanKernel.Scheduler`: cron-based background job scheduling/execution
- `src/Services/LeanKernel.Gateway`: composition root, minimal APIs, middleware, auth, health endpoints
- `src/Terminals/LeanKernel.Portal`: management portal
- `src/Terminals/LeanKernel.Client`: AG-UI client powered by `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore`

Key pairings in runtime composition:

- `LeanKernel.Gateway` composes all runtime modules and hosts HTTP + UI surfaces
- `LeanKernel.Tools` + `LeanKernel.Plugins` provide built-in tools and dynamic skill tools
- `LeanKernel.Context` + `LeanKernel.Knowledge` + `LeanKernel.Persistence` provide context assembly and durable memory inputs
- `LeanKernel.Agents` + `LeanKernel.Learning` provide turn execution plus background learning updates


## CI-Aligned Local Commands

The CI workflow is in [`.github/workflows/build-and-test.yml`](.github/workflows/build-and-test.yml).

### Build and non-Playwright tests

```bash
dotnet restore src/LeanKernel.sln
dotnet build src/LeanKernel.sln --no-restore -v minimal
dotnet test test --no-build -v minimal --filter 'FullyQualifiedName!~Playwright' --logger "trx;LogFileName=results.trx" --collect:"XPlat Code Coverage"
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
dotnet run --project src/Services/LeanKernel.Gateway/LeanKernel.Gateway.csproj --urls "http://127.0.0.1:5080"
```

In another shell, run UI tests:

```bash
LEANKERNEL_BASE_URL="http://127.0.0.1:5080" dotnet test test/LeanKernel.Tests.Playwright/LeanKernel.Tests.Playwright.csproj
```

## Local Runtime Notes

- Gateway default URL in local docs/tests: `http://127.0.0.1:5080`
- Running outside Docker is supported for UI/test flows, but service probes to external dependencies may log warnings if backing services are unavailable.
- For roadmap and implementation planning artifacts, see [`docs/plans/index.md`](docs/plans/index.md).

## Dependency Credits

LeanKernel builds on several great open-source projects:

- Microsoft Agent Framework (MAF): <https://github.com/microsoft/agent-framework>
- .NET (SDK/Runtime images): <https://github.com/dotnet/dotnet-docker>
- PostgreSQL: <https://github.com/postgres/postgres>
- pgvector: <https://github.com/pgvector/pgvector>
- GBrain: <https://github.com/garrytan/gbrain>
- LiteLLM: <https://github.com/BerriAI/litellm>
- Playwright: <https://github.com/microsoft/playwright>
- PaddleOCR: <https://github.com/PaddlePaddle/PaddleOCR>
- PaddlePaddle: <https://github.com/PaddlePaddle/Paddle>
- pdf2image: <https://github.com/Belval/pdf2image>
- signal-cli REST API: <https://github.com/bbernhard/signal-cli-rest-api>
