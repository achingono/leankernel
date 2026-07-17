# Phase 18 Evidence

## Evidence Log

| Item | Reference | Notes |
|---|---|---|
| MCP Protocol Specification | https://spec.modelcontextprotocol.io/ | Official MCP specification |
| MCP C# SDK NuGet Package | https://www.nuget.org/packages/ModelContextProtocol | Official SDK package (default selection) |
| MCP C# SDK Documentation | https://modelcontextprotocol.github.io/csharp-sdk/ | API and package guidance |
| Community Webwright MCP Package | `docker-compose.yml` Webwright service + `config/webwright/` runtime | Community-maintained browser automation service |
| Approved MCP Endpoint List | `docker-compose.yml` and `src/Common/LeanKernel.Logic/Configuration/McpSettings.cs` | Initial rollout limited to pre-configured HTTP/SSE endpoints |
| Playwright MCP Server | `@anthropic-ai/playwright-mcp` npm package | Contingency reference for potential future phase |
| GBrain MCP Client | `src/Services/LeanKernel.Gateway/Memory/GBrainMcpClient.cs` | Existing MCP client pattern |
| Tool Registry Interface | `src/Common/LeanKernel.Logic/Tools/IToolRegistry.cs` | Tool registration mechanism |
| Webwright MCP Adapter Layer | `src/Common/LeanKernel.Logic/Mcp/` | LeanKernel-owned discovery and invocation adapters |
| Current Browser-Specific Playwright Tool Implementations | Removed from the runtime | Replaced by Webwright MCP tools |

## Test Evidence
- Unit test results: `dotnet build src/LeanKernel.sln` passed after MCP adapter updates
- Integration test results: `LEANKERNEL_DOCKER_E2E_ENABLED=true dotnet test test/LeanKernel.Tests.Playwright/LeanKernel.Tests.Playwright.csproj --filter "FullyQualifiedName~DockerWebwrightE2ETests"` passed
- End-to-end test results: `LEANKERNEL_DOCKER_E2E_ENABLED=true dotnet test test/LeanKernel.Tests.Playwright/LeanKernel.Tests.Playwright.csproj --filter "FullyQualifiedName~RunningDockerDeployment_GatewayWebwrightToolCallsSucceed"` passed
- Code coverage report: [pending; not regenerated in this documentation update]

## Review Evidence
- Architecture review: reflected in `docs/plans/phase-18-webwright-mcp-integration/index.md`
- Code review: reflected in `src/Common/LeanKernel.Logic/Mcp/`
- Quality gate results: `dotnet build src/LeanKernel.sln` passed; Docker E2E tests passed
- SDK package decision record (`ModelContextProtocol` vs `ModelContextProtocol.Core`): `ModelContextProtocol` selected in `src/Common/LeanKernel.Logic/LeanKernel.Logic.csproj`
- Webwright-only decision record with reassessment triggers: captured in `docs/features/tool-runtime.md` and `docs/operations/tool-configuration.md`
