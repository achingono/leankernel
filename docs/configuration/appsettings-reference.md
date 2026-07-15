# Appsettings Reference

The gateway reads configuration primarily from:

- [`../../src/Services/LeanKernel.Gateway/appsettings.json`](../../src/Services/LeanKernel.Gateway/appsettings.json)
- [`../../src/Services/LeanKernel.Gateway/appsettings.Development.json`](../../src/Services/LeanKernel.Gateway/appsettings.Development.json)

## Current Sections

| Section | Purpose |
|---|---|
| `ConnectionStrings` | Database provider inputs. Local defaults use SQLite. |
| `OpenAI` | Base model endpoint, API key, default model, memory model settings, fact extraction model settings. |
| `Agents` | Default agent metadata, root path, and nested tool runtime settings. |
| `Identity` | Anonymous user defaults plus token/OpenID settings. |
| `Files` | Root data path. |
| `Cors` | Local policy settings. |
| `GBrain` | MCP base URL, auth token, timeout. |

## OpenAI Subsections

`OpenAI` currently includes two important nested model configs:

- `Memory`
- `FactExtraction`

These are used by the logic-layer memory pipeline.

## Agents Subsections

`Agents` currently includes the tool runtime branch under `Agents:Tools`.

| Key | Purpose | Default |
|-----|---------|---------|
| `Agents:Tools:Enabled` | Master switch for the tool runtime | `true` |
| `Agents:Tools:WebSearch:Provider` | Preferred web-search backend (`brave` or `duckduckgo`) | `brave` |
| `Agents:Tools:WebSearch:ApiKeyEnv` | Env var holding the Brave API key | `BRAVE_API_KEY` |
| `Agents:Tools:WebSearch:AllowHosts` | Egress allowlist for web search | `["api.search.brave.com", "api.duckduckgo.com"]` |
| `Agents:Tools:AllowedToolNames` | Name allowlist (takes precedence when non-empty) | `[]` (all allowed) |
| `Agents:Tools:AllowedCategories` | Category allowlist (applied when name list is empty) | `[]` (all allowed) |
| `Agents:Tools:SkillBasePaths` | Directories scanned for `SKILL.md` at startup | `["/app/data/skills"]` |
| `Agents:Tools:DynamicHttp:AllowHosts` | Global egress ceiling layered over per-skill `egress.allowHosts` | `[]` |
| `Agents:Tools:BuiltIns:Calculation:Enabled` | Enables deterministic local calculation/aggregation helpers | `true` |
| `Agents:Tools:BuiltIns:Calculation:MaxInputItems` | Upper bound for aggregate/group/count inputs | `1000` |

The current implementation does not use a top-level `LeanKernel` configuration root. New runtime settings should extend the existing top-level sections rather than introducing `LeanKernel:*` duplicates.

Code anchors:

- [`../../src/Common/LeanKernel.Logic/Configuration/MemorySettings.cs`](../../src/Common/LeanKernel.Logic/Configuration/MemorySettings.cs)
- [`../../src/Common/LeanKernel.Logic/Configuration/FactExtractionSettings.cs`](../../src/Common/LeanKernel.Logic/Configuration/FactExtractionSettings.cs)
- [`../../src/Common/LeanKernel.Logic/Configuration/ToolSettings.cs`](../../src/Common/LeanKernel.Logic/Configuration/ToolSettings.cs)

## Provider Selection Notes

Database provider selection is not hardcoded to one backend. The gateway resolves the first configured supported connection string in this order:

1. `Postgres`
2. `SqlServer`
3. `Sqlite`

Reference: [`../../src/Services/LeanKernel.Gateway/Extensions/DbContextOptionsBuilderExtensions.cs`](../../src/Services/LeanKernel.Gateway/Extensions/DbContextOptionsBuilderExtensions.cs)
