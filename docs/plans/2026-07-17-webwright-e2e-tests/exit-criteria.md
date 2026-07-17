# Phase 18A Exit Criteria

## Gate Checklist
- [x] New Webwright docker E2E test is implemented and committed in `test/LeanKernel.Tests.Playwright`.
- [x] Test is opt-in and does not run by default (uses same `LEANKERNEL_DOCKER_E2E_ENABLED` gate as GBrain lifecycle test).
- [x] MCP tool discovery and run lifecycle are asserted end-to-end.
- [x] `browser_get_artifact` payload is validated for metadata correctness (`image/png`, artifact id match) and decodable non-empty binary content.
- [x] `docs/development/build-and-test.md` includes execution instructions.
- [x] Targeted test execution passes locally.
- [ ] Coverage remains at or above 80%.
- [ ] SonarQube scan run completes with no Blocker/Critical/Major issues introduced.
- [x] Deep-review findings are addressed and logged.

## Approval Table

| Role | Name | Status | Notes |
| --- | --- | --- | --- |
| Owner | Coding agent | Pending | |
| Reviewer | Secondary model session | Pending | |
| Approver | Repository maintainer | Pending | |
