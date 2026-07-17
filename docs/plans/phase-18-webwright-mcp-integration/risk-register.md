# Phase 18 Risk Register

## Risks

| ID | Risk | Impact | Mitigation | Status |
|---|---|---|---|---|
| R1 | MCP protocol complexity may cause implementation delays if we bypass SDK guidance | Medium | Use official SDK abstractions first; only extend for documented gaps | Closed |
| R2 | Community Webwright MCP package may have maintenance or compatibility issues | Medium | Pin versions, track upstream activity, and maintain a documented future fallback path for direct Playwright | Mitigated |
| R3 | Startup-time registration of tools discovered from pre-configured MCP servers may conflict with existing tools | Low | Implement tool namespace isolation and validate uniqueness during startup | Closed |
| R4 | HTTP/SSE transport or endpoint behavior may vary across MCP servers | Low | Start with pre-configured endpoints only, validate against pinned server versions, and keep transport scope to HTTP/SSE | Mitigated |
| R5 | Removing the custom Playwright sidecar and browser-specific Playwright tool implementations may break existing tests | Medium | Comprehensive test coverage, incremental removal | Closed |
| R6 | Wrong package selection (`Core` vs full package) may increase integration cost | Medium | Default to `ModelContextProtocol`; require architecture sign-off for `Core` usage | Closed |
| R7 | Webwright-only scope may miss edge-case low-level browser controls needed by some workflows | Medium | Define clear reassessment triggers and collect evidence for potential next-phase direct Playwright enablement | Mitigated |

## Open Decisions
- None. The initial rollout is Webwright-only, discovery runs at startup, and server failures are handled by probe/discovery logging plus the standard tool runtime error path.
