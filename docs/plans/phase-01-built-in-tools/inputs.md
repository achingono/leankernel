# Phase 01 Inputs

## Required Inputs

| Input | Source | Owner |
|---|---|---|
| Gateway startup and agent registration path | `src/Services/LeanKernel.Gateway/Programs.cs`, `src/Common/LeanKernel.Logic/Extensions/IServiceCollectionExtensions.cs` | OpenCode |
| Current runtime configuration shape | `src/Services/LeanKernel.Gateway/appsettings.json`, `src/Services/LeanKernel.Gateway/appsettings.Development.json`, `docker-compose.yml` | OpenCode |
| Observed runtime failure evidence | `docker logs 5f90d40eaa6a7142b838f478c681d69aa75641d48948abf5ca9971b77d8b2978` | OpenCode |
| Current GBrain memory-only integration | `src/Services/LeanKernel.Gateway/Providers/GBrainMemoryClient.cs`, `src/Common/LeanKernel.Logic/Providers/MemoryProvider.cs` | OpenCode |
| Current GBrain callable capability validation | Current gateway logs plus explicit MCP capability check for read/list-style knowledge operations | OpenCode |
| Older LeanKernel tool runtime reference | `~/source/repos/leankernel/src/LeanKernel.Tools/*`, `~/source/repos/leankernel/src/LeanKernel.Plugins/BuiltIn/Skills/*`, `~/source/repos/leankernel/src/LeanKernel.Agents/*` | OpenCode |
| Older GBrain knowledge-service reference | `~/source/repos/leankernel/src/LeanKernel.Knowledge/GBrainKnowledgeService.cs`, `~/source/repos/leankernel/src/LeanKernel.Abstractions/Interfaces/IKnowledgeService.cs`, `~/source/repos/leankernel/src/LeanKernel.Tools/BuiltIn/Knowledge/*` | OpenCode |
| Older dynamic-tool loading + HTTP/egress/secret reference | `~/source/repos/leankernel/src/LeanKernel.Plugins/BuiltIn/Skills/SkillParser.cs`, `SkillDefinition.cs`, `DynamicSkillTool.cs`, `SkillExtensions.cs` | OpenCode |
| Older scoped built-in tool reference | `~/source/repos/leankernel/src/LeanKernel.Tools/BuiltIn/Internet/WebSearchTool.cs`, `~/source/repos/leankernel/src/LeanKernel.Tools/BuiltIn/FileSystem/FileSearchTool.cs`, `ToolRegistry.cs`, `ToolGovernancePolicy.cs` | OpenCode |
| Older MAF tool adaptation reference | `~/source/repos/leankernel/src/LeanKernel.Agents/Orchestration/ToolDefinitionAIToolAdapter.cs` | OpenCode |
| MAF local function/tooling surface | `Microsoft.Agents.AI` XML docs, `Microsoft.Extensions.AI` XML docs from local NuGet cache | OpenCode |
| Hosted tool product guidance used only for disqualification | Microsoft Learn pages for web search, file search, code interpreter, and providers overview | Microsoft Learn |
| `SKILL.md` manifest schema (Phase 01) | `activities.md` Appendix A, grounded in the older `SkillParser`/`DynamicSkillTool` behavior | OpenCode |

## Optional Inputs
- A test provider route through LiteLLM for manual end-to-end tool execution verification.
- A sample `SKILL.md` conforming to Appendix A, used to validate startup loading and egress/secret handling.

## Input Validation Checklist
- [x] All required inputs are current (not from a superseded version)
- [x] No required input is missing or in draft state
- [x] The `SKILL.md` manifest schema is defined (Appendix A) rather than left to implementation
- [x] The `web_search` backend and its egress/secret configuration are specified under `Agents:*` rather than `OpenAI:*` (Appendix B)
- [x] The scoped-execution model for startup-registered tools is defined (Appendix C)
- [x] The GBrain callable-capability pre-check is defined (Appendix D)
