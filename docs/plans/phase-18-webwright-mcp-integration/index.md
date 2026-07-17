# Phase 18

## Companion Documents
- [Inputs](inputs.md)
- [Activities](activities.md)
- [Outputs](outputs.md)
- [Exit Criteria](exit-criteria.md)
- [Risk Register](risk-register.md)
- [Evidence](evidence.md)

## Objective
Integrate the official Model Context Protocol C# SDK (`ModelContextProtocol` NuGet package, with `ModelContextProtocol.Core` considered only when a slimmer dependency surface is explicitly justified) so LeanKernel can connect to pre-configured HTTP/SSE MCP servers, discover their tools, and register LeanKernel-owned tool adapters in the tool registry. Incorporate ongoing Webwright work by integrating a community-maintained Webwright MCP package into the agent tool chain, and expose only Webwright MCP tools to agents in the initial rollout. This replaces the custom Playwright sidecar and browser-specific C# tool implementations while providing a reusable SDK-first foundation for future MCP server integrations.

## Scope
### In Scope
- SDK-based MCP client integration supporting HTTP/SSE transport only
- Tool discovery and registration from pre-configured MCP servers via official SDK abstractions
- Integration with community-maintained Webwright MCP package for browser automation workflows
- Initial agent tool exposure constrained to Webwright MCP tools
- Removal of the custom Playwright sidecar and browser-specific Playwright C# tool implementations
- Configuration for pre-configured MCP server endpoints
- Health checks and error handling for MCP servers
- Documentation of future reassessment triggers for potential direct Playwright exposure in a later phase

### Out of Scope
- Re-implementing MCP protocol primitives, JSON-RPC transport wiring, or custom MCP DTOs when equivalent SDK capabilities exist
- Stdio-based MCP server process hosting or lifecycle management
- Modifying existing GBrain MCP client to use generic client (future phase)
- MCP server-side implementation
- Ad-hoc or user-supplied MCP server discovery/registration at runtime
- Authentication/authorization for MCP servers beyond basic transport security
- Direct Playwright MCP tool exposure to agents during this phase

## Entry Criteria
- Community-maintained Webwright MCP package is identified, version-pinned, and functional in a local validation run over HTTP/SSE
- Official MCP SDK package decision is documented (`ModelContextProtocol` or `ModelContextProtocol.Core` with rationale)
- Current browser-specific Playwright tool implementations are implemented and working (for replacement)
- LeanKernel tool registry supports startup-time registration of LeanKernel-owned tool adapters for tools discovered from pre-configured MCP servers
- HTTP client infrastructure exists in Gateway

## Exit Criteria
- Official MCP SDK is integrated and used as the HTTP/SSE transport/protocol foundation
- SDK-backed MCP integration connects to Webwright MCP and discovers tools
- Agent tool chain exposes Webwright MCP tools only
- Future direct Playwright reconsideration triggers are documented
- Discovered tools from pre-configured MCP servers appear in LeanKernel tool registry through LeanKernel-owned adapters and are callable
- The custom Playwright sidecar and browser-specific Playwright C# tool implementations are removed
- All existing tests pass
- Documentation updated

## Roles
- Owner: Development Team
- Reviewer: Architecture Review
- Approver: Technical Lead
