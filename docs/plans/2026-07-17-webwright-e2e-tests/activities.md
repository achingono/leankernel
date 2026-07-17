# Phase 18A Activities

## Step-By-Step Activities
1. Create a Webwright-focused, opt-in docker E2E test class under `test/LeanKernel.Tests.Playwright`.
2. Implement MCP helpers for streamable HTTP transport (`initialize`, `tools/list`, `tools/call`, session header handling).
3. Validate run lifecycle by calling `browser_run_task`, polling `browser_get_run`, and retrieving `browser_get_artifact`.
4. Add preflight checks for Webwright health endpoint and MCP endpoint reachability.
5. Update `docs/development/build-and-test.md` with execution command and environment contract.
6. Run targeted tests for new Webwright E2E and existing GBrain docker lifecycle E2E.
7. Run coverage verification and confirm overall threshold remains at or above 80%.
8. Run `scripts/quality/sonarqube-scan.sh` and resolve all Blocker/Critical/Major issues.
9. Run deep-review sub-agent and address reported issues.

## Review Focus
- MCP session management correctness (`mcp-session-id`, Accept headers, SSE parsing).
- Stability and deterministic behavior of artifact assertions.
- Clarity and completeness of test run documentation.
- Verification evidence completeness (coverage, SonarQube, deep review).
