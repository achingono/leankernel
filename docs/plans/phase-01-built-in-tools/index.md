# Phase 01 Tool Runtime Enablement

## Objective
Enable provider-agnostic tool execution for the `leankernel` agent behind the existing `/v1/responses` surface by introducing a LeanKernel-owned tool runtime that supports both built-in tools and custom user-defined tools. In this phase, “built-in” means stable LeanKernel tool contracts such as `web_search`, `file_search`, deterministic `calculate` / aggregation helpers, and GBrain-backed `wiki_*` knowledge tools that are executed locally through MAF function-calling primitives, not provider-hosted OpenAI/Foundry tools whose availability depends on the upstream model provider. Phase 01 is intentionally limited to `web_search`, `file_search`, `calculate`, lightweight aggregation tools, `wiki_search`, `wiki_read`, `wiki_write`, and startup-only `SKILL.md`-defined HTTP user tools.

## Scope
This phase covers the runtime plan needed to move the gateway from a chat client with function invocation enabled but no registered tools to a real shared tool runtime with built-in registry entries, GBrain-backed knowledge tools, startup-only `SKILL.md`-based HTTP user-tool loading, and agent wiring that remains provider-agnostic through LiteLLM or any other `IChatClient`-compatible backend. For Phase 01, all `leankernel` agent turns use the configured tool-capable model alias rather than introducing per-turn model selection. It does not include unrelated UX work or a broad redesign of conversation persistence.

## In Scope
- Confirm the current no-tool behavior from container logs, current code paths, and the older LeanKernel implementation.
- Introduce shared tool contracts and registration for LeanKernel-owned built-in tools.
- Add a callable GBrain-backed knowledge service/tool surface rather than relying only on background memory/context injection.
- Support user-defined tools loaded at startup from user-managed `SKILL.md` definitions, with one registered tool per declared operation.
- Keep the execution path provider-agnostic by using MAF `AIFunction` / `ChatOptions.Tools` rather than provider-hosted web/file tools.
- Add configuration under the existing `OpenAI`, `Agents`, `Files`, and `GBrain` sections for model-provider settings, agent/tool behavior, web-search backend, tool discovery, allowlisting, filesystem boundaries, and GBrain knowledge access, with the full nested contract in `activities.md` Appendix B.
- Gate the whole tool runtime behind `Agents:Tools:Enabled` so it can be disabled to restore the current no-tool chat path.
- Add startup validation, diagnostics, and automated coverage for built-in and dynamic tool execution.
- Add operator documentation for configuring, governing, and smoke-testing the tool runtime.

## Out of Scope
- New top-level configuration sections outside the current repo shape.
- Building a full end-user document upload UI.
- Reworking `app.MapOpenAIResponses()` away from the current no-argument path.
- Provider-hosted OpenAI/Foundry `web_search`, `file_search`, and `code_interpreter` as a required solution path.
- CLI-based user-defined tools in Phase 01.
- Hot-reload of dynamic tool definitions after startup in Phase 01.
- General memory pipeline redesign or unrelated persistence work.

## Entry Criteria
- Gateway and logic projects build on the current `Microsoft.Agents.AI*` and `OpenAI` package set.
- The older `~/source/repos/leankernel` tool implementation is captured as behavioral reference, not as a code-copy target.
- The current LiteLLM-backed route and `UseFunctionInvocation()` path are documented so the phase can preserve provider-agnostic behavior.

## Exit Criteria
The named `leankernel` agent can execute LeanKernel-owned built-in tools and user-defined runtime tools through `/v1/responses`, the execution path remains provider-agnostic across LiteLLM-backed models, and the gateway fails fast with clear errors when tool definitions, governance, or filesystem/runtime prerequisites are invalid. See `exit-criteria.md`.

## Roles
- Owner: OpenCode
- Reviewer: separate agent session / model review
- Approver: repository owner
