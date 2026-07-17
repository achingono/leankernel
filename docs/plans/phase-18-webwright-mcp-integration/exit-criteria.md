# Phase 18 Exit Criteria

## Gate Checklist
- [ ] Official MCP SDK package is integrated (`ModelContextProtocol` default, `ModelContextProtocol.Core` only with documented rationale)
- [ ] SDK abstractions are used for HTTP/SSE MCP transport/protocol behavior (no bespoke JSON-RPC MCP stack)
- [ ] SDK-backed MCP integration connects to Webwright MCP and discovers tools
- [ ] Discovered tools from pre-configured MCP servers appear in LeanKernel tool registry through LeanKernel-owned adapters
- [ ] Discovered tools are callable through standard tool invocation
- [ ] Agent tool chain exposes Webwright MCP tools only in this phase
- [ ] MCP scope remains limited to pre-configured HTTP/SSE server endpoints only
- [ ] Webwright-only decision is approved with documented reassessment triggers for future direct Playwright consideration
- [ ] The custom Playwright sidecar and browser-specific Playwright C# tool implementations are completely removed
- [ ] All existing tests pass without modification
- [ ] Code coverage meets 80% threshold
- [ ] No new quality gate failures (SonarQube Blocker/Critical/Major)
- [ ] Documentation updated to reflect MCP-based implementation
- [ ] Docker compose builds and runs successfully with MCP configuration

## Approval Table

| Role | Name | Status | Notes |
|---|---|---|---|
| Owner | Development Team | Pending | |
| Reviewer | Architecture Review | Pending | |
| Approver | Technical Lead | Pending | |
