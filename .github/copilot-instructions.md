# Copilot Instructions for LeanKernel

These instructions are for contributors and coding agents working in this repository.

## Project Context

- LeanKernel is a modular monolith on **.NET 10** (`src/LeanKernel.sln`).
- Domain ownership:
  - `LeanKernel.Core`: shared contracts/models/config
  - `LeanKernel.Commander`: channels + durable outbound queue
  - `LeanKernel.Thinker`: turn orchestration, routing, agent strategies
  - `LeanKernel.Archivist`: sessions, wiki, context gating, retrieval
  - `LeanKernel.Plugins`: built-in tools and dynamic runtime skills
  - `LeanKernel.Host`: API, Blazor UI, auth, onboarding, composition

## Coding Expectations

- Keep changes **feature-local**; do not move domain behavior into `LeanKernel.Host` unless it is composition/UI/API-only.
- Reuse existing contracts in `LeanKernel.Core.Interfaces` before introducing new abstractions.
- Prefer small collaborators over growing orchestration classes.
- Preserve existing naming patterns and configuration binding style (`LeanKernel:*`).
- Avoid broad exception swallowing; log and surface actionable errors.

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

- Main app config is bound from `src/LeanKernel.Host/appsettings.json` + runtime overlay in data directory.
- Docker compose stack includes: `engine`, `database`, `litellm`, `qdrant`, `unstructured`, `indexer`, `signal`.
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
