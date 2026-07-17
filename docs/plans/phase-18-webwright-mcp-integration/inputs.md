# Phase 18 Inputs

## Required Inputs

| Input | Source | Owner |
|---|---|---|
| Official MCP C# SDK package and docs | `ModelContextProtocol` NuGet + SDK documentation | Development Team |
| MCP package selection decision | ADR/plan note documenting `ModelContextProtocol` vs `ModelContextProtocol.Core` rationale | Architecture Review |
| Community Webwright MCP package details | Package registry + repository docs + pinned version | Development Team |
| Current GBrain MCP client implementation | `src/Services/LeanKernel.Gateway/Memory/GBrainMcpClient.cs` | Gateway Team |
| LeanKernel tool registry interface | `src/Common/LeanKernel.Logic/Tools/IToolRegistry.cs` | Logic Team |
| Current browser-specific Playwright tool implementations | `src/Common/LeanKernel.Logic/Tools/BuiltIn/Browser/` | Logic Team |
| Docker compose configuration | `docker-compose.yml` | DevOps Team |
| Tool registration patterns | `src/Common/LeanKernel.Logic/Extensions/IServiceProviderExtensions.cs` | Logic Team |
| Approved list of MCP server endpoints | Pre-configured HTTP/SSE endpoints and rollout ownership | Architecture Review |
| Webwright-only rollout policy | Criteria and escalation triggers for considering future direct Playwright exposure | Architecture Review |

## Optional Inputs
- MCP protocol specification and HTTP/SSE transport details
- Examples of other MCP servers that could be integrated
- Performance requirements for startup-time tool discovery and invocation
- Future-phase benchmark scenarios for Webwright vs direct Playwright

## Input Validation Checklist
- [ ] All required inputs are current (not from a superseded version)
- [ ] No required input is missing or in draft state
- [ ] Official MCP SDK package/version is selected and documented
- [ ] SDK package-selection rationale is approved (`ModelContextProtocol` default, `Core` only if justified)
- [ ] Community Webwright MCP package is version pinned and governance risk is acknowledged
- [ ] Approved MCP server list is limited to pre-configured HTTP/SSE endpoints
- [ ] Webwright-only rollout policy and reassessment triggers are approved
- [ ] Current browser-specific Playwright tool implementations are documented for replacement
