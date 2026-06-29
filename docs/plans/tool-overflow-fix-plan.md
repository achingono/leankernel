# Fix: LeanKernel Tool Overflow (129 tools exceeding Azure 128 limit)

## Problem

LeanKernel registers **129 tools** per chat request:

| Source | Count |
|--------|-------|
| Built-in tools (22 base + 4 webwright) | 26 |
| Image skills (`data/skills/`: 7 skill packages) | 54 |
| NFS skills (`swarm/deploy/leankernel/skills/`: 5 skill packages) | 49 |
| **Total** | **129** |

Azure OpenAI enforces `max_tools = 128`. All 129 are passed to `ChatOptions.Tools` in `AgentInvocationBuilder.BuildOptions()` (`src/LeanKernel.Agents/Strategies/AgentInvocationBuilder.cs:43`) with no cap, triggering `Invalid 'tools': array too long. Expected an array with maximum length 128` from `platform_litellm`. The error message propagates as `"cannot reach the configured model provider"` — a misleading 500 to the user.

## Solution

Two-pronged approach:

### A. Deduplicate HTTP skills → CLI skills

Remove HTTP-based skills from the Docker image that have CLI-based equivalents on the NFS volume, reducing tool count from **129 → 112**.

| Remove from `data/skills/` | Reason | Ops removed |
|---------------------------|--------|-------------|
| `ms-todo` | Replaced by `ms-todo-cli` on NFS | 13 |
| `simplefin` | Replaced by `simplefin-cli` on NFS | 4 |
| **Total removed** | | **17** |

### B. Configurable cap + smart selection safety net

1. Add `LiteLlmConfig.MaxTools = 128` (env: `LEANKERNEL__LITELLM__MAXTOOLS`)
2. When tools exceed `MaxTools`, invoke the economy-tier small model to select the most relevant subset — keeps the user's intent in focus while staying under the limit
3. Log warnings at threshold and on actual truncation

## Implementation Checklist

### Part A — Deduplicate HTTP/CLI skills

- [ ] Delete `leankernel/data/skills/ms-todo/SKILL.md`
- [ ] Delete `leankernel/data/skills/simplefin/SKILL.md`
- [ ] Verify `blogs/MS_TODO.md` (if it exists) imports the CLI skill name, not the HTTP one
- [ ] Confirm `simplefin-cli` covers all 4 operations that `simplefin` had; if `setup` is missing, consider adding it to `simplefin-cli/SKILL.md`
- [ ] Rebuild the engine Docker image and re-deploy (to remove stale skills)
- [ ] Verify via `docker exec <container> ls /app/data/skills/` that `ms-todo` and `simplefin` are gone

### Part B.1 — Config property

- [ ] Add `int MaxTools { get; set; } = 128;` and `int ToolSelectionTimeoutMs { get; set; } = 5000;` to `src/LeanKernel.Abstractions/Configuration/LiteLlmConfig.cs`
- [ ] Wire `IOptions<LiteLlmConfig>` into callers that need it

### Part B.2 — Tool selection service

- [ ] Create `src/LeanKernel.Agents/ToolSelection/IToolSelector.cs`:
  ```csharp
  namespace LeanKernel.Agents.ToolSelection;
  public interface IToolSelector
  {
      Task<IReadOnlyList<ToolDefinition>> SelectToolsAsync(
          string userMessage,
          IReadOnlyList<ToolDefinition> allTools,
          IReadOnlyList<ChatMessage> chatHistory, // Added for active tool preservation
          int maxTools,
          CancellationToken ct);
  }
  ```
- [ ] Create `src/LeanKernel.Agents/ToolSelection/ToolSelector.cs`:
  - Accept `IOptions<LiteLlmConfig>` and `IOptions<RoutingConfig>` and `IHttpClientFactory`
  - **Robustness Check**: Add `bool IsRequired` to `ToolDefinition` for core tools (e.g., terminal, filesystem). Filter these out before sending to the LLM and automatically include them in the final list.
  - **Preserve Active Tools**: Scan `chatHistory` for recent tool calls and ensure those tools are also automatically included to prevent hallucination or API errors when handling tool results.
  - Build compact tool manifest (name + 1-line description only, skip JSON schema) for the remaining optional tools.
  - Send to economy model (`RoutingConfig.Economy.Model`, typically `"small"`) with `response_format = { "type": "json_object" }` (Structured Outputs) asking for a JSON object containing an array of selected tool names to guarantee reliable parsing.
  - Parse response as `string[]`
  - Filter `allTools` to match; fall back to smart prioritization (Built-in > Active > Recent > others) if parsing or LLM fails.
  - Set `max_tokens: 256`, timeout using `LiteLlmConfig.ToolSelectionTimeoutMs` (default 5s).
- [ ] Register in DI: `services.AddSingleton<IToolSelector, ToolSelector>()`

### Part B.3 — Integrate into TurnPipeline

- [ ] In `TurnPipeline.ProcessAsync()` (around line 135), after `GetVisibleTools()`:
  ```csharp
  if (visibleTools.Count > _config.LiteLlm.MaxTools)
  {
      _logger.LogWarning(
          "Tool count {Count} exceeds MaxTools ({Max}). Selecting relevant subset.",
          visibleTools.Count, _config.LiteLlm.MaxTools);
      visibleTools = await _toolSelector.SelectToolsAsync(
          turnScopedMessage.Content,
          visibleTools,
          turnScopedMessage.History, // Pass chat history for active tool preservation
          _config.LiteLlm.MaxTools,
          ct);
  }
  ```

### Part B.4 — Logging improvements

- [ ] Log a **warning** at 90%+ of MaxTools for proactive signal
- [ ] Log **info** with selected count vs total when selection runs
- [ ] Log **error** if tool selection call fails (fall back to first N)

### Part B.5 — Configure env vars

- [ ] Add `LEANKERNEL__LITELLM__MAXTOOLS` to `swarm/deploy/leankernel/.env` (or keep default 128)
- [ ] Add `LEANKERNEL__LITELLM__MAXTOOLS=128` to `docker-stack.yml` engine service environment block if config propagation is needed
- [ ] Verify `docker-stack.yml` passes the new env var

### Verification

- [ ] Build and deploy the engine image
- [ ] `docker service logs leankernel_engine --tail 50` — check for tool count log lines
- [ ] Send a chat request with a normal user message; confirm 200 response
- [ ] Force tool selection path by temporarily setting `LEANKERNEL__LITELLM__MAXTOOLS=80`; verify the economy model call appears in logs
- [ ] Reset to 128; confirm normal operation

## Risk / Mitigation

| Risk | Mitigation |
|------|-----------|
| Small-model selection adds latency (~500ms–2s) | Configurable timeout (`ToolSelectionTimeoutMs`) + fallback; only triggers above threshold |
| Parsing errors drop all tools | **Use JSON Mode / Structured Outputs** to guarantee format. Fallback to smart prioritization instead of registration order. |
| `simplefin` missing `setup` op | Audit `simplefin-cli`; add if needed |
| Built-in tools filtered out by small model | **Implement `ToolDefinition.IsRequired` immediately** so core tools are never passed to the LLM for selection and are unconditionally preserved. |
| Active tools dropped mid-conversation | **Preserve active tools** from `chatHistory` so the model always has access to tools it just called. |
| Config not propagated to container | Verify the env var is in `docker-stack.yml` and `deploy.sh` passes it |

## Architecture diagram (text)

```
User message
    │
    ▼
TurnPipeline.ProcessAsync()
    │
    ├── GetVisibleTools() → 129 tools
    │
    ├── visibleTools.Count > MaxTools? ──Yes──► ToolSelector.SelectToolsAsync()
    │                                                │
    │                                                ├── Build compact manifest
    │                                                ├── Call economy model (small)
    │                                                ├── Parse JSON response
    │                                                └── Return ≤ MaxTools tools
    │                                                │
    │                                          ◄─────┘
    │
    ├── AgentInvocationBuilder.BuildOptions()
    │       Tools = [.. context.Tools]
    │
    ▼
StaticAgentStrategy → chatClient.GetResponseAsync()
    │
    ▼
platform_litellm → Azure OpenAI (≤128 tools ✓)
```
