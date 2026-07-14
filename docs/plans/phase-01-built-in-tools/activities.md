# Phase 01 Activities

## Step-By-Step Activities
1. Capture the baseline failure precisely.
   Record the full causal chain: `/v1/responses` reached the gateway, the named agent ran through `Microsoft.Extensions.AI.FunctionInvokingChatClient`, the current registration path came from `OpenAIClient(...).GetChatClient(...).AsIChatClient()`, and LiteLLM received `POST /v1/chat/completions` for the failing turn. This establishes that the current runtime already uses provider-agnostic local function invocation, but it has no actual registered tool set.
2. Define the shared LeanKernel tool contract.
   Introduce shared `ToolDefinition`-style models plus registry/executor abstractions in the current solution so built-in tools and runtime-loaded custom tools use the same execution contract. Reuse MAF primitives where they fit, but keep LeanKernel-owned contracts for names, parameters, results, and governance.
3. Port the minimum built-in tool surface needed for usefulness.
   Start with provider-agnostic built-ins that reflect the older repo intent, especially `web_search`, `file_search`, deterministic `calculate` / aggregation helpers, and the GBrain-backed `wiki_search`, `wiki_read`, and `wiki_write` knowledge tools. Preserve stable tool names and parameter shapes where practical so the runtime behavior aligns with the established LeanKernel objective rather than provider-specific hosted tool semantics.
   - `web_search`: LeanKernel-owned tool that queries Brave when `Agents:Tools:WebSearch:ApiKeyEnv` (default `BRAVE_API_KEY`) is present and falls back to DuckDuckGo otherwise, matching the older repo behavior in `~/source/repos/leankernel/src/LeanKernel.Tools/BuiltIn/Internet/WebSearchTool.cs`. The provider backend and its egress targets are pinned in configuration (see Appendix B); it must not depend on any provider-hosted web tool.
   - `file_search`: LeanKernel-owned local search bounded to `Files.RootPath`, resolving every candidate path through a root-confinement helper (reference: `FileSystemSupport.ResolveWithinRoot` and `FileSearchTool.cs`), searching by filename/path and, for text-like files under a byte cap, by content with bounded depth/scan limits.
   - `calculate` / aggregation helpers: deterministic local tools used when a downstream surface does not expose native calculation, count, sum, average, min/max, grouping, or lightweight projection endpoints. These helpers operate only on explicit arguments or structured results already returned by other tools in the same turn, and they do not introduce new external egress or persistence requirements. The former repo's `JsonTransformTool` / `DatabaseQueryTool` / `CsvXlsxReadWriteTool` are inspiration for shape, but Phase 01 keeps the scope smaller: arithmetic, counting, grouping, and aggregate summarization over bounded JSON-like inputs.
   Both built-ins follow the scoped-execution pattern in Appendix C so per-request services (HTTP client, options, identity) are resolved at invocation time rather than captured at startup.
4. Introduce a callable GBrain knowledge abstraction.
   Add a dedicated knowledge-service abstraction in the current worktree for callable wiki/document operations, separate from `IMemoryClient`, modeled on `~/source/repos/leankernel/src/LeanKernel.Abstractions/Interfaces/IKnowledgeService.cs` (`SearchAsync`, `GetPageAsync`, `PutPageAsync`, and optionally `DeletePageAsync`). Reuse the existing gateway GBrain MCP transport and `GBrainAuthHandler`. Before registering any `wiki_*` tool, run the startup capability pre-check in Appendix D to confirm the callable MCP operations (`search`, `get_page`, `put_page`) exist in the deployed GBrain instance beyond the already-confirmed memory `search`/`put_page` path. Expose explicit knowledge operations so the tool runtime can answer wiki availability, retrieval, and write questions from callable tools rather than from background memory hints alone; exact count/list questions are supported only when the downstream capability probe confirms a native list/count-style operation or a bounded search result set that can be deterministically aggregated locally. `wiki_*` tools resolve identity/tenant scope at invocation time per Appendix C and pass scope-relative keys to the transport (storage scope is added by the transport layer).
5. Wire tool definitions into the named MAF agent.
   Convert registered tools into `AIFunction` / `AITool` instances and attach them through `ChatOptions.Tools` so the existing `.UseFunctionInvocation()` path can execute them locally regardless of the upstream provider behind LiteLLM. For Phase 01, run all `leankernel` agent turns on a nested `OpenAI:ToolModel` setting backed by the existing LiteLLM `tool` alias rather than introducing per-turn model-selection logic.
6. Add user-defined runtime tool loading.
   Support dynamic tool registration from user-managed `SKILL.md` definitions, using the older repo as the reference behavior. The Phase 01 agent-visible contract is one concrete LeanKernel tool per declared operation registered into the shared registry at startup, named `{skillName}_{operationId}`. The canonical manifest format is defined in Appendix A (YAML frontmatter with `runtime`, `egress`, `auth`, and `operations`). MAF skill-provider primitives may be used internally if helpful, but generic meta-tools such as skill-loading or script-running commands do not satisfy the phase objective by themselves.
7. Lock the minimum dynamic-tool runtime surface.
   Phase 01 supports startup-time loading only, `SKILL.md` as the canonical manifest format, HTTP runtime operations only (`runtime.type: http`; `cli` is rejected in Phase 01), and duplicate-name rejection against both built-in tools and other user-defined tools. CLI-backed user-defined tools and dynamic reload are deferred.
8. Add governance and safety boundaries.
   Introduce a minimal visibility/execution policy so only allowed built-in and dynamic tools are exposed to the agent, driven by `Agents:Tools:AllowedToolNames` / `Agents:Tools:AllowedCategories` (allowlist semantics: an empty list means the category gate is not applied; name allowlist takes precedence). Bound filesystem tools to `Files.RootPath` via root-confinement. Bound HTTP-based dynamic tools with per-skill `egress.allowHosts`, plus a hard block on loopback/private/link-local egress targets and a redirect policy that re-validates every hop (reference: `TryValidateEgressTarget` / `IsPrivateOrLoopbackHost` in `DynamicSkillTool.cs`). The effective dynamic-tool host allowlist is the intersection of the skill-local `egress.allowHosts` and `Agents:Tools:DynamicHttp:AllowHosts` when the global list is non-empty; when the global list is empty, the per-skill list is authoritative. Resolve dynamic-tool bearer secrets only from `runtime.auth.secretRef` mapped to `/run/secrets/<ref>` or `SKILL__<REF>` environment variables — never inline in the manifest (Appendix A). Keep GBrain-backed wiki tools behind the existing authenticated GBrain MCP transport and fail closed when GBrain is unavailable. The whole tool runtime is gated by `Agents:Tools:Enabled`, which also serves as the rollback lever back to the current no-tool chat path.
9. Add configuration beneath the existing top-level shape.
   Extend existing sections with the concrete nested contract defined in Appendix B: `OpenAI:ToolModel` for model-provider routing only, `Agents:Tools:WebSearch:*`, `Agents:Tools:Enabled`, `Agents:Tools:AllowedToolNames`, `Agents:Tools:AllowedCategories`, `Agents:Tools:SkillBasePaths`, `Agents:Tools:DynamicHttp:AllowHosts`, `Agents:Tools:BuiltIns:Calculation:*`, `Files:RootPath` as the allowed filesystem boundary for built-in file tools, and reuse `GBrain:*` for callable wiki-tool transport settings. Do not add new top-level configuration sections.
10. Add startup validation and actionable diagnostics.
   Fail fast when dynamic tool definitions are malformed, declare a non-HTTP `runtime.type`, reference a missing egress allowlist for a non-loopback host, request an unresolvable `auth.secretRef`, collide on tool names, or violate built-in tool prerequisites (e.g. `Files.RootPath` missing). GBrain capability-precheck outcomes follow Appendix D rather than using a single fail-fast rule. Log which built-in and dynamic tools were registered, which were rejected and why, whether GBrain knowledge tools are active or degraded, whether calculation/aggregation helpers are active, and the selected `OpenAI:ToolModel` alias.
11. Add focused tests.
   Cover registry behavior, tool-to-`AIFunction` adaptation, built-in tool execution, GBrain-backed wiki tool execution, startup-time dynamic loading, duplicate-name rejection, model-alias selection for tool-capable turns, and safety boundaries such as filesystem root enforcement and HTTP egress restrictions.
12. Run manual verification through the current provider-agnostic path.
   Exercise `/v1/responses` through LiteLLM-backed models using the `tool` alias with prompts that cause `web_search`, `file_search`, `wiki_search`/`wiki_read`, deterministic calculation/aggregation helpers, and at least one user-defined HTTP tool to execute, and confirm the responses show local function/tool activity rather than provider-hosted tool requirements or the current “no live web access” / “no connected wiki tool” fallback text.

## Review Focus
- The plan preserves provider-agnostic execution through LiteLLM-backed `IChatClient` usage.
- LeanKernel-owned built-in tools are clearly distinguished from provider-hosted MAF/OpenAI tools.
- Dynamic user-defined tool loading stays within the existing top-level config shape and still results in first-class agent-visible tools.
- GBrain knowledge access is treated as callable tool/runtime behavior, not only as background memory injection.
- Deterministic calculation/aggregation helpers are added only where they close downstream capability gaps without introducing new external dependencies.
- Safety boundaries for filesystem and HTTP-based custom tools are explicit enough to implement directly.

## Appendix A: `SKILL.md` manifest schema (Phase 01)

A `SKILL.md` file begins with a YAML frontmatter block delimited by `---` lines, optionally followed by human-oriented Markdown that is ignored by the loader. Parsing is case-insensitive on keys via camelCase mapping, and unknown keys are ignored. Each declared operation becomes exactly one agent-visible tool named `{name}_{operation.id}`.

```yaml
---
name: weather                 # required; skill identifier, becomes the tool-name prefix
description: Weather lookups   # required; surfaced in each tool's description
metadata:
  category: internet          # optional; used by Agents:Tools:AllowedCategories governance
runtime:
  type: http                  # required; Phase 01 accepts "http" only ("cli" is rejected)
  baseUrl: https://api.example.com   # required for http; absolute, non-loopback
  timeoutSeconds: 30          # optional; default 30
  auth:
    type: none                # none | bearer
    secretRef: weather_token  # required when type=bearer; resolved from /run/secrets/<ref>
                              #   or the SKILL__WEATHER_TOKEN env var — never inline
  egress:
    allowHosts:               # required for any non-loopback host; requests to hosts
      - api.example.com       #   outside this list (including redirects) are rejected
operations:
  - id: current               # required; unique within the skill
    summary: Get current weather for a city   # required; surfaced to the model
    invoke:
      httpMethod: GET         # GET | POST | PUT | PATCH | DELETE
      httpPath: /v1/current/{city}   # appended to baseUrl; {placeholders} filled from args
    parameters:
      city:
        type: string          # string | integer | number | boolean
        description: City name
        required: true
---
```

Loader rules:
- Argument mapping: `{placeholder}` path segments are substituted from matching arguments and URL-escaped; remaining declared parameters become query-string values for GET/DELETE or a JSON body for POST/PUT/PATCH.
- A manifest with no valid operations, a missing `name`, or a non-`http` `runtime.type` is rejected with a logged reason.
- Duplicate resulting tool names (against built-ins or other skills) are rejected deterministically at startup.
- The effective outbound-host policy is: if `Agents:Tools:DynamicHttp:AllowHosts` is non-empty, a request host must be allowed by both the global list and the skill-local `egress.allowHosts`; if the global list is empty, the skill-local list alone governs.
- Reference implementation for behavior (not a code-copy target): `~/source/repos/leankernel/src/LeanKernel.Plugins/BuiltIn/Skills/{SkillParser,SkillDefinition,DynamicSkillTool}.cs`.

## Appendix B: Configuration contract (nested under existing sections)

No new top-level sections are introduced. All keys nest under the existing `OpenAI`, `Agents`, `Files`, and `GBrain` shape. `OpenAI:*` is reserved for model-provider behavior and routing; `Agents:*` owns agentic runtime behavior, including built-in and dynamic tools.

| Key | Purpose | Example / default |
| --- | --- | --- |
| `OpenAI:ToolModel` | LiteLLM alias used for all Phase 01 `leankernel` turns | `tool` |
| `Agents:Tools:Enabled` | Master switch / rollback lever for the tool runtime | `true` |
| `Agents:Tools:WebSearch:Provider` | Preferred web-search backend | `brave` (falls back to `duckduckgo`) |
| `Agents:Tools:WebSearch:ApiKeyEnv` | Env var holding the Brave key | `BRAVE_API_KEY` |
| `Agents:Tools:WebSearch:AllowHosts` | Egress allowlist for web search | `api.search.brave.com`, `api.duckduckgo.com` |
| `Agents:Tools:AllowedToolNames` | Name allowlist (takes precedence when non-empty) | `[]` (all names allowed) |
| `Agents:Tools:AllowedCategories` | Category allowlist (applied when name list is empty) | `[]` (all categories allowed) |
| `Agents:Tools:SkillBasePaths` | Directories scanned for `SKILL.md` at startup | `["/app/data/skills"]` |
| `Agents:Tools:DynamicHttp:AllowHosts` | Global egress ceiling layered over per-skill `egress.allowHosts` | `[]` |
| `Agents:Tools:BuiltIns:Calculation:Enabled` | Enables deterministic local calculation/aggregation helpers | `true` |
| `Agents:Tools:BuiltIns:Calculation:MaxInputItems` | Upper bound for aggregate/group/count inputs | `1000` |
| `Files:RootPath` | Filesystem boundary for `file_search` and other file tools | `./data` |
| `GBrain:BaseUrl` / `GBrain:AuthToken` / `GBrain:TimeoutSeconds` | Reused transport settings for callable `wiki_*` tools | existing values |

> Configuration ownership rule for this phase: provider/model-routing knobs stay under `OpenAI:*`; agent/tool-runtime behavior stays under `Agents:*`.

## Appendix C: Scoped tool execution (identity preservation)

Built-in and `wiki_*` tools are registered once at startup as immutable `ToolDefinition`s, but their handlers must execute with request scope so identity partitioning and per-request transports are honored. The handler captures `IServiceScopeFactory` and, on each invocation, creates a DI scope and resolves the scoped dependencies it needs (HTTP client, `IOptions<>`, the knowledge service, and the request identity/`IPermit`). This mirrors the older repo pattern (`WebSearchTool.Create(IServiceScopeFactory)`, `FileSearchTool.Create(IServiceScopeFactory)`), and it resolves the tension between a startup-registered shared registry and the gateway's scoped identity model. For `wiki_*` tools, the resolved identity determines the memory/knowledge scope, and callers pass scope-relative keys to the transport (the transport layer adds the storage scope prefix).

Deterministic `calculate` / aggregation helpers can remain singleton tool definitions because they do not require external egress, but they still execute under the same handler model for consistency. Their input contract should accept bounded numeric or structured JSON-like values and reject oversized payloads so they remain cheap, deterministic, and side-effect free.

## Appendix D: GBrain callable-capability pre-check

Because the exit criteria require callable `wiki_read`/`wiki_write`, but read/write parity in the deployed GBrain instance is not yet guaranteed, startup performs a lightweight capability probe against the GBrain MCP endpoint for the operations the knowledge service depends on (`search`, `get_page`, `put_page`; `delete_page` optional). Prefer explicit capability discovery (for example, a server-advertised tool list or MCP initialization metadata) when the deployment exposes it; otherwise use guarded probe calls with benign inputs and distinguish “tool not found / unsupported” from business-level validation failures. Outcomes:
- All required operations available and GBrain reachable → register `wiki_search`, `wiki_read`, `wiki_write` and log them active.
- GBrain reachable but missing `get_page`/`put_page` parity → register only the supported subset (at minimum `wiki_search`), optionally pair those results with deterministic local aggregation helpers for bounded count/group questions, log the degraded surface, and treat the missing tools as a documented Phase 01 contingency rather than a hard failure.
- GBrain unreachable or auth-invalid → fail closed for `wiki_*` tools with an actionable diagnostic; the rest of the runtime still starts.
- Local gateway misconfiguration for required GBrain transport settings → fail startup with a clear configuration error because the deployment is internally invalid rather than merely degraded.
