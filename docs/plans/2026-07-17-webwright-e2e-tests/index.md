# Phase 18A Webwright Docker E2E Tests

## Companion Documents
- [Inputs](inputs.md)
- [Activities](activities.md)
- [Outputs](outputs.md)
- [Exit Criteria](exit-criteria.md)
- [Risk Register](risk-register.md)
- [Evidence](evidence.md)

## Objective
Add docker-targeted Playwright test coverage for the Webwright MCP service so browser MCP transport and artifact flow are validated end-to-end, mirroring the existing GBrain docker lifecycle verification approach.

## Scope

## In Scope
- Add an opt-in E2E test class in `test/LeanKernel.Tests.Playwright` for Webwright MCP.
- Validate MCP session initialization, tool discovery, run lifecycle, and artifact retrieval.
- Document command and environment variables for local execution.

## Out of Scope
- Browser workflow feature expansion in the Webwright MCP bridge.
- Gateway tool orchestration behavior beyond MCP connectivity and tool availability.

## Entry Criteria
- Docker compose stack is running and healthy for `playwright` and `webwright`.
- Existing Playwright test project builds successfully.

## Exit Criteria
All new Webwright E2E tests are opt-in, repeatable, and passing in local docker execution. See `exit-criteria.md`.

## Roles
- Owner: Coding agent
- Reviewer: Secondary model session
- Approver: Repository maintainer
