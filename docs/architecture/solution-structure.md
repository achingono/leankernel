# Solution Structure

This reference documents the current implemented project boundaries in `src/LeanKernel.sln`.

## Project responsibilities

| Project | Owns | Pairings |
|---------|------|----------|
| `LeanKernel.Abstractions` | Shared config models, interfaces, enums, DTOs | Referenced by all runtime projects |
| `LeanKernel.Agents` | Turn pipeline execution, runtime strategy selection, routing/orchestration, response quality/enhancement | Uses `Context`, `Tools`, diagnostics sinks |
| `LeanKernel.Context` | Context budget/token logic, retrieval scoping, history shaping, identity-aware context assembly | Uses `Knowledge` and persistence-backed session/history data |
| `LeanKernel.Knowledge` | GBrain MCP client and `IKnowledgeService` implementation | Used by `Context`, tools, and Gateway UI services |
| `LeanKernel.Tools` | Built-in tools, governance policy, tool registry/executor, document ingestion services | Extended at runtime by `LeanKernel.Plugins` skills |
| `LeanKernel.Plugins` | Runtime skill loading (`SKILL.md` parsing/validation) and dynamic tool registration | Complements `Tools` during Gateway startup |
| `LeanKernel.Persistence` | EF Core DbContext, Postgres session store, diagnostics/doc-ingestion persistence | Used by runtime and Gateway composition bootstrap |
| `LeanKernel.Diagnostics` | Metrics counters and diagnostics services/models plumbing | Consumed by runtime, middleware, and APIs |
| `LeanKernel.Channels` | Channel routing/auth and hosted channel lifecycle (including optional Signal adapter) | Sends inbound messages into runtime |
| `LeanKernel.Learning` | Background learning queue/worker and extraction steps | Runs alongside runtime using turn events |
| `LeanKernel.Scheduler` | Cron schedule evaluation and background job execution host | Runs periodic jobs through runtime services |
| `LeanKernel.Gateway` | ASP.NET Core composition root, minimal APIs, Blazor UI, auth, middleware, health endpoints | Hosts and wires all runtime projects |

## Gateway vs Host

- `LeanKernel.Gateway` is the active web host in the solution and contains the current HTTP API and UI runtime.
- `LeanKernel.Host` directory exists under `src/` as scaffold/legacy source layout, but it is not included as a project in `src/LeanKernel.sln` and currently does not provide active controllers/endpoints.

## Dependency guidance

- Keep reusable contracts in `LeanKernel.Abstractions`.
- Keep transport/composition concerns in `LeanKernel.Gateway`; keep domain behavior in runtime projects.
- Keep persistence access in `LeanKernel.Persistence` rather than API/UI projects.
- Keep tool execution in `LeanKernel.Tools`; dynamic skill discovery belongs in `LeanKernel.Plugins`.
- Avoid adding new feature logic to scaffold-only folders that are not active solution projects.
