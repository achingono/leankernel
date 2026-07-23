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
| `Identity` | Anonymous user defaults, token/OpenID settings, trusted proxy networks, and identity-claims prompt-context controls. |
| `Files` | Root data path, scratch directory, download limits, and Python executable path. |
| `Cors` | Local policy settings. |
| `GBrain` | MCP base URL, auth token, timeout. |

## OpenAI Subsections

`OpenAI` currently includes two important nested model configs:

- `Memory`
- `FactExtraction`

These are used by the logic-layer memory pipeline.

## Agents Subsections

`Agents` currently includes tool runtime under `Agents:Tools` and model telemetry under
`Agents:Telemetry`.

| Key | Purpose | Appsettings default |
|-----|---------|--------------------|
| `Agents:Tools:Enabled` | Master switch for the tool runtime | `true` |
| `Agents:Tools:WebSearch:Provider` | Reserved provider preference key (runtime currently uses API-key availability fallback) | `brave` |
| `Agents:Tools:WebSearch:ApiKeyEnv` | Env var holding the Brave API key | `BRAVE_API_KEY` |
| `Agents:Tools:WebSearch:AllowHosts` | Reserved web-search allowlist key | `["api.search.brave.com", "api.duckduckgo.com"]` |
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
| `Agents:Tools:McpServers` | Pre-configured MCP server definitions discovered at startup | see `appsettings.json` |
| `Agents:Tools:McpServers[i]:Name` | MCP server name used for categorization and logs | required |
| `Agents:Tools:McpServers[i]:Endpoint` | MCP HTTP/SSE endpoint URL | required |
| `Agents:Tools:McpServers[i]:Enabled` | Enables discovery for this MCP server | `true` |
| `Agents:Tools:McpServers[i]:TransportMode` | MCP transport mode (`AutoDetect`, `StreamableHttp`, `Sse`) | `AutoDetect` |
| `Agents:Tools:McpServers[i]:ConnectionTimeoutSeconds` | MCP connection timeout for discovery/probes | `30` |
| `Agents:Tools:McpServers[i]:Required` | Fails startup discovery when true and server is unreachable | `false` |
| `Agents:Tools:DocumentIngestion:Enabled` | Enables document ingestion queue/hosted services | `false` |
| `Agents:Tools:DocumentIngestion:MaxConcurrentJobs` | Max concurrent ingestion jobs | `3` |
| `Agents:Tools:DocumentIngestion:QueueCapacity` | Capacity of the ingestion queue | `100` |
| `Agents:Tools:DocumentIngestion:EnqueueTimeoutSeconds` | Queue enqueue timeout | `30` |
| `Agents:Tools:DocumentIngestion:WatchSettleDelaySeconds` | Settle delay for watch-folder stability detection | `2` |
| `Agents:Tools:DocumentIngestion:WatchMaxRetries` | Max stability retries per watched file before giving up | `3` |
| `Agents:Tools:DocumentIngestion:WatchRetryBaseDelaySeconds` | Base delay used for watch-folder retry backoff | `1` |
| `Agents:Tools:DocumentIngestion:WatchRetryMaxDelaySeconds` | Max delay used for watch-folder retry backoff | `60` |
| `Agents:Telemetry:Enabled` | Enables capture and persistence of model/token/cost telemetry | `true` |
| `Agents:Telemetry:Currency` | Currency code for reported/estimated cost | `USD` |
| `Agents:Telemetry:RetainRawMetadata` | Keeps room for retaining source metadata fields alongside structured values | `true` |
| `Agents:Telemetry:UseCostEstimate` | Falls back to token-based estimates when provider cost is missing | `true` |
| `Agents:Telemetry:CostEstimate:CostPer1kInputTokens` | Model-to-rate map for input token estimates | see `appsettings.json` |
| `Agents:Telemetry:CostEstimate:CostPer1kOutputTokens` | Model-to-rate map for output token estimates | see `appsettings.json` |

The current implementation does not use a top-level `LeanKernel` configuration root. New runtime settings should extend the existing top-level sections rather than introducing `LeanKernel:*` duplicates.

Document-ingestion note: `WatchSettleDelaySeconds` and `WatchMaxRetries` are actively used by watch-folder stability checks. Some other ingestion settings are currently validated/configured for future enforcement and may not affect runtime behavior yet.

## Files

| Key | Purpose | Appsettings default |
|-----|---------|--------------------|
| `Files:RootPath` | Root path for gateway-managed files and filesystem tool boundary | `"./data"` |
| `Files:ScratchRoot` | Root directory for temporary or scratch file operations | `"./scratch"` |
| `Files:MaxDownloadBytes` | Max size in bytes for a single file download | `10000000` |
| `Files:MaxExtractedCharacters` | Max characters to extract from a file | `20000` |
| `Files:PythonExecutable` | Python executable path for advanced file processing | `python3` |
| `Files:WatchFolders` | List of watch folder configurations for document library ingestion | `[]` |
| `Files:WatchFolders[i]:Path` | Directory path to monitor for new files | required |
| `Files:WatchFolders[i]:FilePattern` | File glob pattern (e.g. `*.pdf`) | `*.*` |
| `Files:WatchFolders[i]:TenantId` | Tenant identity for ingested documents | required |
| `Files:WatchFolders[i]:UserId` | User identity for ingested documents | required |
| `Files:WatchFolders[i]:PersonId` | Person identity for ingested documents | required |
| `Files:WatchFolders[i]:ChannelId` | Channel identity for ingested documents | required |
| `Files:WatchFolders[i]:AvailabilityScope` | Document scope (`Tenant`, `User`, or `Channel`) | `User` |
| `Files:UploadMaxFileSizeBytes` | Reserved upload-size setting currently present in `appsettings.json` | `10485760` |
| `Files:UploadAllowedContentTypes` | Reserved upload content-type list currently present in `appsettings.json` | see `appsettings.json` |

## Identity

| Key | Purpose | Default |
|-----|---------|---------|
| `Identity:TrustedProxies` | Known proxy/network CIDR ranges for forwarded headers | `[]` |

## CORS

The gateway currently applies a permissive in-code CORS policy named `AllowLocal` (allow any origin/method/header). `Cors:Policy:*` settings exist in appsettings but are not yet wired as runtime policy inputs.

## Identity Claims Context

`Identity:ClaimsContext` controls how authenticated claims are persisted and rendered into per-turn AI context.

| Key | Purpose | Default |
|-----|---------|---------|
| `Identity:ClaimsContext:Enabled` | Enables claim-profile refresh and prompt injection | `true` |
| `Identity:ClaimsContext:AllowedCustomClaims` | Deny-by-default allowlist of custom claim names to persist | `[]` |
| `Identity:ClaimsContext:PromptFields` | Ordered field allowlist for identity prompt rendering | `full_name,email,preferred_username,locale,timezone,organization,roles,groups,custom_claims` |
| `Identity:ClaimsContext:MaxRoles` | Upper bound for persisted/rendered role values | `10` |
| `Identity:ClaimsContext:MaxGroups` | Upper bound for persisted/rendered group values | `20` |
| `Identity:ClaimsContext:MaxCustomClaimValuesPerClaim` | Upper bound for values per allowlisted custom claim | `5` |
| `Identity:ClaimsContext:MaxPromptTokens` | Max estimated token budget for rendered identity block | `256` |

Startup validation enforces non-negative role/group/custom-claim bounds and requires `MaxPromptTokens > 0`.

Code anchors:

- [`../../src/Common/LeanKernel.Logic/Configuration/MemorySettings.cs`](../../src/Common/LeanKernel.Logic/Configuration/MemorySettings.cs)
- [`../../src/Common/LeanKernel.Logic/Configuration/FactExtractionSettings.cs`](../../src/Common/LeanKernel.Logic/Configuration/FactExtractionSettings.cs)
- [`../../src/Common/LeanKernel.Logic/Configuration/ToolSettings.cs`](../../src/Common/LeanKernel.Logic/Configuration/ToolSettings.cs)
- [`../../src/Common/LeanKernel.Logic/Configuration/IdentityClaimsContextSettings.cs`](../../src/Common/LeanKernel.Logic/Configuration/IdentityClaimsContextSettings.cs)
- [`../../src/Common/LeanKernel.Logic/Configuration/FileSettings.cs`](../../src/Common/LeanKernel.Logic/Configuration/FileSettings.cs)
- [`../../src/Common/LeanKernel.Logic/Configuration/DocumentIngestionToolSettings.cs`](../../src/Common/LeanKernel.Logic/Configuration/DocumentIngestionToolSettings.cs)
- [`../../src/Services/LeanKernel.Gateway/Program.cs`](../../src/Services/LeanKernel.Gateway/Program.cs)

## Provider Selection Notes

Database provider selection is not hardcoded to one backend. The gateway resolves the first configured supported connection string in this order:

1. `SqlServer`
2. `Postgres`
3. `Sqlite`

Reference: [`../../src/Services/LeanKernel.Gateway/Extensions/DbContextOptionsBuilderExtensions.cs`](../../src/Services/LeanKernel.Gateway/Extensions/DbContextOptionsBuilderExtensions.cs)
