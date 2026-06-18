# AGENTS.md

Contributor and coding-agent guide for this repository.

## Project Snapshot

- Monorepo: `.NET 10` modular monolith (`src/LeanKernel.sln`).
- Primary runtime app: `src/LeanKernel.Gateway` (API + Blazor UI + composition).
- Core project map (current folders under `src/`):
  - `LeanKernel.Abstractions`: contracts, shared models, config types.
  - `LeanKernel.Core`: shared domain/core primitives.
  - `LeanKernel.Agents` + `LeanKernel.Thinker`: orchestration, strategy/routing, agent runtime.
  - `LeanKernel.Context`: context assembly, identity/history shaping.
  - `LeanKernel.Knowledge` + `LeanKernel.Archivist`: wiki/retrieval/knowledge integration.
  - `LeanKernel.Persistence`: DB/session persistence.
  - `LeanKernel.Plugins` + `LeanKernel.Tools`: built-in tools and skill/tool execution.
  - `LeanKernel.Channels` + `LeanKernel.Commander`: channel routing and outbound command paths.
  - `LeanKernel.Diagnostics`: metrics/diagnostics.
  - `LeanKernel.Scheduler`: scheduled work.
  - `LeanKernel.Gateway` + `LeanKernel.Host`: host composition and UI/API surface.

## Working Rules

- Keep behavior feature-local to the owning domain project.
- Do not move domain logic into `LeanKernel.Gateway`/host layers unless it is composition/UI/API only.
- Reuse existing contracts in `LeanKernel.Abstractions.Interfaces` before introducing abstractions.
- Preserve config style (`LeanKernel:*`) and current naming patterns.
- Avoid broad exception swallowing; log actionable context.

## Required Change Workflow

When implementing user-requested issues/changes:

1. Draft a concrete implementation plan.
2. Review the plan with a different model.
3. Save the reviewed plan as a PRD in `docs/plans/` before code changes.
4. Implement, run tests, and run quality checks.
5. Iterate until quality gates pass.

## Local Testing (CI-Aligned)

Reference workflow: `.github/workflows/build-and-test.yml`.

### Prerequisites

- .NET SDK `10.0.x`
- Node.js `18`
- Playwright browsers:

```bash
npx playwright install --with-deps chromium
```

### CI-parity build and test (non-Playwright)

```bash
dotnet restore src/LeanKernel.sln
dotnet build src/LeanKernel.sln --no-restore -v minimal
dotnet test src/LeanKernel.sln --no-build -v minimal --filter 'FullyQualifiedName!~Playwright' --logger "trx;LogFileName=results.trx" --collect:"XPlat Code Coverage"
scripts/quality/test-coverage.sh
```

### Local Playwright process

Playwright tests in this repo expect the app to already be running.

Start the app and run UI tests:

```bash
dotnet run --project src/LeanKernel.Gateway/LeanKernel.Gateway.csproj --urls "http://127.0.0.1:5080"
```

In another shell:

```bash
LEANKERNEL_BASE_URL="http://127.0.0.1:5080" dotnet test test/LeanKernel.Tests.Playwright/LeanKernel.Tests.Playwright.csproj
```

One-shot helper (start app, run tests, stop app):

```bash
dotnet run --project "src/LeanKernel.Gateway/LeanKernel.Gateway.csproj" --urls "http://127.0.0.1:5080" > /tmp/leankernel-gateway.log 2>&1 & APP_PID=$!; for i in {1..60}; do if curl -sSf "http://127.0.0.1:5080/" >/dev/null; then break; fi; sleep 1; done; LEANKERNEL_BASE_URL="http://127.0.0.1:5080" dotnet test "test/LeanKernel.Tests.Playwright/LeanKernel.Tests.Playwright.csproj"; TEST_EXIT=$?; kill $APP_PID; wait $APP_PID 2>/dev/null; exit $TEST_EXIT
```

## Runtime/Config Notes

- Main host config is under `src/LeanKernel.Gateway/appsettings*.json` with runtime overlays.
- Docker stack includes `database`, `litellm`, `gbrain`, and related services.
- Running the gateway outside docker will log probe/connectivity warnings if those services are unavailable; this is expected for local UI-only runs.

## Documentation Hygiene

- If behavior/config/API changes, update:
  - `README.md`
  - relevant docs in `docs/features`, `docs/skills`, `docs/development`, and `docs/plans`.
- Keep docs implementation-accurate; mark future work explicitly as roadmap/plan.
