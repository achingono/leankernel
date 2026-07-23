# Health and Observability

The current implementation has a small but useful operational surface.

## Health Endpoint

The gateway exposes:

- `GET /health`

This endpoint is used both for manual checks and for the Docker Compose health check.

Reference: [`../../src/Services/LeanKernel.Gateway/Program.cs`](../../src/Services/LeanKernel.Gateway/Program.cs)

## Integration Coverage

Integration and Playwright tests exercise the gateway endpoints under `test/`.

Relevant projects:

- [`../../test/LeanKernel.Tests.Integration`](../../test/LeanKernel.Tests.Integration)
- [`../../test/LeanKernel.Tests.Playwright`](../../test/LeanKernel.Tests.Playwright)

## Quality Tooling

Operational quality scripts live under `scripts/quality/` and cover:

- test coverage
- SonarQube scan orchestration
- SonarQube result summaries
- quality-gate polling (scanner waits for gate result)

## Current Observability Boundary

The runtime does not yet expose a separate diagnostics service or rich production telemetry surface. The current observability story is centered on:

- health checks
- build and test workflows
- direct code-level inspection of persisted state and provider behavior
