# Phase 18 Inputs

## Required Inputs

| Input | Source | Owner |
|---|---|---|
| Official MCP C# SDK package and docs | `ModelContextProtocol` NuGet 1.4.0 + SDK documentation | Development Team |
| MCP package selection decision | `ModelContextProtocol` chosen; `ModelContextProtocol.Core` not required | Architecture Review |
| Community Webwright MCP package details | Webwright MCP service/container in `docker-compose.yml` and runtime config | Development Team |
| Current GBrain MCP client implementation | `src/Services/LeanKernel.Gateway/Memory/GBrainMcpClient.cs` | Gateway Team |
| LeanKernel tool registry interface | `src/Common/LeanKernel.Logic/Tools/IToolRegistry.cs` | Logic Team |
| Current browser-specific Playwright tool implementations | Replaced by Webwright MCP adapters under `src/Common/LeanKernel.Logic/Mcp/` | Logic Team |
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
- [x] All required inputs are current (not from a superseded version)
- [x] No required input is missing or in draft state
- [x] Official MCP SDK package/version is selected and documented
- [x] SDK package-selection rationale is approved (`ModelContextProtocol` default, `Core` only if justified)
- [x] Community Webwright MCP package is version pinned and governance risk is acknowledged
- [x] Approved MCP server list is limited to pre-configured HTTP/SSE endpoints
- [x] Webwright-only rollout policy and reassessment triggers are approved
- [x] Current browser-specific Playwright tool implementations are documented for replacement
