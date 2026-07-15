# AGENTS.md

Contributor and coding-agent guide for this repository.

## Workspace Snapshot

- Source of truth: current worktree at `~/source/worktrees/leankernel`
- Full solution: [`LeanKernel.sln`](LeanKernel.sln)
- App-only solution: [`src/LeanKernel.sln`](src/LeanKernel.sln)
- Primary runtime app: [`src/Services/LeanKernel.Gateway`](src/Services/LeanKernel.Gateway)
- Docs home: [`docs/index.md`](docs/index.md)

Current implemented project map: [`docs/architecture/solution-structure.md`](docs/architecture/solution-structure.md)

## Canonical References

| Topic | Canonical doc |
| --- | --- |
| Getting started | [`docs/getting-started/index.md`](docs/getting-started/index.md) |
| Architecture and boundaries | [`docs/architecture/index.md`](docs/architecture/index.md) |
| Runtime features | [`docs/features/index.md`](docs/features/index.md) |
| API surface | [`docs/api/index.md`](docs/api/index.md) |
| Configuration | [`docs/configuration/index.md`](docs/configuration/index.md) |
| Development workflows | [`docs/development/index.md`](docs/development/index.md) |
| Operations | [`docs/operations/index.md`](docs/operations/index.md) |
| Decisions | [`docs/decisions/index.md`](docs/decisions/index.md) |
| Planning templates | [`docs/templates/`](docs/templates/) |

## Working Rules

- Keep behavior feature-local to the owning project.
- Keep transport, composition, and host concerns in `LeanKernel.Gateway`; keep reusable runtime logic in `src/Common`.
- Reuse existing contracts in `LeanKernel.Core.Interfaces`, `LeanKernel.Logic`, and `LeanKernel.Data` before adding new abstractions.
- Preserve the current configuration shape: `ConnectionStrings`, `OpenAI`, `Agents`, `Identity`, `Files`, `Cors`, and `GBrain`.
- Avoid broad exception swallowing; log actionable context.

## Required Change Workflow

When implementing user-requested changes:

1. Copy the relevant blank files from [`docs/templates/`](docs/templates/) into a new folder under [`docs/plans/`](docs/plans/).
2. Draft a concrete implementation plan in that folder.
3. Review the plan with a different model/session before implementation.
4. Implement the change and associated tests.
5. Ensure code coverage is at-least 80%.
6. Run verification appropriate to the scope.
7. Run `scripts/quality/sonarqube-scan.sh` and address all `Blocker`, `Critical`, and `Major` issues reported.
8. Run a [deep review](.agents/prompts/deep-review.prompt.md) sub-agent and address all issues reported.
9. Update documentation to reflect current implementation state.

## Planning Templates

The reusable blank planning templates live under [`docs/templates/`](docs/templates/):

- [`index.md`](docs/templates/index.md)
- [`inputs.md`](docs/templates/inputs.md)
- [`activities.md`](docs/templates/activities.md)
- [`outputs.md`](docs/templates/outputs.md)
- [`exit-criteria.md`](docs/templates/exit-criteria.md)
- [`risk-register.md`](docs/templates/risk-register.md)
- [`evidence.md`](docs/templates/evidence.md)

## Durable Repo-Specific Guidance

- Use this worktree, not `~/source/repos/leankernel`, as the current implementation source of truth.
- For MAF persistence and provider work, read session state from the invocation context rather than constructor-injecting `AgentSession`-style state.
- Keep `app.MapOpenAIResponses()` on the current no-argument path unless you are deliberately reworking named-agent registration and have verified the endpoint semantics.
- Preserve identity partitioning across transcript history, agent state, and memory scope. Related runtime details: [`docs/features/identity-partitioning.md`](docs/features/identity-partitioning.md)
- Pass scope-relative keys to `GBrainMemoryClient`; storage scope is added by the transport layer. Related runtime details: [`docs/features/memory-pipeline.md`](docs/features/memory-pipeline.md), [`docs/decisions/0004-keep-gbrain-transport-in-gateway.md`](docs/decisions/0004-keep-gbrain-transport-in-gateway.md)
- When testing EF-backed providers such as `DbChatHistoryProvider`, prefer SQLite in-memory over EF InMemory when relational behavior matters.
- Verify MAF package surface area before planning conversation durability extensions; documented APIs and compiled public APIs may differ by package version.
