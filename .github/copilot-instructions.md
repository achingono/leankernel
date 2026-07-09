# Copilot Instructions for LeanKernel

These instructions are for contributors and coding agents working in this repository.

## Project Context

- LeanKernel is a modular monolith on **.NET 10** (`src/LeanKernel.sln`).
- Domain ownership:
  - `LeanKernel.Abstractions`: shared contracts/models/config
  - `LeanKernel.Agents`: turn orchestration, routing, and runtime strategies
  - `LeanKernel.Context`: context assembly, history shaping, and retrieval scoping
  - `LeanKernel.Knowledge`: wiki/retrieval integration
  - `LeanKernel.Persistence`: session/diagnostics persistence
  - `LeanKernel.Tools` + `LeanKernel.Plugins`: built-in tools and dynamic runtime skills
  - `LeanKernel.Channels`: channel ingress/routing and adapters
  - `LeanKernel.Diagnostics`: metrics and diagnostics services
  - `LeanKernel.Scheduler`: scheduled execution
  - `LeanKernel.Gateway`: API, Blazor UI, auth, onboarding, and composition

## Coding Expectations

- Keep changes **feature-local**; do not move domain behavior into `LeanKernel.Gateway` unless it is composition/UI/API-only.
- Reuse existing contracts in `LeanKernel.Abstractions.Interfaces` before introducing new abstractions.
- Prefer small collaborators over growing orchestration classes.
- Preserve existing naming patterns and configuration binding style (`LeanKernel:*`).
- Avoid broad exception swallowing; log and surface actionable errors.

## Mandatory Issue/Bug/Change Workflow

When a user highlights an issue, bug, or requested change in the system, the agent **must** follow this process in order:

1. Create a concrete implementation plan.
2. Review that plan using a different model than the one doing implementation.
3. Save the reviewed plan as a PRD under `docs/plans` before any code changes.
4. Implement the change, run tests, and run Sonar scans.
5. Iterate on fixes until all quality gates pass.

Do not skip, reorder, or partially apply these steps.

## Build, Test, and Quality Commands

Run from repo root unless noted:

```bash
dotnet restore src/LeanKernel.sln
dotnet build src/LeanKernel.sln --no-restore -v minimal
dotnet test src/LeanKernel.sln --no-build -v minimal
```

Coverage and quality:

```bash
scripts/quality/test-coverage.sh
scripts/quality/sonarqube-scan.sh
```

## Configuration and Runtime Notes

- Main app config is bound from `src/LeanKernel.Gateway/appsettings.json` + runtime overlay in data directory.
- Docker compose stack includes: `engine`, `database`, `litellm`, `gbrain`, `signal`.
- LiteLLM config is authored in `config/litellm/config.yaml` and compiled at startup by `config/litellm/render_litellm_config.py`.

## Skills and Tools

- Dynamic skills are loaded from `LeanKernel:Skills:BasePaths` (default `/app/data/skills`) by `SkillHostedService`.
- Skills are defined in `SKILL.md` files parsed by `SkillParser`.
- Built-in tools are registered in `LeanKernelFeatureServiceCollectionExtensions.AddPlugins`.

## Documentation Hygiene

- If behavior/config/API changes, update:
  - `README.md`
  - relevant `docs/*` pages (especially `docs/features`, `docs/skills`, `docs/development`)
- Keep docs implementation-accurate; label forward-looking work as plan/roadmap explicitly.
