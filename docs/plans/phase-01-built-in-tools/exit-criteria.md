# Phase 01 Exit Criteria

## Gate Checklist
- [x] The named `leankernel` agent can execute registered tools through the existing provider-agnostic `UseFunctionInvocation()` path.
- [x] All Phase 01 `leankernel` agent turns use the configured LiteLLM-backed `OpenAI:ToolModel` alias rather than the current plain `medium` route.
- [x] LeanKernel-owned built-in `web_search` is registered and executes without relying on provider-hosted web search support, using the configured backend (Brave with DuckDuckGo fallback) and its pinned egress allowlist.
- [x] LeanKernel-owned built-in `file_search` is registered and executes within the `Files.RootPath` boundary, with path-confinement rejecting traversal outside the root.
- [x] Deterministic built-in calculation/aggregation helpers are registered for arithmetic, count, grouping, and bounded aggregate summarization where downstream services do not expose native tools or endpoints.
- [x] Built-in and `wiki_*` tools execute under a per-request DI scope so request identity/partitioning is preserved at invocation time (Appendix C), not captured at startup.
- [x] The GBrain callable-capability pre-check (Appendix D) runs at startup and its outcome (active, degraded subset, or failed-closed) is logged.
- [x] GBrain-backed `wiki_search` is registered as a callable tool rather than existing only through background memory/context enrichment; `wiki_read` and `wiki_write` are registered when the pre-check confirms `get_page`/`put_page` parity, and their absence is a documented contingency rather than a silent gap.
- [x] The agent can answer wiki/document availability questions by invoking callable GBrain-backed knowledge tools when GBrain is configured, reachable, and capability-validated; exact count/list answers are required only when the downstream capability probe confirms a native list/count surface or a bounded result set suitable for deterministic local aggregation.
- [x] At least one user-defined HTTP runtime tool can be loaded from a `SKILL.md` conforming to Appendix A, registered as a first-class `{skill}_{operation}` tool, and executed successfully.
- [x] Duplicate dynamic tool names are rejected deterministically at startup.
- [x] Dynamic HTTP tools enforce per-skill `egress.allowHosts`, block loopback/private/link-local targets and unlisted redirect hops, use global-vs-local allowlist intersection semantics, and resolve bearer secrets only via `auth.secretRef` (no inline secrets).
- [x] Tool-related configuration remains under the existing `OpenAI`, `Agents`, `Files`, and `GBrain` top-level sections.
- [x] `Agents:Tools:Enabled=false` cleanly disables the tool runtime and restores the current no-tool chat path (rollback lever verified).
- [x] Startup validation rejects malformed dynamic tool definitions, non-HTTP `runtime.type`, forbidden HTTP targets, unresolvable secret references, duplicate names, invalid GBrain knowledge prerequisites, and invalid built-in tool prerequisites with actionable errors.
- [x] Automated tests cover registry behavior, MAF tool adaptation, built-in execution, GBrain knowledge-tool execution, startup-only dynamic loading, model-alias selection, duplicate-name rejection, and safety boundaries (filesystem root, egress allowlist/redirect, secretRef resolution).
- [x] Operator documentation explains built-in versus GBrain-backed versus user-defined tools, the `SKILL.md` schema, configuration, and smoke-test steps.

## Approval Table

| Role | Name | Status | Notes |
| --- | --- | --- | --- |
| Owner | OpenCode | Completed | DI fix, operator docs, sample SKILL.md added |
| Reviewer | Separate agent session | Revised | Independent review completed; plan expanded with SKILL.md schema (Appendix A), config contract (Appendix B), scoped-execution model (Appendix C), and GBrain capability pre-check (Appendix D). Ready for approver review. |
| Approver | Repository owner | Pending | |
