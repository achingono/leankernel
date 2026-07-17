# Phase 18 Activities

## Step-By-Step Activities

1. **Analyze MCP Protocol and Transport**
   - Research MCP protocol specification and HTTP/SSE transport behavior
   - Review official MCP C# SDK capabilities and package selection guidance
   - Review community Webwright MCP package capabilities, release cadence, and maintenance model
   - Study existing GBrain MCP client implementation for HTTP transport patterns
   - Identify required LeanKernel adapter boundaries without re-implementing SDK protocol plumbing

2. **Design SDK-First MCP Integration Architecture**
   - Select `ModelContextProtocol` package by default (`ModelContextProtocol.Core` only with documented rationale)
   - Define LeanKernel-specific adapter interfaces around SDK client abstractions (not custom protocol clients)
   - Plan startup-time tool discovery and registration mechanism using SDK discovery APIs
   - Design configuration model for pre-configured MCP servers only

3. **Implement MCP SDK Integration**
   - Add official MCP SDK package reference to relevant projects
   - Implement LeanKernel MCP adapter layer in `src/Common/LeanKernel.Logic/Mcp/`
   - Configure HTTP/SSE MCP connections through SDK-supported transports
   - Add MCP client configuration models and settings
   - Implement tool discovery mapping from SDK models to LeanKernel `ToolDefinition` adapters
   - Avoid custom JSON-RPC DTOs/parsers unless a documented SDK gap requires a narrow extension

4. **Integrate Webwright MCP into Agent Tool Chain**
   - Configure community Webwright MCP package as a pre-configured MCP server endpoint
   - Add Webwright MCP server/service wiring in local runtime and docker-compose where applicable
   - Test startup-time tool discovery and registration from the pre-configured Webwright MCP endpoint
   - Verify discovered Webwright tools work correctly through tool registry and agent invocation paths

5. **Establish Webwright-Only Exposure Policy**
   - Document the rollout decision to expose only Webwright MCP tools to agents for this phase
   - Define reassessment triggers for future direct Playwright consideration (critical capability gaps, sustained reliability issues)
   - Capture contingency activation steps for a later phase if reassessment triggers are met

6. **Remove Custom Playwright Implementation**
   - Remove `PlaywrightToolDefinitions.cs`, `IPlaywrightClient.cs`, `PlaywrightClient.cs`, `PlaywrightModels.cs`
   - Remove `PlaywrightSidecarSettings` from `ToolSettings.cs`
   - Remove Playwright client DI registration from `IServiceCollectionExtensions.cs`
   - Remove Playwright tool registration from `IServiceProviderExtensions.cs`
   - Remove Playwright configuration from `appsettings.json`
   - Remove `playwright-sidecar` service from `docker-compose.yml`

7. **Update Browser Tool Definitions**
   - Update `BrowserToolDefinitions.cs` descriptions to reference MCP-based Webwright tools
   - Clarify that direct Playwright MCP tools are not exposed in this phase
   - Ensure Webwright disambiguation is clear

8. **Add Configuration and DI**
   - Add MCP server configuration under the existing `Agents:Tools` configuration shape in `appsettings.json`
   - Register SDK-backed MCP services and LeanKernel-owned tool adapters in DI container
   - Ensure agent-facing tool registration includes Webwright MCP tools only
   - Add health checks for MCP servers
   - Update tool registration to support startup-time registration from pre-configured MCP servers only

9. **Testing and Validation**
   - Unit tests for SDK adapter implementations
   - Integration tests with mock MCP servers
   - End-to-end tests with Webwright MCP
   - Validate that no custom MCP protocol stack is introduced where SDK support exists
   - Validate that direct Playwright MCP tools are not exposed to agents in this phase
   - Verify all existing tests pass
   - Run code coverage and quality checks

10. **Documentation Updates**
   - Update `docs/features/browser-tool.md` to reflect MCP-based implementation
   - Document Webwright-only exposure decision and future reassessment triggers
   - Update configuration documentation
   - Update architecture documentation
   - Update `docs/plans/index.md` with new phase

## Review Focus
- MCP client architecture and HTTP/SSE-only transport abstraction
- Startup-time tool discovery and registration mechanism for Webwright MCP integration
- Removal of the custom Playwright sidecar and browser-specific Playwright tool implementations without breaking existing functionality
- Configuration model for pre-configured MCP servers
- Enforced Webwright-only agent exposure and reassessment trigger quality
- Error handling and health check implementation
