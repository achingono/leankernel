# Phase 01 Outputs

## Mandatory Outputs

| Output | Description | Format |
| --- | --- | --- |
| Shared tool runtime design | Concrete implementation notes for registry, execution, and MAF function wiring for built-in and dynamic tools | Markdown + code changes |
| Built-in tool contract | Stable LeanKernel-owned built-in tool definitions, starting with `web_search`, `file_search`, deterministic calculation/aggregation helpers, and `wiki_*` knowledge tools | C# code + docs |
| Deterministic calculation/aggregation helpers | Local, bounded tools for arithmetic, count, group, and aggregate summarization when downstream services do not provide native endpoints or tools | C# code + docs |
| Dynamic tool loading design | Startup-only `SKILL.md` discovery and one-tool-per-operation registration flow for user-defined HTTP tools | Markdown + code changes |
| `SKILL.md` manifest schema | Canonical Phase 01 frontmatter schema with runtime/egress/auth/operations and one-tool-per-operation naming | Markdown (Appendix A) + parser code |
| Configuration contract | Nested `OpenAI` / `Agents` / `Files` / `GBrain` settings for tool model alias, web-search backend, discovery paths, allowlists, callable knowledge tools, and safety validation | C# options + appsettings docs (Appendix B) |
| Scoped-execution + safety design | Per-invocation DI scoping for identity preservation, filesystem root-confinement, egress allowlisting/redirect re-validation, and secretRef resolution | Markdown (Appendices C–D) + code changes |
| GBrain knowledge integration design | Dedicated callable knowledge-service abstraction, capability pre-check, and tool wiring over the existing GBrain MCP transport | Markdown + code changes |
| Verification evidence | Test output and manual smoke-test notes covering built-in, GBrain knowledge, and user-defined tool execution | Markdown |

## Optional Outputs
- A follow-on plan for additional built-ins such as deterministic browser or code-execution tools if still needed after Phase 01.

## Output Quality Checklist
- [ ] All mandatory outputs produced
- [ ] All outputs reviewed before gate
- [x] Evidence log updated with output references (reviewer pass; see `evidence.md` and Appendices A–D)
