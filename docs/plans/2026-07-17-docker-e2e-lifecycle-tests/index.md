# Phase 17 Docker E2E Lifecycle Tests

## Companion Documents
- [Inputs](inputs.md)
- [Activities](activities.md)
- [Outputs](outputs.md)
- [Exit Criteria](exit-criteria.md)
- [Risk Register](risk-register.md)
- [Evidence](evidence.md)

## Objective
Add executable end-to-end tests that run against an already running Docker deployment and validate full request lifecycle behavior: identity provisioning, memory seeding, request execution, memory retrieval validation, and post-turn memory persistence validation.

## Scope

## In Scope
- Add docker-targeted e2e tests in `test/LeanKernel.Tests.Playwright`.
- Provision test identities directly in Postgres for channel-authenticated flow coverage.
- Seed memory data through GBrain MCP and verify retrieval and persistence behavior.
- Document required env vars and test execution command.

## Out of Scope
- Spinning up Docker containers from the test runner.
- Reworking gateway runtime behavior or memory pipeline logic.
- Introducing CI orchestration for external dependency provisioning.

## Entry Criteria
- Docker stack is already running and healthy (`gateway`, `database`, `gbrain`).
- Local test runner can connect to gateway, gbrain, and postgres endpoints.
- Existing Playwright test project builds successfully.

## Exit Criteria
All checks in `exit-criteria.md` are complete.

## Roles
- Owner: Coding agent
- Reviewer: Separate model/session plan reviewer
- Approver: Repository maintainer
