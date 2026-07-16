# Tool Runtime

The tool runtime enables provider-agnostic tool execution for the `leankernel` agent
behind the `/v1/responses` surface. It is gated by `Agents:Tools:Enabled` (default `true`);
set it to `false` to restore the no-tool chat path.

## Architecture

All tools are registered at startup into a shared `IToolRegistry` (singleton). When the agent
is created, governance-filtered tools are adapted to MAF `AITool` instances via
`ToolDefinitionAIToolAdapter` and attached through `ChatOptions.Tools`. The existing
`.UseFunctionInvocation()` pipeline then executes them locally regardless of the upstream
provider behind LiteLLM.

Reference: [`../../src/Common/LeanKernel.Logic/Tools/`](../../src/Common/LeanKernel.Logic/Tools/)

## Tool Categories

### Built-in Tools

LeanKernel-owned tools executed locally with no provider dependency:

| Category | Tools |
|------|-------------|
| Search/utility | `web_search`, `file_search`, `calculate`, `count`, `sum`, `average`, `min_max`, `group_by` |
| Filesystem | `file_read`, `file_write`, `file_edit`, `file_stat`, `file_copy`, `file_move`, `file_delete`, `file_touch`, `file_chmod`, `directory_list`, `directory_create`, `extract_text` |
| Internet | `web_fetch`, `http_request` |
| Data | `database_query`, `json_transform`, `csv_xlsx_read_write` |
| Browser | `browser_run_task`, `browser_get_run`, `browser_get_artifact`, `browser_cancel_run` |

All built-in tools use per-request DI scopes (Appendix C pattern) to preserve identity
partitioning at invocation time.

### Memory/Knowledge Tools

GBrain-backed tools registered conditionally based on a startup capability pre-check:

| Tool | Description |
|------|-------------|
| `memory_search` | Search the GBrain knowledge store |
| `memory_read` | Read a specific page by key |
| `memory_write` | Create or update a page |

If the GBrain capability probe finds degraded support (e.g., search works but `get_page`
is missing), only the supported subset is registered. If GBrain is unreachable, these tools
are skipped entirely and the rest of the runtime starts normally.

Reference: [`../../src/Services/LeanKernel.Gateway/Memory/GBrainCapabilityCheck.cs`](../../src/Services/LeanKernel.Gateway/Memory/GBrainCapabilityCheck.cs)

### User-Defined Dynamic Tools

HTTP tools loaded at startup from `SKILL.md` files. Each declared operation becomes one
agent-visible tool named `{skillName}_{operationId}`.

Reference: [`../../samples/skills/`](../../samples/skills/)

## SKILL.md Manifest Format

A `SKILL.md` file uses YAML frontmatter delimited by `---`:

```yaml
---
name: weather
description: Weather lookup tools
metadata:
  category: internet
runtime:
  type: http
  baseUrl: https://api.open-meteo.com
  timeoutSeconds: 15
  auth:
    type: none
  egress:
    allowHosts:
      - api.open-meteo.com
operations:
  - id: current
    summary: Get current weather
    invoke:
      httpMethod: GET
      httpPath: /v1/forecast?latitude={latitude}&longitude={longitude}&current=temperature_2m
    parameters:
      latitude:
        type: number
        description: Latitude coordinate
        required: true
      longitude:
        type: number
        description: Longitude coordinate
        required: true
---
```

### Rules

- `runtime.type` must be `http` (Phase 01 rejects `cli`)
- Duplicate tool names (against built-ins or other skills) are rejected at startup
- Bearer secrets are resolved from `auth.secretRef` mapped to `/run/secrets/<ref>` or
  `SKILL__<REF>` environment variables -- never inline in the manifest
- The effective outbound-host policy is the intersection of the skill-local
  `egress.allowHosts` and the global `Agents:Tools:DynamicHttp:AllowHosts` when non-empty

## Safety Boundaries

- **Filesystem**: file-system tools are bounded to `Files:RootPath` via `FileSystemSupport.ResolveWithinRoot`
- **HTTP egress**: `EgressValidator` blocks loopback, private, and link-local hosts; enforces
  allowlist intersection; re-validates every redirect hop
- **Browser sidecar**: browser tools route through `IWebwrightClient` with bounded payloads and
  explicit artifact-size limits (`Agents:Tools:Webwright:MaxArtifactBytes`)
- **Governance**: `ToolGovernancePolicy` filters tools by `AllowedToolNames` (name allowlist,
  takes precedence) or `AllowedCategories` (category allowlist)
- **Master switch**: `Agents:Tools:Enabled=false` disables the entire tool runtime

## Configuration

All tool settings live under `Agents:Tools` in `appsettings.json`. See
[appsettings reference](../configuration/appsettings-reference.md) for the full key reference.

## Smoke-Test Steps

1. Start the gateway with `Agents:Tools:Enabled=true`
2. Confirm built-in tools appear in the startup log (`Tool runtime ready. N tool(s) registered`)
3. Send a `/v1/responses` request with a prompt that triggers `calculate` or `web_search`
4. Verify the response shows tool activity rather than "I cannot" or "I don't have access"
5. For dynamic tools: place a `SKILL.md` in one of the `SkillBasePaths` directories and restart
6. Confirm the dynamic tool name appears in the startup log
