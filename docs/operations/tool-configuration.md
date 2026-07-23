# Tool Configuration

Operational guidance for configuring, governing, and troubleshooting the tool runtime.

## Enabling and Disabling

The tool runtime is gated by `Agents:Tools:Enabled` (default `true`). Setting it to `false`
completely disables tool registration and restores the no-tool chat path.

```json
{
  "Agents": {
    "Tools": {
      "Enabled": false
    }
  }
}
```

## Web Search

`web_search` uses Brave Search when the `BRAVE_API_KEY` environment variable is set, and
falls back to DuckDuckGo otherwise. The runtime currently keys off `ApiKeyEnv` availability; `Provider` is present in config but not actively used as a strict selector.

Example configuration:

```json
{
  "Agents": {
    "Tools": {
      "WebSearch": {
        "Provider": "brave",
        "ApiKeyEnv": "BRAVE_API_KEY",
        "AllowHosts": ["api.search.brave.com", "api.duckduckgo.com"]
      }
    }
  }
}
```

## MCP Servers

MCP tools are discovered at startup from pre-configured endpoints under
`Agents:Tools:McpServers`. Webwright MCP-first browser tooling is exposed through the
gateway tool runtime.

The current implementation uses the official `ModelContextProtocol` SDK, registers discovered
tools as LeanKernel-owned adapters, and invokes each browser tool through a fresh MCP client
per call.

```json
{
  "Agents": {
    "Tools": {
      "McpServers": [
        {
          "Name": "webwright",
          "Endpoint": "http://webwright:8000/mcp",
          "Enabled": true,
          "TransportMode": "AutoDetect",
          "ConnectionTimeoutSeconds": 30,
          "Required": false
        }
      ]
    }
  }
}
```

## Internet Tool Limits

`web_fetch` and `http_request` use `Agents:Tools:Internet:MaxRedirects` to bound redirect
chasing and re-validate each hop against egress controls.

## Governance Allowlists

Restrict which tools the agent can see by name or category:

```json
{
  "Agents": {
    "Tools": {
      "AllowedToolNames": ["calculate", "web_search"],
      "AllowedCategories": []
    }
  }
}
```

When `AllowedToolNames` is non-empty it takes precedence. An empty list in both fields
means all registered tools are visible.

## Adding User-Defined Tools

1. Create a `SKILL.md` file following the manifest schema in
   [tool-runtime](../features/tool-runtime.md)
2. Place it in one of the directories listed in `Agents:Tools:SkillBasePaths`
3. Restart the gateway -- tools are loaded at startup only (no hot-reload)
4. Confirm registration in the startup log

For a working example, see [`../../samples/skills/weather.Skill.md`](../../samples/skills/weather.Skill.md).

## Egress Security

Dynamic HTTP tools enforce a layered host allowlist:

- Per-skill `egress.allowHosts` controls which hosts that skill may call
- Global `Agents:Tools:DynamicHttp:AllowHosts` adds a ceiling; when non-empty, a request
  host must appear in both lists
- Loopback, private (10.x, 172.16-31.x, 192.168.x), and link-local (169.254.x) hosts are
  always blocked
- Redirect hops are re-validated against the allowlist

## GBrain Knowledge Tools

`memory_search`, `memory_read`, and `memory_write` are registered conditionally based on a
startup capability probe. Check the startup log for:

- `GBrain capability pre-check complete: Full` -- all tools active
- `GBrain capability pre-check complete: Degraded` -- partial tool surface
- `Memory unavailable` -- no memory tools registered

If GBrain is misconfigured (missing required transport settings), startup fails with a clear
error.

## Troubleshooting

| Symptom | Likely cause | Fix |
|---------|-------------|-----|
| No tools registered | `Agents:Tools:Enabled` is `false` | Set to `true` |
| `memory_*` tools missing | GBrain unreachable or capability probe failed | Check `GBrain:BaseUrl` and GBrain container health |
| Dynamic tool not loaded | Parse failure or invalid `SKILL.md` | Check startup warnings for the specific file |
| `web_search` returns error | Brave API key missing/invalid | Set `BRAVE_API_KEY` env var or verify key |
| Tool execution returns "Access denied" | File path outside `Files:RootPath` | Ensure target files are within the allowed root |
| MCP tools missing at startup | MCP endpoint unreachable or discovery failed | Verify `Agents:Tools:McpServers:*` config and MCP server health |
| Webwright browser tool calls fail with cancellation | Gateway was built before the fresh-client MCP invocation fix or the container is stale | Rebuild and restart gateway, then retry the request |
