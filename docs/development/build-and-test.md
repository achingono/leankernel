# Build and Test

This page documents the current developer commands for the implemented runtime.

## Solution Build

From the repository root:

```bash
dotnet build LeanKernel.sln
```

App-only solution:

```bash
dotnet build src/LeanKernel.sln
```

## Focused App Builds

```bash
dotnet build src/Common/LeanKernel.Logic/LeanKernel.Logic.csproj
dotnet build src/Services/LeanKernel.Gateway/LeanKernel.Gateway.csproj
```

## Test Projects

Current test projects:

- `test/LeanKernel.Tests.Unit`
- `test/LeanKernel.Tests.Integration`
- `test/LeanKernel.Tests.Playwright`

## Docker Deployment E2E Lifecycle Test

The Playwright test project now includes a docker-targeted lifecycle e2e that validates:

- test user creation in Postgres
- seeded memory insertion in GBrain
- request execution through `/v1/responses`
- seeded memory retrieval in the response
- post-turn memory persistence in GBrain

This test is opt-in and only runs when explicitly enabled:

```bash
LEANKERNEL_DOCKER_E2E_ENABLED=true \
dotnet test test/LeanKernel.Tests.Playwright/LeanKernel.Tests.Playwright.csproj --filter "FullyQualifiedName~DockerLifecycleE2ETests"
```

Optional environment variables:

- `LEANKERNEL_E2E_GATEWAY_URL` (default `http://localhost:8080`)
- `LEANKERNEL_E2E_GBRAIN_URL` (default `http://localhost:8789`)
- `LEANKERNEL_E2E_POSTGRES_CONNECTION` (default local docker compose Postgres)
- `LEANKERNEL_E2E_MODEL` (default `medium`)
- `LEANKERNEL_E2E_GBRAIN_AUTH_TOKEN` (optional bearer token for GBrain MCP)
- `LEANKERNEL_E2E_GBRAIN_TOKEN_FILE` (optional token-file path; defaults to `data/gbrain/.engine-token` when present)
- `LEANKERNEL_E2E_REQUIRE_PERSISTENCE` (optional; set `true` to fail when post-turn persistence marker is not observed)

## Docker Webwright MCP E2E Test

The Playwright test project includes an opt-in docker-targeted Webwright MCP e2e that validates:

- Webwright MCP session initialization over `/mcp`
- MCP tool discovery (`tools/list`)
- browser run lifecycle (`browser_run_task` -> `browser_get_run`)
- artifact retrieval and PNG payload validation (`browser_get_artifact`)

This test is opt-in and only runs when explicitly enabled:

```bash
docker compose up -d webwright playwright

LEANKERNEL_DOCKER_E2E_ENABLED=true \
dotnet test test/LeanKernel.Tests.Playwright/LeanKernel.Tests.Playwright.csproj --filter "FullyQualifiedName~DockerWebwrightE2ETests"
```

Optional environment variables:

- `LEANKERNEL_E2E_WEBWRIGHT_URL` (default `http://localhost:8000`)
- `LEANKERNEL_E2E_WEBWRIGHT_RUN_TIMEOUT_SECONDS` (default `90`)

Note: the test-level timeout is `LEANKERNEL_E2E_WEBWRIGHT_RUN_TIMEOUT_SECONDS + 120 seconds`.

## Docker Gateway Webwright Tool-Call E2E Test

The Playwright test project also includes an opt-in gateway-level e2e that validates the
model-triggered tool chain end-to-end:

- `/v1/responses` requests include `agent.name = "leankernel"`
- the gateway discovers Webwright MCP tools at startup
- the response path successfully invokes Webwright browser tools through the gateway
- the completion text contains the expected Webwright result marker

This test is opt-in and only runs when explicitly enabled:

```bash
LEANKERNEL_DOCKER_E2E_ENABLED=true \
dotnet test test/LeanKernel.Tests.Playwright/LeanKernel.Tests.Playwright.csproj --filter "FullyQualifiedName~RunningDockerDeployment_GatewayWebwrightToolCallsSucceed"
```

## Coverage Gate

```bash
scripts/quality/test-coverage.sh
```

Reference: [`../../scripts/quality/test-coverage.sh`](../../scripts/quality/test-coverage.sh)

## Sonar Workflows

Supporting quality scripts live under:

- `scripts/quality/sonarqube-scan.sh`
- `scripts/quality/sonarqube-summary.sh`
- `scripts/quality/sonarqube-create-user.sh`

Run the local Sonar scan with quality-gate wait:

```bash
scripts/quality/sonarqube-scan.sh
```

## Current Note About Full Solution Verification

The app projects build cleanly. If full-solution verification fails, inspect the unit test project first for stale namespace references before assuming the runtime projects are broken.
