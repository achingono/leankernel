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
| `Agents:Tools:FileSystem:Enabled` | Enables filesystem tool family (`file_*`, `directory_*`, `extract_text`) | `true` |
| `Agents:Tools:Internet:Enabled` | Enables internet tool family (`web_fetch`, `http_request`) | `true` |
| `Agents:Tools:Internet:MaxRedirects` | Redirect hop ceiling for internet tools | `3` |
| `Agents:Tools:DatabaseQuery:Enabled` | Enables database query/data transform tools | `false` |
| `Agents:Tools:DatabaseQuery:MaxRows` | Maximum rows returned by `database_query` | `200` |
| `Agents:Tools:DatabaseQuery:DefaultTimeoutSeconds` | Max DB timeout ceiling for `database_query` | `30` |
| `Agents:Tools:DatabaseQuery:Connections` | Named DB connection definitions for `database_query` | `[]` |
| `Agents:Tools:Webwright:Enabled` | Enables browser tools (`browser_*`) | `false` |
| `Agents:Tools:Webwright:BaseUrl` | Browser sidecar base URL | `http://webwright:8000` |
| `Agents:Tools:Webwright:ApiToken` | Bearer token for browser sidecar calls | `""` |
| `Agents:Tools:Webwright:RequestTimeoutSeconds` | Timeout for browser sidecar requests | `15` |
| `Agents:Tools:Webwright:MaxArtifactBytes` | Model-facing maximum artifact bytes | `2000000` |
| `Agents:Tools:Webwright:DefaultModel` | Default model alias sent with browser tasks | `tool` |
| `Agents:Tools:DocumentIngestion:Enabled` | Enables document ingestion queue/hosted services | `false` |

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
